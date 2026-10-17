using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WavLevel;

namespace OpenArmada.Tests;

// WAVs are built in code (INV-0001): 32-bit integer PCM, stereo, 1000 Hz.
[TestClass]
public class WavLevelTests
{
    const int Rate = 1000;

    static byte[] Wav(Func<int, double> sample, int frames, int channels = 2, short format = 1, short bits = 32)
    {
        var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        var dataSize = frames * channels * bits / 8;
        w.Write("RIFF"u8.ToArray());
        w.Write(36 + dataSize);
        w.Write("WAVEfmt "u8.ToArray());
        w.Write(16);
        w.Write(format);
        w.Write((short)channels);
        w.Write(Rate);
        w.Write(Rate * channels * bits / 8);
        w.Write((short)(channels * bits / 8));
        w.Write(bits);
        w.Write("data"u8.ToArray());
        w.Write(dataSize);
        for (var i = 0; i < frames; i++)
        for (var c = 0; c < channels; c++)
            w.Write((int)Math.Round(sample(i) * int.MaxValue));
        return ms.ToArray();
    }

    static double Sine(int i, double amplitude = 1) => amplitude * Math.Sin(2 * Math.PI * 50 * i / Rate);

    [TestMethod]
    public void FullScaleSineReadsZeroPeakAndMinusThreeRms()
    {
        var (level, problem) = WavLevelMeter.Measure(Wav(i => Sine(i), 1000));

        Assert.IsNull(problem);
        Assert.AreEqual(0, level!.PeakDb, 0.1);
        Assert.AreEqual(-3.01, level.RmsDb, 0.1);
    }

    [TestMethod]
    public void HalfAmplitudeReadsSixDecibelsLess()
    {
        var (level, _) = WavLevelMeter.Measure(Wav(i => Sine(i, 0.5), 1000));

        Assert.AreEqual(-6.02, level!.PeakDb, 0.1);
        Assert.AreEqual(-9.03, level.RmsDb, 0.1);
    }

    [TestMethod]
    public void SilenceReadsNegativeInfinity()
    {
        var (level, _) = WavLevelMeter.Measure(Wav(_ => 0, 1000));

        Assert.IsTrue(double.IsNegativeInfinity(level!.PeakDb));
        Assert.IsTrue(double.IsNegativeInfinity(level.RmsDb));
    }

    [TestMethod]
    public void WindowMeasuresOnlyItsSamples()
    {
        // Loud for the first second, silent for the second second.
        var wav = Wav(i => i < Rate ? Sine(i) : 0, 2 * Rate);

        var (loud, _) = WavLevelMeter.Measure(wav, from: 0, to: 1);
        var (quiet, _) = WavLevelMeter.Measure(wav, from: 1, to: 2);

        Assert.AreEqual(0, loud!.PeakDb, 0.1);
        Assert.AreEqual(-3.01, loud.RmsDb, 0.1);
        Assert.IsTrue(double.IsNegativeInfinity(quiet!.PeakDb));
    }

    [TestMethod]
    public void ChunkWithAHugeSizeBeforeTheDataIsReportedNotThrown()
    {
        var wav = Wav(i => Sine(i), 100);
        var junk = new byte[wav.Length + 8];
        wav.AsSpan(0, 36).CopyTo(junk);
        "JUNK"u8.CopyTo(junk.AsSpan(36));
        BitConverter.GetBytes(0x7FFFFFF0).CopyTo(junk, 40);
        wav.AsSpan(36).CopyTo(junk.AsSpan(44));

        StringAssert.Contains(Problem(junk), "no data chunk");
    }

    [TestMethod]
    public void SecondsThatAreNotFiniteAreRejected()
    {
        var wav = Wav(i => Sine(i), Rate);

        StringAssert.Contains(Problem(wav, from: double.NaN), "finite");
        StringAssert.Contains(Problem(wav, to: double.NaN), "finite");
        StringAssert.Contains(Problem(wav, to: double.PositiveInfinity), "finite");
    }

    static string Problem(byte[] wav, double? from = null, double? to = null)
    {
        var (level, problem) = WavLevelMeter.Measure(wav, from, to);
        Assert.IsNull(level);
        return problem!;
    }

    [TestMethod]
    public void BytesThatAreNoWavAreUnreadable() => StringAssert.Contains(Problem([1, 2, 3]), "not a WAV");

    [TestMethod]
    public void DataSizeThatDoesNotMatchTheBytesIsUnfinished()
    {
        var wav = Wav(i => Sine(i), 1000);
        // Movie Maker writes the sizes on close; a file cut short keeps the size 0 or too big.
        BitConverter.GetBytes(0).CopyTo(wav, 40);

        StringAssert.Contains(Problem(wav), "unfinished");
    }

    [TestMethod]
    public void AnythingButPlainPcmIsRejected()
    {
        StringAssert.Contains(Problem(Wav(i => Sine(i), 100, format: 3)), "not PCM");
        StringAssert.Contains(Problem(Wav(i => Sine(i), 100, format: unchecked((short)0xFFFE))), "not PCM");
    }

    [TestMethod]
    public void Non32BitPcmIsRejected() => StringAssert.Contains(Problem(Wav(i => Sine(i), 100, bits: 16)), "32-bit");

    [TestMethod]
    public void WindowOutsideTheAudioIsRejected()
    {
        var wav = Wav(i => Sine(i), Rate);

        StringAssert.Contains(Problem(wav, from: 2), "outside");
        StringAssert.Contains(Problem(wav, to: 2), "outside");
        StringAssert.Contains(Problem(wav, from: -1), "outside");
    }

    [TestMethod]
    public void FromAfterToIsRejected() =>
        StringAssert.Contains(Problem(Wav(i => Sine(i), Rate), from: 0.8, to: 0.2), "after");
}
