using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Formats;

namespace OpenArmada.Tests;

[TestClass]
public class TgaTests
{
    // Builds an uncompressed TGA in code (INV-0001): type 2 = true color, type 1 = color-mapped (256 entries of 24 bits).
    internal static byte[] Make(int w, int h, int bpp, byte type = 2, int pixelBytes = -1)
    {
        var mapped = type == 1;
        var header = new byte[18];
        header[1] = (byte)(mapped ? 1 : 0);
        header[2] = type;
        if (mapped) { header[5] = 0; header[6] = 1; header[7] = 24; } // 256 entries, 24 bits each
        header[12] = (byte)w; header[13] = (byte)(w >> 8);
        header[14] = (byte)h; header[15] = (byte)(h >> 8);
        header[16] = (byte)bpp;
        var map = mapped ? 768 : 0;
        var pixels = pixelBytes >= 0 ? pixelBytes : w * h * bpp / 8;
        var all = new byte[18 + map + pixels];
        header.CopyTo(all, 0);
        return all;
    }

    [TestMethod]
    public void ReadsTrueColorHeader()
    {
        var info = Tga.Read(Make(4, 2, 24));
        Assert.AreEqual((4, 2, 24, false), (info.Width, info.Height, info.BitsPerPixel, info.Indexed));
    }

    [TestMethod]
    public void ReadsColorMappedHeader()
    {
        var info = Tga.Read(Make(4, 4, 8, type: 1));
        Assert.AreEqual((4, 4, 8, true), (info.Width, info.Height, info.BitsPerPixel, info.Indexed));
    }

    [TestMethod]
    public void TruncatedPixelDataIsCorrupt()
    {
        Assert.ThrowsExactly<InvalidDataException>(() => Tga.Read(Make(4, 4, 24, pixelBytes: 10)));
    }

    [TestMethod]
    public void ShortHeaderIsCorrupt()
    {
        Assert.ThrowsExactly<InvalidDataException>(() => Tga.Read(new byte[5]));
    }

    [TestMethod]
    public void UnknownImageTypeIsUnsupported()
    {
        var bytes = Make(4, 4, 24);
        bytes[2] = 10; // run-length true color
        Assert.ThrowsExactly<UnsupportedFormatException>(() => Tga.Read(bytes));
    }

    [TestMethod]
    public void ZeroSizeIsCorrupt()
    {
        Assert.ThrowsExactly<InvalidDataException>(() => Tga.Read(Make(0, 4, 24)));
    }

    [TestMethod]
    public void ColorMappedWithoutColorMapIsCorrupt()
    {
        var bytes = Make(4, 4, 8, type: 1);
        bytes[1] = 0;
        Assert.ThrowsExactly<InvalidDataException>(() => Tga.Read(bytes));
    }

    [TestMethod]
    public void ColorMapEntriesOtherThan24BitsAreUnsupported()
    {
        var bytes = Make(4, 4, 8, type: 1);
        bytes[7] = 16;
        Assert.ThrowsExactly<UnsupportedFormatException>(() => Tga.Read(bytes));
    }

    [TestMethod]
    public void CutOffColorMapIsCorrupt()
    {
        Assert.ThrowsExactly<InvalidDataException>(() => Tga.Read(Make(4, 4, 8, type: 1)[..100]));
    }

    [TestMethod]
    public void UnknownPixelDepthIsUnsupported()
    {
        Assert.ThrowsExactly<UnsupportedFormatException>(() => Tga.Read(Make(2, 2, 12)));
    }
}
