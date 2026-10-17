using System;
using System.IO;

namespace OpenArmada.Formats;

public sealed record BmpInfo
{
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required int BitsPerPixel { get; init; }
}

// Header and size check of the uncompressed Windows BMPs the game ships. Godot decodes the pixels.
public static class Bmp
{
    public static BmpInfo Read(byte[] d)
    {
        if (d.Length < 14 || d[0] != 'B' || d[1] != 'M')
            throw new InvalidDataException("not a BMP file");
        if (d.Length < 34)
            throw new InvalidDataException($"BMP header is cut off ({d.Length} bytes)");
        long offset = BitConverter.ToUInt32(d, 10);
        int headerSize = BitConverter.ToInt32(d, 14);
        if (headerSize != 40)
            throw new UnsupportedFormatException($"BMP info header of {headerSize} bytes is not read (only 40)");
        int width = BitConverter.ToInt32(d, 18),
            height = BitConverter.ToInt32(d, 22),
            bpp = BitConverter.ToUInt16(d, 28);
        int compression = BitConverter.ToInt32(d, 30);
        if (compression != 0)
            throw new UnsupportedFormatException($"BMP compression {compression} is not read");
        if (bpp is not (1 or 4 or 8 or 24 or 32))
            throw new UnsupportedFormatException($"BMP with {bpp} bits per pixel is not read");
        if (width <= 0 || height == 0)
            throw new InvalidDataException($"BMP size is {width}x{height}");
        var need = offset + ((long)width * bpp + 31) / 32 * 4 * Math.Abs((long)height);
        if (need > d.Length)
            throw new InvalidDataException($"BMP is cut off ({d.Length} of {need} bytes)");

        return new BmpInfo
        {
            Width = width,
            Height = Math.Abs(height),
            BitsPerPixel = bpp,
        };
    }
}
