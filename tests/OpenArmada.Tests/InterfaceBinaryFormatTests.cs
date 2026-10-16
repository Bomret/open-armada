using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Formats;

namespace OpenArmada.Tests;

// Settings files, fonts and cursors, built in code (INV-0001).
[TestClass]
public class InterfaceBinaryFormatTests
{
    static byte[] U32(params uint[] v) => v.SelectMany(System.BitConverter.GetBytes).ToArray();

    // --- settings ---

    [TestMethod]
    public void SettingsListTheirLinesInOrderIncludingNone()
    {
        var s = SettingsFile.Read("m.set", Encoding.ASCII.GetBytes("fed1.bzn\r\nnone \r\nfed2.bzn"));
        CollectionAssert.AreEqual(new[] { "fed1.bzn", "none", "fed2.bzn" }, s.Entries.Select(e => e.Text).ToArray());
        Assert.AreEqual(2, s.Entries[1].Line);
        Assert.AreEqual(0, s.Notes.Count);
    }

    [TestMethod]
    public void SettingsLineThatIsNeitherAMissionNorNoneIsANote()
    {
        var s = SettingsFile.Read("m.set", Encoding.ASCII.GetBytes("fed1.bzn\nwhat is this\n"));
        Assert.AreEqual(2, s.Entries.Count);
        StringAssert.StartsWith(s.Notes.Single(), "m.set:2:");
    }

    [TestMethod]
    public void BinarySettingsAreUnsupportedNotCorrupt() =>
        Assert.ThrowsExactly<UnsupportedFormatException>(() => SettingsFile.Read("shell.set", U32(0, 0, 0, 1).Concat(Encoding.ASCII.GetBytes("Player")).Concat(new byte[10]).ToArray()));

    // --- fonts ---

    // 256 glyphs of height 2; glyph i has width (i == 65 ? 3 : 0): the letter A is 3x2 with ink at (0,0) and (2,1).
    static byte[] Font(uint count = 256, uint height = 2, int truncate = 0)
    {
        var data = new List<byte>(U32(0x2e31, count, height, 0xff));
        var table = data.Count;
        data.AddRange(new byte[count * 4]);
        for (var i = 0; i < count; i++)
        {
            var offset = U32((uint)data.Count);
            for (var k = 0; k < 4; k++) data[table + i * 4 + k] = offset[k];
            if (i == 65) { data.AddRange(U32(3)); data.AddRange(new byte[] { 0, 255, 255, 255, 255, 0 }); }
            else data.AddRange(U32(0));
        }
        return data.Take(data.Count - truncate).ToArray();
    }

    [TestMethod]
    public void FontDecodesGlyphsByCode()
    {
        var f = BitmapFont.Read("f.fnt", Font());
        Assert.AreEqual(2, f.Height);
        Assert.AreEqual(256, f.Glyphs.Count);
        var a = f.Glyphs[65];
        Assert.AreEqual(3, a.Width);
        CollectionAssert.AreEqual(new byte[] { 0, 255, 255, 255, 255, 0 }, a.Pixels);
        Assert.AreEqual(255, f.Transparent);
        Assert.AreEqual(0, f.Glyphs[66].Width);
    }

    [TestMethod]
    public void FontWithMoreGlyphsThanOneByteHoldsIsUnsupported() =>
        Assert.ThrowsExactly<UnsupportedFormatException>(() => BitmapFont.Read("j.fnt", Font(count: 300)));

    [TestMethod]
    public void FontWithACorruptGlyphCountIsCorruptNotUnsupported()
    {
        Assert.ThrowsExactly<InvalidDataException>(() => BitmapFont.Read("f.fnt", Font(count: 0)));
        Assert.ThrowsExactly<InvalidDataException>(() => BitmapFont.Read("f.fnt", U32(0x2e31, 0xFFFFFFF0, 2, 0xff).Concat(new byte[64]).ToArray()));
    }

    [TestMethod]
    public void TruncatedFontIsCorrupt() =>
        Assert.ThrowsExactly<InvalidDataException>(() => BitmapFont.Read("f.fnt", Font(truncate: 3)));

