using System;
using System.Collections.Generic;
using System.IO;

namespace OpenArmada.Formats;

/// <summary>
/// A Windows cursor (.cur): a directory (type 2) of images, each an 8-bit palette DIB with an AND mask. The
/// first image decodes to Rgba (row by row, top down); a mask bit set makes the pixel transparent. Other depths
/// throw UnsupportedFormatException; cut-off or inconsistent data throws InvalidDataException. A file of
/// several images and a hotspot outside the image are Notes (INV-0002).
/// </summary>
public sealed class CursorImage
{
    /// <summary>The width of the first image in pixels, from its directory entry (an entry of 0 means 256).</summary>
    public int Width { get; }

    /// <summary>The height of the first image in pixels, from its directory entry (an entry of 0 means 256).</summary>
    public int Height { get; }

    /// <summary>
    /// The hotspot's column in pixels from the left edge; it can lie outside the image, which adds a note.
    /// </summary>
    public int HotspotX { get; }

    /// <summary>
    /// The hotspot's row in pixels from the top edge; it can lie outside the image, which adds a note.
    /// </summary>
    public int HotspotY { get; }

    /// <summary>
    /// The first image as 4 bytes (R, G, B, A) per pixel, row by row, top down; A is 0 where the mask bit is set, else
    /// 255.
    /// </summary>
    public byte[] Rgba { get; }

    /// <summary>The problems that do not stop the read: more than one image, and a hotspot outside the image.</summary>
    public IReadOnlyList<string> Notes { get; }

    CursorImage(int w, int h, int x, int y, byte[] rgba, IReadOnlyList<string> notes) =>
        (Width, Height, HotspotX, HotspotY, Rgba, Notes) = (w, h, x, y, rgba, notes);

    /// <summary>Reads and decodes the first image of a cursor file.</summary>
    /// <param name="name">The file name, used in exception messages and notes.</param>
    /// <param name="d">The whole cursor file.</param>
    /// <exception cref="InvalidDataException">
    /// when the file is cut off before the first image entry or inside the image data, the directory is not of type 2,
    /// it lists no images, the bitmap header size lies outside the image data, the bitmap size differs from the
    /// directory entry, or a pixel uses a palette index past the palette.
    /// </exception>
    /// <exception cref="UnsupportedFormatException">
    /// when the first image is not an uncompressed 8-bit bitmap.
    /// </exception>
    public static CursorImage Read(string name, byte[] d)
    {
        if (d.Length < 22)
            throw new InvalidDataException($"{name}: cut off before the first image entry");
        if (BitConverter.ToUInt16(d, 0) != 0 || BitConverter.ToUInt16(d, 2) != 2)
            throw new InvalidDataException($"{name}: not a cursor (directory type {BitConverter.ToUInt16(d, 2)})");
        if (BitConverter.ToUInt16(d, 4) < 1)
            throw new InvalidDataException($"{name}: no images");
        int w = d[6] == 0 ? 256 : d[6],
            h = d[7] == 0 ? 256 : d[7];
        int hotX = BitConverter.ToUInt16(d, 10),
            hotY = BitConverter.ToUInt16(d, 12);
        long size = BitConverter.ToUInt32(d, 14),
            at = BitConverter.ToUInt32(d, 18);
        if (at + size > d.Length || size < 40)
            throw new InvalidDataException($"{name}: image data is cut off");
        var dib = (int)at;
        var headerSize = BitConverter.ToUInt32(d, dib);
        if (headerSize < 40 || headerSize > size)
            throw new InvalidDataException($"{name}: bitmap header size {headerSize} is outside the image data");
        var bits = BitConverter.ToUInt16(d, dib + 14);
        if (bits != 8 || BitConverter.ToUInt32(d, dib + 16) != 0)
            throw new UnsupportedFormatException($"{name}: {bits}-bit cursor (only uncompressed 8-bit is decoded)");
        if (BitConverter.ToInt32(d, dib + 4) != w || BitConverter.ToInt32(d, dib + 8) != h * 2)
            throw new InvalidDataException($"{name}: image size differs from its directory entry");
        var colors = (int)BitConverter.ToUInt32(d, dib + 32);
        if (colors is 0 or > 256)
        {
            colors = 256;
        }

        var palette = dib + (int)headerSize;
        var pixels = palette + colors * 4;
        var maskRow = (w + 31) / 32 * 4;
        var rowBytes = (w + 3) / 4 * 4; // rows are padded to 4 bytes
        var mask = pixels + rowBytes * h;
        if (mask + maskRow * h > at + size)
            throw new InvalidDataException($"{name}: image data is cut off");
        var notes = new List<string>();
        var images = BitConverter.ToUInt16(d, 4);
        if (images > 1)
        {
            notes.Add($"{name}: {images} images; only the first is read");
        }

        if (hotX >= w || hotY >= h)
        {
            notes.Add($"{name}: hotspot ({hotX}, {hotY}) is outside the {w}x{h} image");
        }

        var rgba = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var src = h - 1 - y; // DIB rows run bottom-up
                var index = d[pixels + src * rowBytes + x];
                if (index >= colors)
                    throw new InvalidDataException($"{name}: pixel {x},{y} uses palette index {index} of {colors}");
                var p = palette + index * 4;
                var o = (y * w + x) * 4;
                rgba[o] = d[p + 2];
                rgba[o + 1] = d[p + 1];
                rgba[o + 2] = d[p];
                rgba[o + 3] = (byte)((d[mask + src * maskRow + x / 8] >> (7 - x % 8) & 1) == 1 ? 0 : 255);
            }
        }

        return new CursorImage(w, h, hotX, hotY, rgba, notes);
    }
}
