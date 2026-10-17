using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenArmada.Formats;

namespace OpenArmada.Assets;

// The single view of an install's files. Names are game-style ("SOD\Fente.sod"); lookups ignore
// case and path separator, in folder and file names alike, on every platform.
//
// A bare name (no folder) is searched only in the folders that hold its kind, by extension, first
// match wins: ".tga" in the full-color texture folder before the 8-bit one, ".sod" in the main
// model folder before its software-renderer copies, ".pal" in the install root before the texture
// folder, ".bmp" in the install root (splash.bmp), and ".map", ".set", ".ldl", ".fnt", ".exe" and ".h" in
// the install root (the key and label maps, settings files, text tables, fonts, the launcher splash.exe,
// whose bytes decode the text tables, and the RTS_CFG.h header). Any other bare name is not found. ReadByPath reads a root file
// by path with no layers; the text tables and splash.exe load that way, as the launcher opens them.
//
// A bare name is looked up the way Armada.exe does (REA report of Armada.exe: FUN_004b8eb0 builds the
// index, FUN_004b9270 scans addon\, FUN_004b9900 picks the lowest slot), first match wins:
//   1. a loose file in the install's addon folder (not recursive),
//   2. a member of an addon\*.zfs archive,
//   3. the folder or archive that startrek.zix assigns the name to.
// The game's index is authoritative and a name it lacks is not found. This engine instead falls back
// to the loose-folder lookup above for a name the index does not list (the game loads e.g. bitmaps by
// a direct relative path), and for a name the index lists but cannot resolve (it never hides a file
// the install has). A name with a folder resolves directly and skips the layers.
//
// A name is matched whole against a folder, and by its first 16 characters against an archive, for an
// addon archive and an index archive alike (the game's key, and the length of an archive entry name).
// Each archive file is opened once, however many sources name it.
//
// Faults (an unreadable archive or index, an entry past the end of its archive) and index entries whose
// file is missing (Missing) are collected once, when the install is opened; every other file loads.
public sealed class GameFileSystem
{
    static readonly string[][] TextureFolders = { new[] { "Textures", "RGB" }, new[] { "Textures", "Index8" } };
    static readonly string[][] ModelFolders = { new[] { "SOD" }, new[] { "SOD", "Software" } };
    static readonly string[][] PaletteFolders = { Array.Empty<string>(), new[] { "Textures", "RGB" } };
    static readonly string[][] RootFolder = { Array.Empty<string>() };

    // An opened archive: Rel is its path in the install (forward slashes), Path its real path.
    sealed record Archive(string Rel, string Path, ZfsArchive Zfs);

    // A resolved file: a loose file, or a member of Archive.
    sealed record Hit(string? Loose = null, Archive? Archive = null, string? Member = null)
    {
        public string Path => Archive?.Path ?? Loose!;
        public string Display => Archive == null ? Path : $"{Archive.Rel}::{Member}";
    }

    // Where an index dir is read from: a loose folder or an archive (both null when it did not resolve).
    sealed record Source(string? Folder, Archive? Archive);

    readonly string Root;
    readonly Dictionary<string, Dictionary<string, string>> _entries = new(StringComparer.Ordinal);
    readonly List<string> _faults = new();
    readonly List<string> _missing = new();
    readonly List<Archive> _addonArchives = new();
    readonly Dictionary<string, Archive?> _opened = new(StringComparer.OrdinalIgnoreCase); // by Rel; null = unreadable
    readonly List<Source> _sources = new(); // per dir index - 1
    ZixIndex? _index;

