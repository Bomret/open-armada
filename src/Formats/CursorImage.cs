using System;
using System.IO;

namespace OpenArmada.Formats;

// A Windows cursor (.cur): a directory (type 2) of images, each an 8-bit palette DIB with an AND mask. The
// first image decodes to Rgba (row by row, top down); a mask bit set makes the pixel transparent. Other depths
// throw UnsupportedFormatException; cut-off or inconsistent data throws InvalidDataException.
public sealed class CursorImage
{
    public int Width { get; }
    public int Height { get; }
    public int HotspotX { get; }
    public int HotspotY { get; }
    public byte[] Rgba { get; }

    CursorImage(int w, int h, int x, int y, byte[] rgba) => (Width, Height, HotspotX, HotspotY, Rgba) = (w, h, x, y, rgba);

    public static CursorImage Read(string name, byte[] d)
    {
        if (d.Length < 22) throw new InvalidDataException($"{name}: cut off before the first image entry");
        if (BitConverter.ToUInt16(d, 0) != 0 || BitConverter.ToUInt16(d, 2) != 2) throw new InvalidDataException($"{name}: not a cursor (directory type {BitConverter.ToUInt16(d, 2)})");
        if (BitConverter.ToUInt16(d, 4) < 1) throw new InvalidDataException($"{name}: no images");
        int w = d[6] == 0 ? 256 : d[6], h = d[7] == 0 ? 256 : d[7];
        int hotX = BitConverter.ToUInt16(d, 10), hotY = BitConverter.ToUInt16(d, 12);
        long size = BitConverter.ToUInt32(d, 14), at = BitConverter.ToUInt32(d, 18);
        if (at + size > d.Length || size < 40) throw new InvalidDataException($"{name}: image data is cut off");
        var dib = (int)at;
        var bits = BitConverter.ToUInt16(d, dib + 14);
        if (bits != 8 || BitConverter.ToUInt32(d, dib + 16) != 0) throw new UnsupportedFormatException($"{name}: {bits}-bit cursor (only uncompressed 8-bit is decoded)");
        if (BitConverter.ToInt32(d, dib + 4) != w || BitConverter.ToInt32(d, dib + 8) != h * 2) throw new InvalidDataException($"{name}: image size differs from its directory entry");
        var colors = (int)BitConverter.ToUInt32(d, dib + 32);
        if (colors is 0 or > 256) colors = 256;
        var palette = dib + (int)BitConverter.ToUInt32(d, dib);
        var pixels = palette + colors * 4;
        var maskRow = (w + 31) / 32 * 4;
        var rowBytes = (w + 3) / 4 * 4; // rows are padded to 4 bytes
        var mask = pixels + rowBytes * h;
        if (mask + maskRow * h > at + size) throw new InvalidDataException($"{name}: image data is cut off");
        var rgba = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var src = h - 1 - y; // DIB rows run bottom-up
                var index = d[pixels + src * rowBytes + x];
                if (index >= colors) throw new InvalidDataException($"{name}: pixel {x},{y} uses palette index {index} of {colors}");
                var p = palette + index * 4;
                var o = (y * w + x) * 4;
                rgba[o] = d[p + 2]; rgba[o + 1] = d[p + 1]; rgba[o + 2] = d[p];
                rgba[o + 3] = (byte)((d[mask + src * maskRow + x / 8] >> (7 - x % 8) & 1) == 1 ? 0 : 255);
            }
        return new CursorImage(w, h, hotX, hotY, rgba);
    }
}
