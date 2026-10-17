using System.Collections.Generic;
using System.IO;

namespace OpenArmada.Formats;

// A raw palette file: 256 RGB triplets of 8 bits, 768 bytes, nothing else.
public static class Palette
{
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
