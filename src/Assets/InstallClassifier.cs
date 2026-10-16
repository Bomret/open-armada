using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace OpenArmada.Assets;

// What the asset check does with an install file: some asset kind loads it, it is out of scope (video,
// help, AI plans, compiled code, install, network, copy protection), the engine knows what it is but
// reads it with no kind, or no rule places it.
public enum FileClass { Loaded, OutOfScope, KnownNotLoaded, Unclassified }

// One install file (an Inventory entry), its class, and the group that class names ("models", "video",
// "input device maps").
public sealed record Classified(string Entry, FileClass Class, string Group);

// The classification rules: the one place that says which install file belongs to which class. Godot-free.
// The "Loaded" rules mirror the enumeration scope of the asset kinds, the others come from the REA report
// of Armada.exe on the unknown file groups. Entries and rules ignore case.
public static class InstallClassifier
{
    static readonly Regex Numbered = new(@"^\.\d+$");

    public static IReadOnlyList<Classified> Classify(IEnumerable<string> inventory)
    {
        var entries = inventory.ToList();
        var all = new HashSet<string>(entries.Select(e => e.ToLowerInvariant()));
        return entries.Select(e => { var (c, g) = Rule(e.ToLowerInvariant(), all); return new Classified(e, c, g); }).ToList();
    }

    static (FileClass, string) Loaded(string group) => (FileClass.Loaded, group);
    static (FileClass, string) Out(string group) => (FileClass.OutOfScope, group);
    static (FileClass, string) Known(string group) => (FileClass.KnownNotLoaded, group);
    static readonly (FileClass, string) Unknown = (FileClass.Unclassified, "");

    static (FileClass, string) Rule(string entry, HashSet<string> all)
    {
        var at = entry.IndexOf("::", StringComparison.Ordinal);
        if (at >= 0) return Member(entry[..at], entry[(at + 2)..]);
        var parts = entry.Split('/');
        var ext = Path.GetExtension(parts[^1]);
        var depth = parts.Length;
        return parts[0] switch
        {
            _ when depth == 1 => RootFile(parts[0], ext),
            "sod" when ext == ".sod" && (depth == 2 || (depth == 3 && parts[1] == "software")) => Loaded("models"),
            "textures" when depth == 3 && parts[1] is "rgb" or "index8" => Texture(entry, parts, ext, all),
            "sprites" when depth == 2 && ext == ".spr" => Loaded("sprite tables"),
            "bitmaps" when ext == ".bmp" => Loaded("bitmaps"),
            "bzn" when depth == 2 && ext == ".bzn" => Loaded("maps"),
            "bzn" when depth == 2 && ext == ".mdf" => Loaded("map start cells"),
            "bzn" when depth == 2 && ext == ".bmp" => Loaded("bitmaps"),
            "3d_paths" when depth == 2 && ext == ".pth" => Loaded("scripted paths"),
            "sounds" when ext == ".wav" => Loaded("sounds"),
            "cursors" when ext == ".cur" => Loaded("cursors"),
            "misc" when depth == 2 => Misc(parts[1], ext),
            "giddi" when depth == 2 && ext == ".std" => Known("input device maps"),
            "addon" => Addon(parts, ext),
            "bmp" or "wav" => Known("launcher resources"),
            "animations" => Out("video"),
            "help" => Out("help"),
            "ai" => Out("AI plans"),
            "missions" => Out("compiled mission scripts"),
            "dll" => Out("network drivers"),
            "uninstall" => Out("install"),
            "ico" or "url" => Out("launcher shortcuts"),
            "logs" => Out("logs"),
            _ => Unknown,
        };
    }

    // A member of an archive: the objective texts, and the definitions, tech trees, models and textures of
    // any archive. Any other member of an override-layer archive is an override no kind loads.
    static (FileClass, string) Member(string archive, string member) =>
        archive == "objtxt.zfs" ? Loaded("objective texts")
        : Layer(member) is var c && (c.Item1 == FileClass.Loaded || archive.StartsWith("addon/", StringComparison.Ordinal)) ? c
        : Unknown;

    // A file of the override layer or an archive, by its extension: the kinds that list such files by
    // bare name load it (DefinitionKind.Named, AssetKind.Layered); a file of any other kind still
    // replaces a file of its name for the game, but no kind loads it.
    static (FileClass, string) Layer(string name) => Path.GetExtension(name) switch
    {
        ".odf" => Loaded("definitions"),
        ".tt" => Loaded("tech trees"),
        ".sod" => Loaded("models"),
        ".tga" => Loaded("textures"),
        _ => Known("overrides no kind loads"),
    };

    static (FileClass, string) RootFile(string name, string ext) => (name, ext) switch
    {
        (_, ".map") => Loaded("key maps"),
        (_, ".set") => Loaded("settings"),
        (_, ".ldl") => Loaded("text tables"),
        (_, ".fnt") => Loaded("fonts"),
        (_, ".pal") => Loaded("palettes"),
        (_, ".zfs") => Loaded("archives"),
        (_, ".zix") => Loaded("archive index"),
        ("splash.bmp", _) => Loaded("bitmaps"),
        (_, ".exe" or ".dll") => Out("compiled code"),
        ("events.odf", _) => Known("event table"),
        (_, ".h") or ("cca.cfg" or "collision.cfg", _) => Known("configuration headers"),
        ("logfile.cfg", _) => Out("logs"),
        ("woncfg.cfg" or "kver.pub" or "anet.inf", _) => Out("network"),
        ("net.ini", _) => Known("network settings, consumer unknown"),
        ("texture.dat", _) => Out("copy protection"),
        ("credits.txt" or "credits2.txt", _) => Known("credits text"),
        _ => Unknown,
    };

    static (FileClass, string) Misc(string name, string ext) =>
        name.StartsWith("gui_", StringComparison.Ordinal) && ext == ".cfg" ? Loaded("interface configurations")
        : ext is ".aip" or ".h" ? Out("AI plans")
        : ext is ".trn" or ".lgt" or ".des" or ".act" or ".alb" or ".lum" or ".tbl" or ".stb" ? Known("Battlezone leftovers")
        : Unknown;

    // The game reads addon without recursion, so a file in a subfolder is never reached.
    static (FileClass, string) Addon(string[] parts, string ext) =>
        parts.Length > 2 ? Known("addon subfolders")
        : ext == ".zfs" || parts[1].Contains(".zfs", StringComparison.Ordinal) ? Loaded("archives")
        : Layer(parts[1]);

    // Full-color and 8-bit textures with their mip levels, and the palettes of the full-color folder. A
    // level counts when every level before it exists (the game stops at the first gap); the rest of the
    // folder is a file the game never opens.
    static (FileClass, string) Texture(string entry, string[] parts, string ext, HashSet<string> all)
    {
        if (ext == ".tga") return Loaded("textures");
        if (ext == ".pal" && parts[1] == "rgb") return Loaded("palettes");
        if (ext == ".jbf") return Known("texture extras");
        if (!Numbered.IsMatch(ext)) return parts[1] == "index8" && ext == "" ? Known("texture extras") : Unknown;
        var stem = entry[..^ext.Length];
        var level = int.Parse(ext[1..]);
        var chain = all.Contains(stem + ".tga") && Enumerable.Range(1, level - 1).All(i => all.Contains($"{stem}.{i}"));
        return level is >= 1 and <= 9 && chain ? Loaded("textures") : Known("texture extras");
    }
}
