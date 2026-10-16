using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OpenArmada.Assets;

// The single view of an install's files. Names are game-style ("SOD\Fente.sod"); lookups ignore
// case and path separator, in folder and file names alike, on every platform.
//
// A bare name (no folder) is searched only in the folders that hold its kind, by extension, first
// match wins: ".tga" in the full-color texture folder before the 8-bit one, ".sod" in the main
// model folder before its software-renderer copies. Any other bare name is not found.
//
// Archives and the override layer (issue #2) plug in at Resolve: the sources are tried in order
// there, and List and Inventory gather from the same sources.
public sealed class GameFileSystem
{
    static readonly string[][] TextureFolders = { new[] { "Textures", "RGB" }, new[] { "Textures", "Index8" } };
    static readonly string[][] ModelFolders = { new[] { "SOD" }, new[] { "SOD", "Software" } };

    readonly string Root;
    readonly Dictionary<string, Dictionary<string, string>> _entries = new(StringComparer.Ordinal);

    // Throws DirectoryNotFoundException when root holds no install (no SOD folder).
    public GameFileSystem(string root)
    {
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"no Armada install at '{root}' (folder does not exist)");
        Root = root;
        if (!Entries(root).TryGetValue("SOD", out var sod) || !Directory.Exists(sod))
            throw new DirectoryNotFoundException($"no Armada install at '{root}' (missing SOD folder)");
    }

    // Full path of the file, or null when it does not exist.
    public string? Find(string name)
    {
        var parts = Parts(name);
        if (parts.Length == 0) return null;
        if (parts.Length > 1) return Resolve(parts);
        var folders = Path.GetExtension(parts[0]).ToLowerInvariant() switch
        {
            ".tga" => TextureFolders,
            ".sod" => ModelFolders,
            _ => Array.Empty<string[]>(),
        };
        foreach (var folder in folders)
            if (Resolve(folder.Append(parts[0]).ToArray()) is { } found) return found;
        return null;
    }

    public byte[] ReadAllBytes(string name) =>
        File.ReadAllBytes(Find(name) ?? throw new FileNotFoundException($"'{name}' is not in the install", name));

    // Bare file names with the extension (e.g. ".sod") across the given folders, each name once,
    // in the order of the folders. Find(name) then loads the winning copy.
    public IReadOnlyList<string> List(string extension, params string[] folders)
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in folders)
            if (ResolveDir(Parts(folder)) is { } dir)
                foreach (var f in Directory.EnumerateFiles(dir).Select(Path.GetFileName).OfType<string>())
                    if (f.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                        names.TryAdd(f, f);
        return names.Values.ToList();
    }

    // Every file of the install, as a game-style name with forward slashes.
    public IEnumerable<string> Inventory() =>
        Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(Root, f).Replace('\\', '/'));

    static string[] Parts(string name) => name.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);

    string? Resolve(string[] parts)
    {
        var path = ResolveDir(parts[..^1]);
        if (path == null) return null;
        return Entries(path).TryGetValue(parts[^1], out var real) && File.Exists(real) ? real : null;
    }

    string? ResolveDir(string[] parts)
    {
        var path = Root;
        foreach (var part in parts)
        {
            if (!Entries(path).TryGetValue(part, out var next) || !Directory.Exists(next)) return null;
            path = next;
        }
        return path;
    }

    // Case-insensitive name -> real path for the entries of one folder, listed once.
    Dictionary<string, string> Entries(string dir)
    {
        if (_entries.TryGetValue(dir, out var map)) return map;
        map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in Directory.EnumerateFileSystemEntries(dir))
            map.TryAdd(Path.GetFileName(e), e);
        return _entries[dir] = map;
    }
}
