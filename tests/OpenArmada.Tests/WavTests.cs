using System;
using System.IO;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Formats;

namespace OpenArmada.Tests;

[TestClass]
public class WavTests
{
    // Builds a WAV in code (INV-0001). `declared` is the data chunk size the header claims; `actual` the bytes that follow.
    internal static byte[] Make(int rate = 22050, int channels = 1, int bits = 16, int format = 1, int declared = 8, int actual = 8,
        int? byteRate = null, int? blockAlign = null, int fmtSize = 16)
    {
        var ms = new MemoryStream();
        var w = new BinaryWriter(ms, Encoding.ASCII);
        w.Write("RIFF"u8.ToArray()); w.Write(36 + actual); w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray()); w.Write(fmtSize);
        w.Write((short)format); w.Write((short)channels); w.Write(rate);
        w.Write(byteRate ?? rate * channels * bits / 8); w.Write((short)(blockAlign ?? channels * bits / 8)); w.Write((short)bits);
        for (var i = 16; i < fmtSize; i++) w.Write((byte)0);
        w.Write("data"u8.ToArray()); w.Write(declared); w.Write(new byte[actual]);
        return ms.ToArray();
    }

    [TestMethod]
    public void ReadsFormatAndData()
    {
        var wav = Wav.Read(Make(rate: 11025, channels: 2, bits: 8, declared: 6, actual: 6));
        Assert.AreEqual((11025, 2, 8, 6), (wav.SampleRate, wav.Channels, wav.BitsPerSample, wav.Data.Length));
    }

    [TestMethod]
    public void SkipsOddFormatChunkExtension()
    {
        Assert.AreEqual(22050, Wav.Read(Make(fmtSize: 18)).SampleRate);
    }

    [TestMethod]
    public void TruncatedDataChunkIsCorrupt()
    {
        var e = Assert.ThrowsExactly<InvalidDataException>(() => Wav.Read(Make(declared: 100, actual: 8)));
        StringAssert.Contains(e.Message, "100");
    }

    [TestMethod]
    public void AdpcmIsUnsupported()
    {
        Assert.ThrowsExactly<UnsupportedFormatException>(() => Wav.Read(Make(format: 2)));
    }

    [TestMethod]
    public void UnknownFormatTagIsUnsupported()
    {
        Assert.ThrowsExactly<UnsupportedFormatException>(() => Wav.Read(Make(format: 0x55)));
    }

    [TestMethod]
    public void SampleSizeOtherThan8Or16IsUnsupported()
    {
        Assert.ThrowsExactly<UnsupportedFormatException>(() => Wav.Read(Make(bits: 24, declared: 6, actual: 6)));
    }

    [TestMethod]
    public void ByteRateDisagreeingWithFormatIsCorrupt()
    {
        Assert.ThrowsExactly<InvalidDataException>(() => Wav.Read(Make(byteRate: 5)));
    }

    [TestMethod]
    public void BlockAlignDisagreeingWithFormatIsCorrupt()
    {
        Assert.ThrowsExactly<InvalidDataException>(() => Wav.Read(Make(blockAlign: 3)));
    }

    [TestMethod]
    public void DataNotWholeFramesIsCorrupt()
    {
        Assert.ThrowsExactly<InvalidDataException>(() => Wav.Read(Make(declared: 7, actual: 7)));
    }

    [TestMethod]
    public void ZeroChannelsIsCorrupt()
    {
        Assert.ThrowsExactly<InvalidDataException>(() => Wav.Read(Make(channels: 0)));
    }

    [TestMethod]
    public void MissingDataChunkIsCorrupt()
    {
        var bytes = Make();
        Assert.ThrowsExactly<InvalidDataException>(() => Wav.Read(bytes[..36]));
    }

    [TestMethod]
    public void NotRiffIsCorrupt()
    {
        Assert.ThrowsExactly<InvalidDataException>(() => Wav.Read(new byte[40]));
    }
}
