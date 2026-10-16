using System;
using System.IO;

namespace OpenArmada.Formats;

public sealed record TgaInfo(int Width, int Height, int BitsPerPixel, bool Indexed);

// Header and size check of the uncompressed TGAs the game ships (true color and 8-bit color-mapped).
// The pixels are decoded by Godot's own loader; this reader only decides whether the file is sound.
public static class Tga
{
    public static TgaInfo Read(byte[] data)
    {
        if (data.Length < 18) throw new InvalidDataException($"TGA header is cut off ({data.Length} of 18 bytes)");
        int idLength = data[0], mapType = data[1], type = data[2];
        if (type != 1 && type != 2) throw new UnsupportedFormatException($"TGA image type {type} is not read (only 1 and 2)");
        int mapLength = data[5] | data[6] << 8, mapBits = data[7];
        int width = data[12] | data[13] << 8, height = data[14] | data[15] << 8, bpp = data[16];
        if (width == 0 || height == 0) throw new InvalidDataException($"TGA size is {width}x{height}");
        if (type == 1)
        {
            if (mapType != 1) throw new InvalidDataException("color-mapped TGA has no color map");
            if (bpp != 8) throw new UnsupportedFormatException($"color-mapped TGA with {bpp} bits per pixel is not read");
            if (mapBits != 24) throw new UnsupportedFormatException($"TGA color map entries of {mapBits} bits are not read");
        }
        else if (bpp != 24 && bpp != 32) throw new UnsupportedFormatException($"true-color TGA with {bpp} bits per pixel is not read");
        var map = mapType == 1 ? (long)mapLength * ((mapBits + 7) / 8) : 0;
        var need = 18 + idLength + map + (long)width * height * (bpp / 8);
        if (need > data.Length) throw new InvalidDataException($"TGA is cut off ({data.Length} of {need} bytes)");
        return new TgaInfo(width, height, bpp, type == 1);
    }
}
