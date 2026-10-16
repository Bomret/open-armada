using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using OpenArmada.Formats;

namespace OpenArmada.Assets;

// A start location of a multiplayer map: its number (1..8), the .mdf cell, and the ground position the
// cell stands for (MapBounds.FromStartCell).
public sealed record MapStart(int Number, StartCell Cell, float X, float Z);

// A scripted camera path of 3D_Paths/ (points relative to the first point).
public sealed record ScriptedPath(string Name, IReadOnlyList<Vector3> Points);

// A map and its companions. Starts is empty for a map without an .mdf (campaign maps); Overview is the
// install name of the map's .bmp or null. ScriptedPaths are the install's 3D_Paths, the same for every map.
public sealed record MapAsset(string Name, BznMap Map, IReadOnlyList<MapStart> Starts, string? Overview, BmpInfo? OverviewInfo,
    IReadOnlyList<ScriptedPath> ScriptedPaths);

// Maps: the .bzn files of bzn/, each with its .mdf (start positions), .bmp (overview image), and the
// scripted camera paths. Names carry their folder ("bzn/2duel.bzn").
public sealed class MapKind : AssetKind
{
    public const string KindName = "maps";
    const string Folder = "bzn/", PathFolder = "3D_Paths/";

    public override string Name => KindName;

    public override IEnumerable<string> Enumerate(GameFileSystem files) =>
        files.Inventory().Where(f => f.StartsWith(Folder, StringComparison.OrdinalIgnoreCase) && f.EndsWith(".bzn", StringComparison.OrdinalIgnoreCase));

    public override object Load(GameFileSystem files, string name)
    {
        var map = Bzn.Read(files.ReadAllBytes(name));
        var stem = name[..^Path.GetExtension(name).Length];
        var starts = new List<MapStart>();
        if (files.Find(stem + ".mdf") != null)
        {
            var cells = Mdf.Read(Windows1252.Encoding.GetString(files.ReadAllBytes(stem + ".mdf")));
            for (var i = 0; i < cells.Count; i++)
            {
                var (x, z) = map.Bounds.FromStartCell(cells[i]);
                starts.Add(new MapStart(i + 1, cells[i], x, z));
            }
        }
        var overview = files.Find(stem + ".bmp") != null ? stem + ".bmp" : null;
        var paths = files.Inventory()
            .Where(f => f.StartsWith(PathFolder, StringComparison.OrdinalIgnoreCase) && f.EndsWith(".pth", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .Select(f => new ScriptedPath(f[PathFolder.Length..], Pth.Read(Windows1252.Encoding.GetString(files.ReadAllBytes(f)))))
            .ToList();
        return new MapAsset(name, map, starts, overview, overview == null ? null : Bmp.Read(files.ReadAllBytes(overview)), paths);
    }

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