    // Throws DirectoryNotFoundException when root holds no install (no SOD folder).
    public GameFileSystem(string root)
    {
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"no Armada install at '{root}' (folder does not exist)");
        Root = root;
        if (!Entries(root).TryGetValue("SOD", out var sod) || !Directory.Exists(sod))
            throw new DirectoryNotFoundException($"no Armada install at '{root}' (missing SOD folder)");
        LoadSources();
    }

    // One line per fault, "archive/<name>: reason", "archive/<name>/<entry>: reason" or
    // "index/<name>: reason". The asset check prints each as a failure.
    public IReadOnlyList<string> Faults => _faults;

    // One line per index entry whose file exists nowhere ("index/<name>: not found in <dir>"). The
    // stock install's index names build tools that the install does not hold, so the asset check
    // lists these without failing.
    public IReadOnlyList<string> Missing => _missing;

    // Opens the addon archives and the index and checks each index entry once.
    void LoadSources()
    {
        if (ResolveDir(new[] { "addon" }) is { } addon)
        {
            foreach (
                var f in Entries(addon).Values.Where(File.Exists).OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            )
            {
                if (Path.GetFileName(f).Contains(".zfs", StringComparison.OrdinalIgnoreCase) && OpenArchive(f) is { } a)
                {
                    _addonArchives.Add(a);
                }
            }
        }

        if (Resolve(new[] { "startrek.zix" }) is not { } zixPath)
            return;
        try
        {
            _index = ZixIndex.Parse(File.ReadAllText(zixPath, Windows1252.Encoding));
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            _faults.Add($"index/startrek.zix: {e.Message}");

            return;
        }

        _faults.AddRange(_index.Faults.Select(f => $"index/{f}"));
        foreach (var dir in _index.Dirs)
        {
            string? folder = null;
            Archive? archive = null;
            if (dir != null)
            {
                var parts = Parts(dir).Where(p => p != ".").ToArray(); // "." is the install root
                if (dir.Contains(".zfs", StringComparison.OrdinalIgnoreCase))
                {
                    if (Resolve(parts) is { } path)
                    {
                        archive = OpenArchive(path);
                    }
                    else
                    {
                        _faults.Add($"index/{dir}: archive is missing");
                    }
                }
                else if ((folder = ResolveDir(parts)) == null)
                {
                    _faults.Add($"index/{dir}: folder is missing");
                }
            }

            _sources.Add(new Source(folder, archive));
        }

        foreach (var (name, dir) in _index.Entries)
        {
            if (
                (_sources[dir - 1].Folder != null || _sources[dir - 1].Archive != null)
                && (FromAddon(name) ?? FromIndex(name)) == null
            )
            {
                _missing.Add($"index/{name}: not found in {_index.Dirs[dir - 1]}");
            }
        }
    }

    // Opens the archive file once; a later call returns the same result and reports no fault again.
    Archive? OpenArchive(string path)
    {
        var rel = Path.GetRelativePath(Root, path).Replace('\\', '/');
        if (_opened.TryGetValue(rel, out var opened))
            return opened;
        try
        {
            var zfs = ZfsArchive.Open(File.ReadAllBytes(path));
            _faults.AddRange(zfs.Faults.Select(f => $"archive/{rel}/{f}"));

            return _opened[rel] = new Archive(rel, path, zfs);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            _faults.Add($"archive/{rel}: {e.Message}");

            return _opened[rel] = null;
        }
    }

    // Full path of the file, or null when it does not exist. A member of an archive is named
    // "<archive path>::<member>".
    public string? Find(string name) => Locate(name)?.Display;

    public byte[] ReadAllBytes(string name) =>
        Locate(name) is { } hit ? Read(hit) : throw new FileNotFoundException($"'{name}' is not in the install", name);

    // The file `sibling` (a bare name) from the source of `name`'s winning copy: the override layer when
    // that copy is an override, else the same folder or archive; null when that source lacks it. A
    // texture reads its mip levels this way, so it never takes the levels of a copy it replaces or that
    // replaces it.
    public byte[]? ReadBeside(string name, string sibling) => Beside(name, sibling) is { } hit ? Read(hit) : null;

    // The Inventory entry of the file `name` resolves to, and of the file ReadBeside reads; null when there is none.
    public string? Entry(string name) => Locate(name) is { } hit ? Entry(hit) : null;

    public string? EntryBeside(string name, string sibling) => Beside(name, sibling) is { } hit ? Entry(hit) : null;

    Hit? Beside(string name, string sibling)
    {
        if (Locate(name) is not { } hit)
            return null;
        var parts = Parts(name);

        return parts.Length == 1 && FromAddon(parts[0]) != null ? FromAddon(sibling)
            : hit.Archive is { } a
                ? (a.Zfs.Contains(ZixIndex.MemberKey(sibling)) ? new Hit(null, a, ZixIndex.MemberKey(sibling)) : null)
            : Entries(Path.GetDirectoryName(hit.Loose)!).TryGetValue(sibling, out var path) && File.Exists(path)
                ? new Hit(path)
            : null;
    }

    string Entry(Hit hit) =>
        hit.Archive == null ? Path.GetRelativePath(Root, hit.Loose!).Replace('\\', '/') : hit.Display;

    static byte[] Read(Hit hit) =>
        hit is { Archive: { } a, Member: { } m } ? a.Zfs.Read(m) : File.ReadAllBytes(hit.Path);

    // The loose file at the path, relative to the install root, as a program that opens it by path reads
    // it: no addon folder, archive, or index; null when there is none.
    public byte[]? ReadByPath(string name) =>
        Parts(name) is { Length: > 0 } parts && Resolve(parts) is { } path ? File.ReadAllBytes(path) : null;

    Hit? Locate(string name)
    {
        var parts = Parts(name);
        if (parts.Length == 0)
            return null;
        if (parts.Length > 1)
            return Resolve(parts) is { } p ? new Hit(p) : null;
        if ((FromAddon(parts[0]) ?? FromIndex(parts[0])) is { } layered)
            return layered;
        var folders = Path.GetExtension(parts[0]).ToLowerInvariant() switch
        {
            ".tga" => TextureFolders,
            ".sod" => ModelFolders,
            ".pal" => PaletteFolders,
            ".bmp" or ".map" or ".set" or ".ldl" or ".fnt" or ".exe" or ".h" => RootFolder,
            _ => Array.Empty<string[]>(),
        };
        foreach (var folder in folders)
        {
            if (Resolve(folder.Append(parts[0]).ToArray()) is { } found)
                return new Hit(found);
        }

        return null;
    }

    // True when a file of the override layer replaces an archive or index copy of the same name.
    public bool Overrides(string name) => FromAddon(name) != null && FromIndex(name) != null;

    Hit? FromAddon(string name)
    {
        if (
            ResolveDir(new[] { "addon" }) is { } addon
            && Entries(addon).TryGetValue(name, out var loose)
            && File.Exists(loose)
        )
            return new Hit(loose);
        foreach (var a in _addonArchives)
        {
            if (a.Zfs.Contains(ZixIndex.MemberKey(name)))
                return new Hit(null, a, ZixIndex.MemberKey(name));
        }

        return null;
    }

    Hit? FromIndex(string name)
    {
        if (_index == null || !_index.TryGetDir(name, out var dir))
            return null;
        var source = _sources[dir - 1];
        if (source.Archive is { } a)
            return a.Zfs.Contains(ZixIndex.MemberKey(name)) ? new Hit(null, a, ZixIndex.MemberKey(name)) : null;
        if (source.Folder is { } folder && Entries(folder).TryGetValue(name, out var path) && File.Exists(path))
            return new Hit(path);

        return null;
    }

    // Names of the members of the archives called `archive` (e.g. "objtxt.zfs"), wherever the
    // index or the addon folder places them. Find(name) then loads the winning copy.
    public IEnumerable<string> Members(string archive) =>
        _opened
            .Values.OfType<Archive>()
            .Where(a => Path.GetFileName(a.Path).Equals(archive, StringComparison.OrdinalIgnoreCase))
            .SelectMany(a => a.Zfs.Names)
            .Distinct(StringComparer.OrdinalIgnoreCase);

    // Bare file names with the extension (e.g. ".sod") across the given folders, each name once,
    // in the order of the folders. Find(name) then loads the winning copy.
    public IReadOnlyList<string> List(string extension, params string[] folders)
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in folders)
        {
            if (ResolveDir(Parts(folder)) is { } dir)
            {
                foreach (var f in Directory.EnumerateFiles(dir).Select(Path.GetFileName).OfType<string>())
                {
                    if (f.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                    {
                        names.TryAdd(f, f);
                    }
                }
            }
        }

        return names.Values.ToList();
    }

    // Every file of the install, as a game-style name with forward slashes, then every archive member
    // as "<archive path>::<member>" (the "::" tells a member from a loose file).
    public IEnumerable<string> Inventory() =>
        Directory
            .EnumerateFiles(Root, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(Root, f).Replace('\\', '/'))
            .Concat(_opened.Values.OfType<Archive>().SelectMany(a => a.Zfs.Names.Select(n => $"{a.Rel}::{n}")));

    static string[] Parts(string name) => name.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);

    string? Resolve(string[] parts)
    {
        var path = ResolveDir(parts[..^1]);
        if (path == null)
            return null;

        return Entries(path).TryGetValue(parts[^1], out var real) && File.Exists(real) ? real : null;
    }

    string? ResolveDir(string[] parts)
    {
        var path = Root;
        foreach (var part in parts)
        {
            if (!Entries(path).TryGetValue(part, out var next) || !Directory.Exists(next))
                return null;
            path = next;
        }

        return path;
    }

    // Case-insensitive name -> real path for the entries of one folder, listed once.
    Dictionary<string, string> Entries(string dir)
    {
        if (_entries.TryGetValue(dir, out var map))
            return map;
        map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in Directory.EnumerateFileSystemEntries(dir))
        {
            map.TryAdd(Path.GetFileName(e), e);
        }

        return _entries[dir] = map;
    }
}
