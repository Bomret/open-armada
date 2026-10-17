using System;
using System.Buffers.Binary;
using System.Text;

namespace WavLevel;

// Peak and RMS of a window, in dB relative to full scale; -inf for silence.
public sealed record Level(double PeakDb, double RmsDb);

public static class WavLevelMeter
{
    // Measures a Movie Maker WAV (32-bit integer PCM) given as bytes over [from, to] seconds, which default to the
    // whole audio. The result holds the level or a message naming the problem.
    public static (Level? Level, string? Problem) Measure(byte[] wav, double? from = null, double? to = null)
    {
        if (wav.Length < 12 || Tag(wav, 0) != "RIFF" || Tag(wav, 8) != "WAVE")
            return (null, "not a WAV file (no RIFF/WAVE header)");

        int format = 0,
            channels = 0,
            rate = 0,
            bits = 0;
        var haveFormat = false;
        long pos = 12;
        while (pos + 8 <= wav.Length)
        {
            var id = Tag(wav, (int)pos);
            var size = BinaryPrimitives.ReadUInt32LittleEndian(wav.AsSpan((int)pos + 4));
            var body = (int)pos + 8;
            if (id == "fmt " && size >= 16 && body + 16 <= wav.Length)
            {
                format = BinaryPrimitives.ReadUInt16LittleEndian(wav.AsSpan(body));
                channels = BinaryPrimitives.ReadUInt16LittleEndian(wav.AsSpan(body + 2));
                rate = BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(body + 4));
                bits = BinaryPrimitives.ReadUInt16LittleEndian(wav.AsSpan(body + 14));
                haveFormat = true;
            }
            else if (id == "data")
            {
                if (!haveFormat)
                    return (null, "not a WAV file (data before the format)");
                if (format != 1)
                    return (null, $"not PCM (format tag {format}); only plain PCM is read");
                if (bits != 32)
                    return (null, $"{bits}-bit PCM; only 32-bit PCM is read");
                if (channels < 1 || rate < 1)
                    return (null, "not a WAV file (bad channel count or sample rate)");
                if (size != wav.Length - body)
                    return (null, $"unfinished file (data size {size} but {wav.Length - body} bytes follow)");
                return Window(wav.AsSpan(body, (int)size), channels, rate, from, to);
            }
            // Long arithmetic: size is uint, and a uint sum wraps to a zero or short advance.
            pos += 8L + size + (size & 1);
        }
        return (null, "not a WAV file (no data chunk)");
    }

    static (Level?, string?) Window(ReadOnlySpan<byte> data, int channels, int rate, double? from, double? to)
    {
        var frames = data.Length / (4 * channels);
        var duration = (double)frames / rate;
        var start = from ?? 0;
        var end = to ?? duration;
        if (!double.IsFinite(start) || !double.IsFinite(end))
            return (null, "--from and --to need finite seconds");
        if (start < 0 || start > duration || end < 0 || end > duration)
            return (
                null,
                FormattableString.Invariant($"window {start} to {end} s is outside the audio (0 to {duration:0.###} s)")
            );
        if (start > end)
            return (null, "--from is after --to");
        var first = (int)Math.Round(start * rate);
        var last = (int)Math.Round(end * rate);
        if (last <= first)
            return (null, "window holds no samples");

        var peak = 0.0;
        var sum = 0.0;
        var count = (last - first) * channels;
        for (var i = first * channels; i < last * channels; i++)
        {
            var v = Math.Abs(BinaryPrimitives.ReadInt32LittleEndian(data[(i * 4)..]) / 2147483648.0);
            peak = Math.Max(peak, v);
            sum += v * v;
        }
        return (new Level(Db(peak), Db(Math.Sqrt(sum / count))), null);
    }

    static string Tag(byte[] bytes, int at) => Encoding.ASCII.GetString(bytes, at, 4);

    static double Db(double amplitude) => 20 * Math.Log10(amplitude);
}
