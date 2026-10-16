using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using OpenArmada.Formats;

namespace OpenArmada.Assets;

// A start location of a multiplayer map: its number (1..8) and the .mdf cell; the ground position is
// MapBounds.FromStartCell.
public sealed record MapStart(int Number, StartCell Cell);

// A map and its companions. Starts is empty for a map without an .mdf (campaign maps); Overview is the
// install name of the map's .bmp or null. ScriptedPaths are the install's 3D_Paths (points relative to the
// first point), the same for every map.
public sealed record MapAsset(string Name, BznMap Map, IReadOnlyList<MapStart> Starts, string? Overview,
    IReadOnlyList<MapPath> ScriptedPaths);

// Maps: the .bzn files of bzn/, each with its .mdf (start positions), .bmp (overview image), and the
// scripted camera paths. Names carry their folder ("bzn/2duel.bzn").
public sealed class MapKind : AssetKind
{
    public const string KindName = "maps";
    const string Folder = "bzn/", PathFolder = "3D_Paths/";

    public override string Name => KindName;

    public override IEnumerable<string> Enumerate(GameFileSystem files) =>
        files.Inventory().Where(f => f.StartsWith(Folder, StringComparison.OrdinalIgnoreCase) && f.EndsWith(".bzn", StringComparison.OrdinalIgnoreCase));

    // Maps loaded per install, so the check, the unresolved references and the decode step read each map
    // once. Not thread-safe: the engine loads on one thread. A map that fails is not kept.
    static readonly ConditionalWeakTable<GameFileSystem, Dictionary<string, MapAsset>> Loaded = new();

    public override object Load(GameFileSystem files, string name)
    {
        var cache = Loaded.GetOrCreateValue(files);
        if (!cache.TryGetValue(name, out var asset)) cache[name] = asset = Read(files, name);
        return asset;
    }

    static MapAsset Read(GameFileSystem files, string name)
    {
        var map = Bzn.Read(files.ReadAllBytes(name));
        var stem = name[..^Path.GetExtension(name).Length];
        var starts = new List<MapStart>();
        if (files.Find(stem + ".mdf") != null)
        {
            var cells = Mdf.Read(Windows1252.Encoding.GetString(files.ReadAllBytes(stem + ".mdf")));
            for (var i = 0; i < cells.Count; i++) starts.Add(new MapStart(i + 1, cells[i]));
        }
        var overview = files.Find(stem + ".bmp") != null ? stem + ".bmp" : null;
        var paths = files.Inventory()
            .Where(f => f.StartsWith(PathFolder, StringComparison.OrdinalIgnoreCase) && f.EndsWith(".pth", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .Select(f => new MapPath(f[PathFolder.Length..], ReadPth(files, f)))
            .ToList();
        return new MapAsset(name, map, starts, overview, paths);
    }

    // A broken .pth fails every map, so its error names the file.
    static IReadOnlyList<Vector3> ReadPth(GameFileSystem files, string file)
    {
        try { return Pth.Read(Windows1252.Encoding.GetString(files.ReadAllBytes(file))); }
        catch (InvalidDataException e) { throw new InvalidDataException($"{file}: {e.Message}", e); }
    }

    // The definitions the map's object classes name that the install lacks.
    public override IReadOnlyList<string> Unresolved(GameFileSystem files, string name) =>
        AssetReferences.Missing(((MapAsset)Load(files, name)).Map.Objects.Select(o => o.Class + ".odf"), n => files.Find(n) != null);

    // An object outside the map's X/Z bounds is a note, never a failure: the shipped maps place staged
    // ships, nebula edges, and distant background bodies there.
    public override IReadOnlyList<string> Check(GameFileSystem files, string name)
    {
        var map = ((MapAsset)Load(files, name)).Map;
        return map.Objects.Where(o => !map.Bounds.Contains(o.Position.X, o.Position.Z))
            .Select(o => string.Create(CultureInfo.InvariantCulture,
                $"object '{o.Label}' ({o.Class}) at ({o.Position.X:0.##}, {o.Position.Y:0.##}, {o.Position.Z:0.##}) is outside the map's bounds X {map.Bounds.MinX:0.##}..{map.Bounds.MaxX:0.##}, Z {map.Bounds.MinZ:0.##}..{map.Bounds.MaxZ:0.##}"))
            .ToList();
    }
}
