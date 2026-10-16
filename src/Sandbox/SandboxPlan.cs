using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using OpenArmada.Assets;
using OpenArmada.Formats;

namespace OpenArmada.Sandbox;

// One map object to show. Position and the axes are in Godot's frame (Z mirrored, as the model builder
// does), so the node's transform is Basis(AxisX, AxisY, AxisZ) at Position. Slot indexes
// TeamColors.SlotNames.
public sealed record PlacedObject(string Label, string Class, string Model, int Team, int Slot,
    Vector3 Position, Vector3 AxisX, Vector3 AxisY, Vector3 AxisZ);

// One gap of the map, shown once: the cause, the distinct names it touches, and how many objects it touches.
public sealed record ReportEntry(string Cause, IReadOnlyList<string> Names, int Count)
{
    public string Text => $"{Cause}: {string.Join(", ", Names)} ({Count} {(Count == 1 ? "object" : "objects")})";
}

// What a map sandbox shows of one map: the objects to place, the map's ambient light and background
// model (null when the map names none the install holds), and the gaps found while planning (INV-0002:
// a gap is reported and the map still opens).
public sealed class SandboxPlan
{
    const string AsteroidField = "asteroidfield"; // a definition's classLabel; the rocks are generated, not placed from a model
    const string MissingDefinition = "object definition missing, objects left out";
    const string MissingModel = "model missing, objects left out";
    const string DegenerateOrientation = "degenerate orientation, identity used";
    const string BadSlot = "team-color slot not usable, the team's own slot used";

    public IReadOnlyList<PlacedObject> Objects { get; }
    public IReadOnlyList<ReportEntry> Reports { get; }
    public Vector3 Ambient { get; }
    public string? Background { get; }

    SandboxPlan(IReadOnlyList<PlacedObject> objects, IReadOnlyList<ReportEntry> reports, Vector3 ambient, string? background)
    {
        Objects = objects;
        Reports = reports;
        Ambient = ambient;
        Background = background;
    }

    // The resolved definition of each object class of the map, by class name without regard to case. A
    // class the install has no definition for is absent.
    public static IReadOnlyDictionary<string, ResolvedDefinition> Definitions(GameFileSystem files, MapAsset map)
    {
        var found = new Dictionary<string, ResolvedDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var cls in map.Map.Objects.Select(o => o.Class).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try { found[cls] = ResolvedDefinition.Resolve(files, cls + ".odf"); }
            catch (FileNotFoundException) { }
        }
        return found;
    }

    // `hasModel` tells whether the install holds a model of the name (given without ".sod").
    public static SandboxPlan Build(MapAsset asset, IReadOnlyDictionary<string, ResolvedDefinition> definitions, Func<string, bool> hasModel)
    {
        var map = asset.Map;
        var gaps = new Gaps();
        var placed = new List<PlacedObject>();
        foreach (var o in map.Objects)
        {
            if (!definitions.TryGetValue(o.Class, out var definition)) { gaps.Add(MissingDefinition, o.Class); continue; }
            if (definition.Find("classLabel")?.Tokens.FirstOrDefault()?.Equals(AsteroidField, StringComparison.OrdinalIgnoreCase) == true) continue;
            var model = ModelOf(o.Class, definition, hasModel, gaps);
            if (model == null) continue;
            var (x, y, z) = Orientation(o, gaps);
            placed.Add(new PlacedObject(o.Label, o.Class, model, o.Team, Slot(map, o.Team, gaps),
                new Vector3(o.Position.X, o.Position.Y, -o.Position.Z), x, y, z));
        }
        var background = BackgroundOf(map, hasModel, gaps);
        return new SandboxPlan(placed, gaps.Entries, map.Ambient, background);
    }

    // The definition's baseName, else the model named like the class; null when there is none (a baseName
    // naming a model the install lacks is a gap, with no fallback).
    static string? ModelOf(string cls, ResolvedDefinition definition, Func<string, bool> hasModel, Gaps gaps)
    {
        var baseName = definition.Find("baseName")?.Tokens.FirstOrDefault();
        if (baseName == null) return hasModel(cls) ? cls : null;
        var name = WithoutExtension(baseName);
        if (hasModel(name)) return name;
        gaps.Add(MissingModel, name);
        return null;
    }

    static string? BackgroundOf(BznMap map, Func<string, bool> hasModel, Gaps gaps)
    {
        if (map.BackgroundArt.Length == 0) return null;
        var name = WithoutExtension(map.BackgroundArt);
        if (hasModel(name)) return name;
        gaps.Add(MissingModel, name);
        return null;
    }

    static string WithoutExtension(string model) => model.EndsWith(".sod", StringComparison.OrdinalIgnoreCase) ? model[..^4] : model;

    // The map's axes (Right, Up, Front) in Godot's frame: the same mirror as SodBuilder's node transform
    // (S * M * S with S = diag(1, 1, -1)). A zero, non-finite, or collapsed set of axes is a gap and gives
    // the identity.
    static (Vector3, Vector3, Vector3) Orientation(MapObject o, Gaps gaps)
    {
        var det = Vector3.Dot(o.Right, Vector3.Cross(o.Up, o.Front));
        var finite = new[] { o.Right, o.Up, o.Front }.All(v => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z));
        // stand-in: a determinant under 0.001 counts as collapsed (the game's own limit is not known)
        if (!finite || Math.Abs(det) < 0.001f)
        {
            gaps.Add(DegenerateOrientation, $"{o.Label} ({o.Class})");
            return (Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ);
        }
        return (new Vector3(o.Right.X, o.Right.Y, -o.Right.Z), new Vector3(o.Up.X, o.Up.Y, -o.Up.Z), new Vector3(-o.Front.X, -o.Front.Y, o.Front.Z));
    }

    // The slot the team's map record gives. A record of 0 or an unusable one gives the stand-in: the slot
    // of the team's own number (team 0: empty), which only an unusable one reports.
    static int Slot(BznMap map, int team, Gaps gaps)
    {
        var slots = TeamColors.SlotNames.Count;
        // stand-in: the slot of the team's own number, slot 0 (empty) for a team beyond the slots
        var own = team >= 0 && team < slots ? team : 0;
        if (team < 0 || team >= map.Teams.Count)
        {
            gaps.Add(BadSlot, $"team {team} has no record");
            return own;
        }
        var record = map.Teams[team].Color;
        if (record == 0) return own;
        if (record < 0 || record >= slots)
        {
            gaps.Add(BadSlot, record.ToString());
            return own;
        }
        return record;
    }

    // Gaps grouped by cause in order of first sight, each with its distinct names and its object count.
    sealed class Gaps
    {
        readonly List<string> _causes = new();
        readonly Dictionary<string, (List<string> Names, int Count)> _byCause = new();

        public void Add(string cause, string name)
        {
            if (!_byCause.TryGetValue(cause, out var gap)) { _causes.Add(cause); gap = (new List<string>(), 0); }
            if (!gap.Names.Contains(name)) gap.Names.Add(name);
            _byCause[cause] = (gap.Names, gap.Count + 1);
        }

        public IReadOnlyList<ReportEntry> Entries => _causes.Select(c => new ReportEntry(c, _byCause[c].Names, _byCause[c].Count)).ToList();
    }
}