    [TestMethod]
    public void FontWithoutItsVersionIsCorrupt() =>
        Assert.ThrowsExactly<InvalidDataException>(() => BitmapFont.Read("f.fnt", new byte[40]));

    // --- cursors ---

    // A 2x2 8-bit cursor: palette 0 = red, 1 = blue; rows bottom-up "blue red" then "red red"; AND mask
    // marks the top right pixel transparent.
    static byte[] Cursor(ushort hotX = 1, ushort hotY = 0, ushort type = 2, ushort bits = 8, uint headerSize = 40, bool twoImages = false)
    {
        var dib = new List<byte>(U32(headerSize, 2, 4)); // header size, width, double height (image plus mask)
        dib.AddRange(System.BitConverter.GetBytes((ushort)1));
        dib.AddRange(System.BitConverter.GetBytes(bits));
        dib.AddRange(U32(0, 0, 0, 0, 2, 0));
        dib.AddRange(new byte[] { 0, 0, 255, 0, 255, 0, 0, 0 }); // BGRA: red, blue
        dib.AddRange(new byte[] { 1, 0, 0, 0 });                // bottom row: blue, red (rows padded to 4)
        dib.AddRange(new byte[] { 0, 0, 0, 0 });                // top row: red, red
        dib.AddRange(new byte[] { 0x00, 0, 0, 0 });             // AND mask bottom row: opaque
        dib.AddRange(new byte[] { 0x40, 0, 0, 0 });             // AND mask top row: second pixel transparent
        var head = new List<byte> { 0, 0, (byte)type, (byte)(type >> 8), (byte)(twoImages ? 2 : 1), 0, 2, 2, 2, 0 };
        head.AddRange(System.BitConverter.GetBytes(hotX));
        head.AddRange(System.BitConverter.GetBytes(hotY));
        head.AddRange(U32((uint)dib.Count, twoImages ? 38u : 22u));
        if (twoImages) head.AddRange(head.Skip(6).Take(16).ToArray()); // a second entry naming the same image
        return head.Concat(dib).ToArray();
    }

    [TestMethod]
    public void CursorWithACorruptHeaderSizeIsCorruptNotACrash()
    {
        foreach (var size in new uint[] { 0xFFFFFF00, 0x7FFFFFF0, 39, 5000 })
        {
            var e = Assert.ThrowsExactly<InvalidDataException>(() => CursorImage.Read("c.cur", Cursor(headerSize: size)), $"header size {size}");
            StringAssert.Contains(e.Message, "c.cur");
        }
    }

    [TestMethod]
    public void CursorWithSeveralImagesNotesThatOnlyTheFirstIsRead()
    {
        var c = CursorImage.Read("c.cur", Cursor(twoImages: true));
        StringAssert.Contains(c.Notes.Single(), "2 images");
        Assert.AreEqual(0, CursorImage.Read("c.cur", Cursor()).Notes.Count);
    }

    [TestMethod]
    public void CursorHotspotOutsideTheImageIsANote() =>
        StringAssert.Contains(CursorImage.Read("c.cur", Cursor(hotX: 5)).Notes.Single(), "hotspot");

    [TestMethod]
    public void CursorDecodesPixelsMaskAndHotspot()
    {
        var c = CursorImage.Read("c.cur", Cursor());
        Assert.AreEqual((2, 2, 1, 0), (c.Width, c.Height, c.HotspotX, c.HotspotY));
        CollectionAssert.AreEqual(new byte[]
        {
            255, 0, 0, 255,  255, 0, 0, 0,   // top row: red, red but transparent
            0, 0, 255, 255,  255, 0, 0, 255, // bottom row: blue, red
        }, c.Rgba);
    }

    [TestMethod]
    public void CursorOfAnotherDepthIsUnsupported() =>
        Assert.ThrowsExactly<UnsupportedFormatException>(() => CursorImage.Read("c.cur", Cursor(bits: 24)));

    [TestMethod]
    public void IconIsNotACursorAndTruncationIsCorrupt()
    {
        Assert.ThrowsExactly<InvalidDataException>(() => CursorImage.Read("c.cur", Cursor(type: 1)));
        Assert.ThrowsExactly<InvalidDataException>(() => CursorImage.Read("c.cur", Cursor().Take(60).ToArray()));
    }
}
