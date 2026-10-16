using System;
using System.IO;

namespace OpenArmada.Formats;

public sealed record WavInfo(int SampleRate, int Channels, int BitsPerSample, byte[] Data);

// Reads a PCM WAV and checks that its header agrees with its data: format fields among
// themselves, and the data chunk's declared size against the bytes that follow.
// Only uncompressed PCM of 8 or 16 bits is read; any other encoding is unsupported.
public static class Wav
{
    public static WavInfo Read(byte[] d)
    {
        if (d.Length < 12 || !Is(d, 0, "RIFF") || !Is(d, 8, "WAVE"))
            throw new InvalidDataException("not a RIFF/WAVE file");
        int rate = 0,
            channels = 0,
            bits = 0;
        var haveFmt = false;
        for (long pos = 12; pos + 8 <= d.Length; )
        {
            var size = BitConverter.ToUInt32(d, (int)pos + 4);
            var body = pos + 8;
            if (Is(d, (int)pos, "fmt "))
            {
                if (size < 16 || body + 16 > d.Length)
                    throw new InvalidDataException($"fmt chunk is cut off or short ({size} bytes)");
                var b = (int)body;
                int tag = BitConverter.ToUInt16(d, b),
                    byteRate = BitConverter.ToInt32(d, b + 8),
                    align = BitConverter.ToUInt16(d, b + 12);
                channels = BitConverter.ToUInt16(d, b + 2);
                rate = BitConverter.ToInt32(d, b + 4);
                bits = BitConverter.ToUInt16(d, b + 14);
                if (tag != 1)
                    throw new UnsupportedFormatException($"WAV format tag 0x{tag:X} is not PCM");
                if (bits != 8 && bits != 16)
                    throw new UnsupportedFormatException(
                        $"WAV with {bits} bits per sample is not read (only 8 and 16)"
                    );
                if (channels > 2)
                    throw new UnsupportedFormatException(
                        $"WAV with {channels} channels is not read (only mono and stereo)"
                    );
                if (channels == 0 || rate <= 0)
                    throw new InvalidDataException($"WAV header declares {channels} channels at {rate} Hz");
                if (align != channels * bits / 8)
                    throw new InvalidDataException(
                        $"WAV block align {align} disagrees with {channels} channels of {bits} bits"
                    );
                if (byteRate != (long)rate * align)
                    throw new InvalidDataException(
                        $"WAV byte rate {byteRate} disagrees with {rate} Hz and block align {align}"
                    );
                haveFmt = true;
            }
            else if (Is(d, (int)pos, "data"))
            {
                if (!haveFmt)
                    throw new InvalidDataException("WAV data chunk comes before its fmt chunk");
                var left = d.Length - body;
                if (size > left)
                    throw new InvalidDataException($"WAV data chunk declares {size} bytes but the file holds {left}");
                if (size % (channels * bits / 8) != 0)
                    throw new InvalidDataException(
                        $"WAV data of {size} bytes is not a whole number of {channels * bits / 8}-byte frames"
                    );
                return new WavInfo(rate, channels, bits, d[(int)body..(int)(body + size)]);
            }
            pos = body + size + (size & 1); // chunks are padded to an even size
        }
        throw new InvalidDataException(haveFmt ? "WAV has no data chunk" : "WAV has no fmt chunk");
    }

    static bool Is(byte[] d, int at, string tag) =>
        d[at] == tag[0] && d[at + 1] == tag[1] && d[at + 2] == tag[2] && d[at + 3] == tag[3];
}
