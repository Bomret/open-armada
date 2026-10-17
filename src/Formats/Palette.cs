using System.Collections.Generic;
using System.IO;

namespace OpenArmada.Formats;

/// <summary>A raw palette file: 256 RGB triplets of 8 bits, 768 bytes, nothing else.</summary>
public static class Palette
{
    /// <summary>Reads the 256 colors of a palette file in file order.</summary>
    /// <exception cref="InvalidDataException">when the file is not exactly 768 bytes.</exception>
    public static IReadOnlyList<(byte R, byte G, byte B)> Read(byte[] data)
    {
        if (data.Length != 768)
            throw new InvalidDataException($"palette is {data.Length} bytes, expected 768 (256 RGB colors)");
        var colors = new (byte, byte, byte)[256];
        for (var i = 0; i < 256; i++)
        {
            colors[i] = (data[i * 3], data[i * 3 + 1], data[i * 3 + 2]);
        }

        return colors;
    }
}
