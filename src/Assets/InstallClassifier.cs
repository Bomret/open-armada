using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace OpenArmada.Assets;

// What the asset check does with an install file: some asset kind loads it, it is out of scope (video,
// help, AI plans, compiled code, install, network, copy protection), the engine knows what it is but
// reads it with no kind, or no rule places it.
public enum FileClass
{
    Loaded,
    OutOfScope,
    KnownNotLoaded,
    Unclassified,
}

// One install file (an Inventory entry), its class, and the group that class names ("models", "video",
// "input device maps"); for an unclassified file the reason a rule failed on it, or empty.
public sealed record Classified(string Entry, FileClass Class, string Group);

// The classification rules: the one place that says which install file belongs to which class. Godot-free.
// The "Loaded" rules mirror the enumeration scope of the asset kinds, and with the install opened the kinds
// themselves confirm each file they read (Classify); the others come from the REA report of Armada.exe on
// the unknown file groups. Entries and rules ignore case.
public static class InstallClassifier
{
    static readonly Regex Numbered = new(@"^\.\d+$");

    // With `files`, the kinds decide what loads (AssetKind.Reads): a file some kind reads is loaded, the
    // override copy where it wins its name. A file the rules below call loaded that no kind reads is known
    // but not loaded: a file whose bare name a kind reads from another copy is a shadowed copy (a mip level
    // of a texture an override replaces too), and any other mip level is a texture extra (past a gap, or
    // after a level of the wrong size). Without, the names alone decide.
    public static IReadOnlyList<Classified> Classify(IEnumerable<string> inventory, GameFileSystem? files = null)
    {
        var entries = inventory.ToList();
        var all = new HashSet<string>(entries.Select(e => e.ToLowerInvariant()));
        var reads = files == null ? null : Reads(files);
        return entries
            .Select(e =>
            {
                var (c, g) = Safe(e.ToLowerInvariant(), all, reads);
                return new Classified(e, c, g);
            })
            .ToList();
    }

    // The entries the kinds read, each with the name of a kind that reads it, and the bare names of those
    // entries. A kind that cannot list its assets adds nothing; the asset check reports it.
    sealed record KindReads(Dictionary<string, string> Entries, HashSet<string> Names);

    static KindReads Reads(GameFileSystem files)
    {
        var reads = new KindReads(new(), new());
        foreach (var kind in AssetKind.Discover())
        {
            try
            {
                foreach (var name in kind.Enumerate(files))
                foreach (var entry in kind.Reads(files, name))
                {
                    reads.Entries.TryAdd(entry.ToLowerInvariant(), kind.Name);
                    reads.Names.Add(Bare(entry.ToLowerInvariant()));
                }
            }
            catch (Exception) { }
        }
        return reads;
    }

    // A rule that throws leaves its file unclassified, with the reason as the group, and the rest go on.
    static (FileClass, string) Safe(string entry, HashSet<string> all, KindReads? reads)
    {
        try
        {
            return Asked(entry, Rule(entry, all), reads);
        }
        catch (Exception e)
        {
            return (FileClass.Unclassified, e.Message);
        }
    }

    static (FileClass, string) Asked(string entry, (FileClass, string) rule, KindReads? reads)
    {
        if (reads == null)
            return rule;
        if (reads.Entries.TryGetValue(entry, out var kind))
            return rule.Item1 == FileClass.Loaded ? rule : Loaded(kind);
        if (rule.Item1 != FileClass.Loaded)
            return rule;
        var name = Bare(entry);
        return reads.Names.Contains(name) ? Known("shadowed copies")
            : Numbered.IsMatch(Path.GetExtension(name)) ? Known("texture extras")
            : rule;
    }

    // The file name of an entry, past its folders or its archive.
    static string Bare(string entry) => entry[(Math.Max(entry.LastIndexOf('/'), entry.LastIndexOf(':')) + 1)..];

    static (FileClass, string) Loaded(string group) => (FileClass.Loaded, group);

    static (FileClass, string) Out(string group) => (FileClass.OutOfScope, group);

    static (FileClass, string) Known(string group) => (FileClass.KnownNotLoaded, group);

    static readonly (FileClass, string) Unknown = (FileClass.Unclassified, "");

    static (FileClass, string) Rule(string entry, HashSet<string> all)
    {
        var at = entry.IndexOf("::", StringComparison.Ordinal);
        if (at >= 0)
            return Member(entry[..at], entry[(at + 2)..]);
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
    // any archive. Any other member of an override-layer archive is an override no kind loads, unless a
    // kind reads it (Classify).
    static (FileClass, string) Member(string archive, string member) =>
        archive == "objtxt.zfs" ? Loaded("objective texts")
        : Layer(member) is var c
        && (c.Item1 == FileClass.Loaded || archive.StartsWith("addon/", StringComparison.Ordinal))
            ? c
        : Unknown;

    // A file of the override layer or an archive, by its extension: the kinds that list such files by
    // bare name load it (DefinitionKind.Named, AssetKind.Layered); a file of any other kind still
    // replaces a file of its name for the game. With the install opened, Classify marks such a file
    // loaded when a kind reads it (a font, key map, settings file, palette, interface configuration,
    // splash.bmp, or mip level that replaces the stock one), and the rest stay overrides no kind loads.
    static (FileClass, string) Layer(string name) =>
        Path.GetExtension(name) switch
        {
            ".odf" => Loaded("definitions"),
            ".tt" => Loaded("tech trees"),
            ".sod" => Loaded("models"),
            ".tga" => Loaded("textures"),
            _ => Known("overrides no kind loads"),
        };

    static (FileClass, string) RootFile(string name, string ext) =>
        (name, ext) switch
        {
            (_, ".map") => Loaded("key maps"),
            (_, ".set") => Loaded("settings"),
            ("rts_cfg.h", _) => Loaded("game constants"),
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
        : ext is ".trn" or ".lgt" or ".des" or ".act" or ".alb" or ".lum" or ".tbl" or ".stb"
            ? Known("Battlezone leftovers")
        : Unknown;

    // The game reads addon without recursion, so a file in a subfolder is never reached.
    static (FileClass, string) Addon(string[] parts, string ext) =>
        parts.Length > 2 ? Known("addon subfolders")
        : ext == ".zfs" || parts[1].Contains(".zfs", StringComparison.Ordinal) ? Loaded("archives")
        : Layer(parts[1]);

    // Full-color and 8-bit textures with their mip levels, and the palettes of the full-color folder. A
    // level counts when every level before it exists (the game stops at the first gap); with the install
    // opened, Classify also drops a level the texture does not read (its chain ends at a level of the
    // wrong size). The rest of the folder is a file the game never opens.
    static (FileClass, string) Texture(string entry, string[] parts, string ext, HashSet<string> all)
    {
        if (ext == ".tga")
            return Loaded("textures");
        if (ext == ".pal" && parts[1] == "rgb")
            return Loaded("palettes");
        if (ext == ".jbf")
            return Known("texture extras");
        if (!Numbered.IsMatch(ext))
            return parts[1] == "index8" && ext == "" ? Known("texture extras") : Unknown;
        var stem = entry[..^ext.Length];
        if (!int.TryParse(ext[1..], out var level) || level is < 1 or > 9)
            return Known("texture extras");
        var chain = all.Contains(stem + ".tga") && Enumerable.Range(1, level - 1).All(i => all.Contains($"{stem}.{i}"));
        return chain ? Loaded("textures") : Known("texture extras");
    }
}
