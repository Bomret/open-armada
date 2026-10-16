using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Formats;

namespace OpenArmada.Tests;

[TestClass]
public class BmpPaletteTests
{
    // Builds an uncompressed BMP in code (INV-0001) with a 40-byte info header and 4-byte-aligned rows.
    internal static byte[] MakeBmp(int w, int h, int bpp, int compression = 0, int pixelBytes = -1)
    {
        var palette = bpp == 8 ? 1024 : 0;
        var rows = (w * bpp + 31) / 32 * 4 * Math.Abs(h);
        var data = new byte[14 + 40 + palette + (pixelBytes >= 0 ? pixelBytes : rows)];
        data[0] = (byte)'B'; data[1] = (byte)'M';
        BitConverter.GetBytes(data.Length).CopyTo(data, 2);
        BitConverter.GetBytes(14 + 40 + palette).CopyTo(data, 10);
        BitConverter.GetBytes(40).CopyTo(data, 14);
        BitConverter.GetBytes(w).CopyTo(data, 18);
        BitConverter.GetBytes(h).CopyTo(data, 22);
        data[26] = 1;
        data[28] = (byte)bpp;
        BitConverter.GetBytes(compression).CopyTo(data, 30);
        return data;
    }

    [TestMethod]
    public void ReadsBmpHeader()
    {
        var info = Bmp.Read(MakeBmp(5, 3, 24));
        Assert.AreEqual((5, 3, 24), (info.Width, info.Height, info.BitsPerPixel));
    }

    [TestMethod]
    public void BottomUpAndTopDownHaveSameSize()
    {
        Assert.AreEqual(3, Bmp.Read(MakeBmp(5, -3, 24)).Height);
    }

    [TestMethod]
    public void TruncatedPixelsAreCorrupt()
    {
        Assert.ThrowsExactly<InvalidDataException>(() => Bmp.Read(MakeBmp(5, 3, 24, pixelBytes: 10)));
    }

    [TestMethod]
    public void NotBmpIsCorrupt()
    {
        Assert.ThrowsExactly<InvalidDataException>(() => Bmp.Read(new byte[60]));
    }

    [TestMethod]
    public void CompressedBmpIsUnsupported()
    {
        Assert.ThrowsExactly<UnsupportedFormatException>(() => Bmp.Read(MakeBmp(5, 3, 8, compression: 1)));
    }

    [TestMethod]
    public void UnknownPixelDepthIsUnsupported()
    {
        Assert.ThrowsExactly<UnsupportedFormatException>(() => Bmp.Read(MakeBmp(5, 3, 12)));
    }

    [TestMethod]
    public void PaletteHolds256Colors()
    {
        var bytes = new byte[768];
        bytes[3] = 10; bytes[4] = 20; bytes[5] = 30;
        var colors = Palette.Read(bytes);
        Assert.AreEqual((256, (byte)10, (byte)20, (byte)30), (colors.Count, colors[1].R, colors[1].G, colors[1].B));
    }

    [TestMethod]
    public void PaletteOfOtherSizeIsCorrupt()
    {
        var e = Assert.ThrowsExactly<InvalidDataException>(() => Palette.Read(new byte[100]));
        StringAssert.Contains(e.Message, "100");
    }
}
