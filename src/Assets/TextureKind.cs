using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenArmada.Formats;

namespace OpenArmada.Assets;

public sealed record TextureLevel(string Name, TgaInfo Info, byte[] Data);

// A texture with its mip levels (smaller copies, level 1 first). Notes lists what the asset check
// reports without failing: the level that ended the chain by its size.
public sealed record TextureAsset(TextureLevel Base, IReadOnlyList<TextureLevel> Mips, IReadOnlyList<string> Notes);

// Textures: the .tga files of Textures/RGB (full color), Textures/Index8 (8-bit) and the override layer,
// each with its mip levels, read from the source the texture itself comes from (its folder, its archive,
// or the override layer). The game finds level N as "<name>.N", N from 1 to 9, and counts levels up to the first
// missing one, so a file past a gap belongs to no texture. It also requires each level to be half the
// size of the one before; the chain here ends at the first level that is not, and that level and the
// later ones are no part of the texture. Files that are neither a texture nor a level (no extension,
// other extensions) are not this kind's.
public sealed class TextureKind : AssetKind
{
    public const string KindName = "textures";
    const int MaxMipLevel = 9;

    public override string Name => KindName;

    // A bare name resolves like Find: the override layer first, then full color over 8-bit. A texture
    // hidden behind another of the same name is listed with its folder ("Textures/Index8/x.tga"), so it
    // is still loaded and checked; a texture only the override layer or an archive holds is listed too.
    public override IEnumerable<string> Enumerate(GameFileSystem files) => Layered(files, ".tga", "Textures/RGB", "Textures/Index8");

    // The texture's own file only, with no mip level read. The builder uses it: a model needs the base image.
    public TextureLevel LoadBase(GameFileSystem files, string name)
    {
        var data = files.ReadAllBytes(name);
        return new TextureLevel(name, Tga.Read(data), data);
    }

    // The last texture loaded, with its install and name. The asset check's decode step loads the texture
    // Check has just loaded; it takes this one instead of reading the files again.
    // simplicity: one entry, not a cache of every texture, so the check holds one texture's bytes at a time.
    (GameFileSystem Files, string Name, TextureAsset Asset)? _last;

    public override object Load(GameFileSystem files, string name)
    {
        if (_last is { } last && last.Files == files && last.Name == name) return last.Asset;
        var asset = Read(files, name);
        _last = (files, name, asset);
        return asset;
    }

    TextureAsset Read(GameFileSystem files, string name)
    {
        var baseLevel = LoadBase(files, name);
        var stem = Path.GetFileNameWithoutExtension(name);
        var mips = new List<TextureLevel>();
        var notes = new List<string>();
        var previous = baseLevel.Info;
        for (var i = 1; i <= MaxMipLevel; i++)
        {
            var mipName = $"{stem}.{i}";
            // A level comes from where the base came from (GameFileSystem.ReadBeside).
            if (files.ReadBeside(name, mipName) is not { } bytes) break;
            TgaInfo info;
            try { info = Tga.Read(bytes); }
            catch (InvalidDataException e) { throw new InvalidDataException($"{mipName}: {e.Message}"); }
            catch (UnsupportedFormatException e) { throw new UnsupportedFormatException($"{mipName}: {e.Message}"); }
            int wantW = Math.Max(1, previous.Width / 2), wantH = Math.Max(1, previous.Height / 2);
            if (info.Width != wantW || info.Height != wantH)
            {
                notes.Add($"level {mipName} is {info.Width}x{info.Height}, expected {wantW}x{wantH}");
                break;
            }
            mips.Add(new TextureLevel(mipName, info, bytes));
            previous = info;
        }
        return new TextureAsset(baseLevel, mips, notes);
    }

    public override IReadOnlyList<string> Check(GameFileSystem files, string name) => ((TextureAsset)Load(files, name)).Notes;

    // The base and each level the texture holds, read beside the base; a texture with no first level is
    // not loaded to find that out, and one that fails to load reads no level.
    public override IEnumerable<string> Reads(GameFileSystem files, string name)
    {
        var reads = base.Reads(files, name).ToList();
        if (reads.Count == 0 || files.EntryBeside(name, Path.GetFileNameWithoutExtension(name) + ".1") == null) return reads;
        try { reads.AddRange(((TextureAsset)Load(files, name)).Mips.Select(m => files.EntryBeside(name, m.Name)).OfType<string>()); }
        catch (Exception) { }
        return reads;
    }
}
