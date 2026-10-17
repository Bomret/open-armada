using System;
using System.IO;

namespace OpenArmada.Formats;

/// <summary>The image size and pixel layout from a TGA header, as <see cref="Tga.Read"/> returns them.</summary>
public sealed record TgaInfo
{
    /// <summary>The image width in pixels.</summary>
    public required int Width { get; init; }

    /// <summary>The image height in pixels.</summary>
    public required int Height { get; init; }

    /// <summary>The bits per pixel: 8 for a color-mapped image, 24 or 32 for a true-color image.</summary>
    public required int BitsPerPixel { get; init; }

    /// <summary>True for a color-mapped image (TGA type 1), false for true color (type 2).</summary>
    public required bool Indexed { get; init; }
}

/// <summary>
/// Header and size check of the uncompressed TGAs the game ships (true color and 8-bit color-mapped).
/// The pixels are decoded by Godot's own loader; this reader only decides whether the file is sound.
/// </summary>
public static class Tga
{
    /// <summary>
    /// Reads the header of an uncompressed TGA and checks that the file holds its color map and pixels.
    /// </summary>
    /// <exception cref="InvalidDataException">
    /// when the header is cut off, the width or height is 0, a color-mapped image has no color map, or the file is
    /// shorter than its ID, color map and pixel data.
    /// </exception>
    /// <exception cref="UnsupportedFormatException">
    /// when the image type is not 1 or 2, a color-mapped image is not 8 bits per pixel or its map entries are not
    /// 24 bits, or a true-color image is not 24 or 32 bits per pixel.
    /// </exception>
    public static TgaInfo Read(byte[] data)
    {
        if (data.Length < 18)
            throw new InvalidDataException($"TGA header is cut off ({data.Length} of 18 bytes)");
        int idLength = data[0],
            mapType = data[1],
            type = data[2];
        if (type != 1 && type != 2)
            throw new UnsupportedFormatException($"TGA image type {type} is not read (only 1 and 2)");
        int mapLength = data[5] | data[6] << 8,
            mapBits = data[7];
        int width = data[12] | data[13] << 8,
            height = data[14] | data[15] << 8,
            bpp = data[16];
        if (width == 0 || height == 0)
            throw new InvalidDataException($"TGA size is {width}x{height}");
        if (type == 1)
        {
            if (mapType != 1)
                throw new InvalidDataException("color-mapped TGA has no color map");
            if (bpp != 8)
                throw new UnsupportedFormatException($"color-mapped TGA with {bpp} bits per pixel is not read");
            if (mapBits != 24)
                throw new UnsupportedFormatException($"TGA color map entries of {mapBits} bits are not read");
        }
        else if (bpp != 24 && bpp != 32)
            throw new UnsupportedFormatException($"true-color TGA with {bpp} bits per pixel is not read");
        var map = mapType == 1 ? (long)mapLength * ((mapBits + 7) / 8) : 0;
        var need = 18 + idLength + map + (long)width * height * (bpp / 8);
        if (need > data.Length)
            throw new InvalidDataException($"TGA is cut off ({data.Length} of {need} bytes)");

        return new TgaInfo
        {
            Width = width,
            Height = height,
            BitsPerPixel = bpp,
            Indexed = type == 1,
        };
    }
}
