using System;
using System.Collections.Generic;
using System.IO;
using OpenArmada.Formats;

namespace OpenArmada.Assets;

public sealed record TextureLevel(string Name, TgaInfo Info, byte[] Data);

// A texture with its mip levels (smaller copies, level 1 first).
public sealed record TextureAsset(string Name, TgaInfo Info, byte[] Data, IReadOnlyList<TextureLevel> Mips);

// Textures: the .tga files of Textures/RGB (full color) and Textures/Index8 (8-bit), each with its
// mip levels. The game finds level N as "<name>.N", N from 1 to 9, and counts levels up to the first
// missing one, so a file past a gap belongs to no texture. Files that are neither a texture nor a
// level (no extension, other extensions) are not this kind's.
public sealed class TextureKind : AssetKind
{
    public const string KindName = "textures";
    const int MaxMipLevel = 9;

    public override string Name => KindName;

    public override IEnumerable<string> Enumerate(GameFileSystem files) => files.List(".tga", "Textures/RGB", "Textures/Index8");

    public override object Load(GameFileSystem files, string name)
    {
        var data = files.ReadAllBytes(name);
        var info = Tga.Read(data);
        // Same folder rule as Find: full color wins over 8-bit.
        var folder = files.Find("Textures/RGB/" + name) != null ? "Textures/RGB/" : "Textures/Index8/";
        var stem = Path.GetFileNameWithoutExtension(name);
        var mips = new List<TextureLevel>();
        var previous = info;
        for (var i = 1; i <= MaxMipLevel; i++)
        {
            var mipName = $"{stem}.{i}";
            if (files.Find(folder + mipName) == null) break;
            var level = ReadLevel(files, folder + mipName, mipName);
            if (level.Width != Math.Max(1, previous.Width / 2) || level.Height != Math.Max(1, previous.Height / 2))
                throw new InvalidDataException($"{mipName} is {level.Width}x{level.Height}, not half of {previous.Width}x{previous.Height}");
            mips.Add(new TextureLevel(mipName, level, files.ReadAllBytes(folder + mipName)));
            previous = level;
        }
        return new TextureAsset(name, info, data, mips);
    }

    static TgaInfo ReadLevel(GameFileSystem files, string path, string label)
    {
        try { return Tga.Read(files.ReadAllBytes(path)); }
        catch (InvalidDataException e) { throw new InvalidDataException($"{label}: {e.Message}"); }
        catch (UnsupportedFormatException e) { throw new UnsupportedFormatException($"{label}: {e.Message}"); }
    }
}
