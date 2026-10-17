using System;
using System.Collections.Generic;
using System.IO;

namespace OpenArmada.Formats;

// A glyph: Width columns by the font's Height rows of palette values, row by row; a Width of 0 is a
// character the font does not draw.
public sealed record FontGlyph(int Width, byte[] Pixels);

// A bitmap font (.FNT, BZONE.FNT): "1.\0\0", glyph count, height, the transparent pixel value (0xff), a
// table of one file offset per glyph, and per glyph a u32 width followed by width x height pixel bytes.
// Glyph i is character code i. A font of another glyph count (BZONEJ.FNT: 8896 glyphs, records of another
// layout, a Japanese multi-byte encoding) throws UnsupportedFormatException; truncated or inconsistent
// data throws InvalidDataException. The format is read from the install's file; Armada.exe does not load it.
public sealed class BitmapFont
{
    const int HeaderSize = 16,
        CodeCount = 256,
        MaxHeight = 64,
        MaxWidth = 64;

    public int Height { get; }
    public int Transparent { get; }
    public IReadOnlyList<FontGlyph> Glyphs { get; }

    BitmapFont(int height, int transparent, IReadOnlyList<FontGlyph> glyphs) =>
        (Height, Transparent, Glyphs) = (height, transparent, glyphs);

    public static BitmapFont Read(string name, byte[] data)
    {
        if (data.Length < HeaderSize || BitConverter.ToUInt32(data, 0) != 0x2e31)
            throw new InvalidDataException($"{name}: not a font (no '1.' header)");
        var count = BitConverter.ToUInt32(data, 4);
        var height = BitConverter.ToUInt32(data, 8);
        if (count == 0 || height is 0 or > MaxHeight)
            throw new InvalidDataException($"{name}: {count} glyphs of {height} rows");
        if (data.Length < HeaderSize + (long)count * 4)
            throw new InvalidDataException($"{name}: glyph table is cut off");
        for (var i = 0; i < count; i++)
        {
            if ((long)BitConverter.ToUInt32(data, HeaderSize + i * 4) + 4 > data.Length)
                throw new InvalidDataException($"{name}: glyph {i} starts past the end of the file");
        }
        // Only a well-formed font of another glyph count is unsupported, so the checks above come first.
        if (count != CodeCount)
            throw new UnsupportedFormatException(
                $"{name}: {count} glyphs of {height} rows; only 256-glyph fonts are decoded (a multi-byte encoding)"
            );
        var glyphs = new List<FontGlyph>();
        for (var i = 0; i < count; i++)
        {
            var at = (long)BitConverter.ToUInt32(data, HeaderSize + i * 4);
            var width = BitConverter.ToUInt32(data, (int)at);
            if (width > MaxWidth || at + 4 + width * height > data.Length)
                throw new InvalidDataException($"{name}: glyph {i} (width {width}) runs past the end of the file");
            glyphs.Add(new FontGlyph((int)width, data[(int)(at + 4)..(int)(at + 4 + width * height)]));
        }
        return new BitmapFont((int)height, (int)BitConverter.ToUInt32(data, 12), glyphs);
    }
}
