using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using OpenArmada.Assets;
using OpenArmada.Diagnostics;
using OpenArmada.Formats;

namespace OpenArmada.Sandbox;

/// <summary>
/// One map object to show. Position and the axes are in Godot's frame (Z mirrored, as the model builder
/// does), so the node's transform is Basis(AxisX, AxisY, AxisZ) at Position. Slot indexes
/// TeamColors.SlotNames. Each object is equal only to itself: a map can stack two objects of the same
/// values, and each is its own unit.
/// </summary>
public sealed record PlacedObject
{
    /// <summary>The map object's label.</summary>
    public required string Label { get; init; }

    /// <summary>The map object's class: the name of its definition without ".odf".</summary>
    public required string Class { get; init; }

    /// <summary>The model to show, without ".sod": the definition's baseName, else the model named like the class.</summary>
    public required string Model { get; init; }

    /// <summary>
    /// The factor the model is loaded at: the definition's <c>scaleSOD</c>, 1 when the definition sets none or a value
    /// that is no number. <see cref="OpenArmada.Formats.Sod.Scaled"/> applies it.
    /// </summary>
    public float Scale { get; init; } = 1f;

    /// <summary>The map object's team.</summary>
    public required int Team { get; init; }

    /// <summary>
    /// The team-color slot, an index into <see cref="TeamColors.SlotNames"/>: the team's map record, or the slot of
    /// the team's own number when the record is 0 or not usable.
    /// </summary>
    public required int Slot { get; init; }

    /// <summary>The map object's position, in Godot's frame (Z mirrored).</summary>
    public required Vector3 Position { get; init; }

    /// <summary>The basis's X axis, from the map object's Right axis in Godot's frame; <see cref="Vector3.UnitX"/> for a degenerate orientation.</summary>
    public required Vector3 AxisX { get; init; }

    /// <summary>The basis's Y axis, from the map object's Up axis in Godot's frame; <see cref="Vector3.UnitY"/> for a degenerate orientation.</summary>
    public required Vector3 AxisY { get; init; }

    /// <summary>The basis's Z axis, from the map object's Front axis in Godot's frame; <see cref="Vector3.UnitZ"/> for a degenerate orientation.</summary>
    public required Vector3 AxisZ { get; init; }

    /// <summary>
    /// The map object's starting state; null for full strength (every object of a map). Its velocity is in Godot's
    /// frame, like <see cref="Position"/>.
    /// </summary>
    public StartingState? Start { get; init; }

    /// <summary>Whether <c>other</c> is this same object; two objects of equal values are not equal.</summary>
    public bool Equals(PlacedObject? other) => ReferenceEquals(this, other);

    /// <inheritdoc/>
    public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
}

/// <summary>One gap of the map, shown once: the cause, the distinct names it touches, and how many objects it touches.</summary>
public sealed record ReportEntry
{
    /// <summary>What is wrong and what the sandbox does about it.</summary>
    public required string Cause { get; init; }

    /// <summary>The distinct names the cause touches, in the order first found.</summary>
    public required IReadOnlyList<string> Names { get; init; }

    /// <summary>How many objects the cause touches.</summary>
    public required int Count { get; init; }

    /// <summary>The line to show: the cause, the names, and the object count.</summary>
    public string Text => $"{Cause}: {string.Join(", ", Names)} ({Count} {(Count == 1 ? "object" : "objects")})";
}

/// <summary>
/// What a map sandbox shows of one map: the objects to place, the map's ambient light and background
/// model (null when the map names none the install holds), and the gaps found while planning (INV-0002:
/// a gap is reported and the map still opens).
/// </summary>
public sealed class SandboxPlan
{
    const string AsteroidFieldLabel = "asteroidfield"; // a definition's classLabel; the rocks are generated, not placed from a model
    const string PlanetLabel = "planet_obj"; // a definition's classLabel: PlanetClass (0x44ca90) names itself so
    const string MissingDefinition = "object definition missing, objects left out";
    const string UnreadableDefinition = "object definition cannot be read, objects left out";
    const string MissingModel = "model missing, objects left out";
    const string DegenerateOrientation = "degenerate orientation, identity used";
    const string BadSlot = "team-color slot not usable, the team's own slot used";
    const string BadScale = "scaleSOD is no number, model drawn at its stored size";

    /// <summary>The objects to place; asteroid fields and objects with a gap that leaves them out are absent.</summary>
    public IReadOnlyList<PlacedObject> Objects { get; }

    /// <summary>The gaps found while planning, the asteroid fields' gaps included.</summary>
    public IReadOnlyList<ReportEntry> Reports { get; }

    /// <summary>The map's ambient light (its AmbientRed, AmbientGreen, and AmbientBlue values).</summary>
    public Vector3 Ambient { get; }

    /// <summary>The background model without ".sod"; null when the map names none the install holds.</summary>
    public string? Background { get; }

    /// <summary>The map's bounds in the map's own frame (Z not mirrored).</summary>
    public MapBounds Bounds { get; }

    /// <summary>The asteroid fields to fill with rocks (a field with a missing or illegal value is reported and absent).</summary>
    public IReadOnlyList<AsteroidField> Fields { get; }

    /// <summary>The resolved definitions the plan was built from, by class name without regard to case.</summary>
    public IReadOnlyDictionary<string, ResolvedDefinition> Resolved { get; }

    /// <summary>The black holes of the map (placed objects whose definition's classLabel is <c>blackhole_obj</c>), read from their definitions.</summary>
    public IReadOnlyList<BlackHole> BlackHoles { get; }

    /// <summary>
    /// The nebulae of the map (placed objects whose definition's classLabel is <c>nebula_obj</c>), planned for the path
    /// costs and the slowdown, never drawn by the plan. A nebula whose <c>effectRadius</c> cannot be read is a gap and
    /// absent.
    /// </summary>
    public IReadOnlyList<Nebula> Nebulae { get; }

    /// <summary>The speed values of each nebula kind some nebula of the map has (the classes <c>mnebula1</c> to <c>mnebula5</c>).</summary>
    public IReadOnlyList<NebulaKind> NebulaKinds { get; }

    /// <summary>The nebula grid over the map's bounds, which gives a ship's position the nebula kinds that apply to it.</summary>
    public NebulaGrid NebulaGrid { get; }

    /// <summary>
    /// Every asteroid field object of the map with the position the path costs read, a belt's path when it has one,
    /// whether or not its rocks can be drawn.
    /// </summary>
    public IReadOnlyList<AsteroidSite> AsteroidSites { get; }

    readonly IReadOnlyList<Obstacle> _fieldObstacles;

    /// <summary>
    /// The spheres that push ships out of them (0x4644b0): every asteroid field by its radius, drawn or
    /// not, and every placed planet (a definition whose classLabel is <c>planet_obj</c>) by <c>modelRadius</c> of its
    /// object, its model radius. A planet whose radius is no number above 0 pushes nothing; a planet whose model is
    /// missing is not placed, so it pushes nothing either.
    /// </summary>
    public IReadOnlyList<Obstacle> Obstacles(Func<PlacedObject, float> modelRadius)
    {
        var obstacles = _fieldObstacles.ToList();
        foreach (var o in Objects)
        {
            if (
                Resolved.TryGetValue(o.Class, out var definition)
                && definition
                    .Find("classLabel")
                    ?.Tokens.FirstOrDefault()
                    ?.Equals(PlanetLabel, StringComparison.OrdinalIgnoreCase) == true
                && modelRadius(o) is var radius
                && radius > 0
                && float.IsFinite(radius)
            )
            {
                obstacles.Add(new Obstacle(o.Position, radius));
            }
        }

        return obstacles;
    }

    SandboxPlan(
        IReadOnlyList<PlacedObject> objects,
        IReadOnlyList<ReportEntry> reports,
        Vector3 ambient,
        string? background,
        MapBounds bounds,
        IReadOnlyList<AsteroidField> fields,
        IReadOnlyList<Obstacle> fieldObstacles,
        IReadOnlyList<BlackHole> blackHoles,
        IReadOnlyDictionary<string, ResolvedDefinition> resolved,
        IReadOnlyList<Nebula> nebulae,
        IReadOnlyList<NebulaKind> nebulaKinds,
        IReadOnlyList<AsteroidSite> asteroidSites
    )
    {
        _fieldObstacles = fieldObstacles;
        BlackHoles = blackHoles;
        Nebulae = nebulae;
        NebulaKinds = nebulaKinds;
        NebulaGrid = new NebulaGrid(nebulae, nebulaKinds, bounds);
        AsteroidSites = asteroidSites;
        Fields = fields;
        Resolved = resolved;
        Objects = objects;
        Reports = reports;
        Ambient = ambient;
        Background = background;
        Bounds = bounds;
    }

    /// <summary>
    /// The resolved definition of each object class of the map, by class name without regard to case, and the
    /// classes whose definition the install holds but that cannot be read, whatever the reason, each logged as one
    /// ERROR with its reason. A class the install has no definition for is in neither. Where the map places a nebula,
    /// the kind classes of the placed nebulae are resolved too, and <c>CannotPathIntoCost</c> is
    /// <c>CANNOT_PATH_INTO_COST</c> of RTS_CFG.h, null where the header or the constant cannot be read (the header's
    /// own failure is logged where the combat plan reads it).
    /// </summary>
    public static (
        IReadOnlyDictionary<string, ResolvedDefinition> Found,
        IReadOnlySet<string> Unreadable,
        float? CannotPathIntoCost
    ) Definitions(GameFileSystem files, MapAsset map)
    {
        var found = new Dictionary<string, ResolvedDefinition>(StringComparer.OrdinalIgnoreCase);
        var unreadable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Resolve(string cls)
        {
            try
            {
                found[cls] = ResolvedDefinition.Resolve(files, cls + ".odf");
            }
            catch (Exception e) when (Log.IsAbsent(e)) { }
            catch (Exception e)
            {
                // an asset failed to load: one ERROR with its name and reason (INV-0005); the gap of its
                // objects is display only, and the map still opens (INV-0002)
                Log.Error(Area.Sandbox, $"object definition {cls}.odf: {e.Message}");
                unreadable.Add(cls);
            }
        }

        foreach (var cls in map.Map.Objects.Select(o => o.Class).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            Resolve(cls);
        }

        // a nebula takes its speed values from the class of its kind, which the map need not place (0x44bf20)
        float? cost = null;
        var nebulaKinds = map
            .Map.Objects.Where(o => found.TryGetValue(o.Class, out var d) && Nebula.IsNebula(d))
            .Select(o => Nebula.KindOf(o.Class))
            .Distinct()
            .ToList();
        if (nebulaKinds.Count > 0)
        {
            foreach (var cls in nebulaKinds.Select(kind => Nebula.KindClasses[kind - 1]))
            {
                if (!found.ContainsKey(cls) && !unreadable.Contains(cls))
                    Resolve(cls);
            }

            try
            {
                if (GameConstantsKind.Of(files).TryNumber("CANNOT_PATH_INTO_COST", out var v, out _))
                    cost = (float)v;
            }
            catch (Exception) { } // simplicity: any failure reading RTS_CFG.h leaves the static value, reported as a gap
        }

        return (found, unreadable, cost);
    }

    /// <summary><c>hasModel</c> tells whether the install holds a model of the name (given without ".sod").</summary>
    public static SandboxPlan Build(
        MapAsset asset,
        (
            IReadOnlyDictionary<string, ResolvedDefinition> Found,
            IReadOnlySet<string> Unreadable,
            float? CannotPathIntoCost
        ) definitions,
        Func<string, bool> hasModel
    )
    {
        var map = asset.Map;
        var gaps = new Gaps();
        var placed = new List<PlacedObject>();
        foreach (var o in map.Objects)
        {
            if (definitions.Unreadable.Contains(o.Class))
            {
                gaps.Add(UnreadableDefinition, o.Class);

                continue;
            }

            if (!definitions.Found.TryGetValue(o.Class, out var definition))
            {
                gaps.Add(MissingDefinition, o.Class);

                continue;
            }

            if (
                definition
                    .Find("classLabel")
                    ?.Tokens.FirstOrDefault()
                    ?.Equals(AsteroidFieldLabel, StringComparison.OrdinalIgnoreCase) == true
            )
                continue;
            var model = ModelOf(o.Class, definition, hasModel, gaps);
            if (model == null)
                continue;
            var (x, y, z) = Orientation(o, gaps);
            placed.Add(
                new PlacedObject
                {
                    Label = o.Label,
                    Class = o.Class,
                    Model = model,
                    Scale = ScaleOf(o.Class, definition, gaps),
                    Team = o.Team,
                    Slot = Slot(map, o.Team, gaps),
                    Position = new Vector3(o.Position.X, o.Position.Y, -o.Position.Z),
                    AxisX = x,
                    AxisY = y,
                    AxisZ = z,
                    // the velocity is in the frame of the position, so its Z is mirrored with it
                    Start = o.Start is { Velocity: { } v } start
                        ? start with
                        {
                            Velocity = new Vector3(v.X, v.Y, -v.Z),
                        }
                        : o.Start,
                }
            );
        }

        var background = BackgroundOf(map, hasModel, gaps);
        var (fields, fieldReports, fieldObstacles) = AsteroidField.Collect(map, definitions.Found, hasModel);
        var blackHoles = BlackHole.Collect(placed, definitions.Found, gaps);
        var (nebulae, nebulaKinds) = Nebula.Collect(map, definitions, gaps);
        var sites = AsteroidSite.Collect(map, definitions.Found, gaps);

        return new SandboxPlan(
            placed,
            Gaps.Logged(gaps.Entries.Concat(fieldReports).ToList(), UnreadableDefinition, Nebula.UnreadableKind),
            map.Ambient,
            background,
            map.Bounds,
            fields,
            fieldObstacles,
            blackHoles,
            definitions.Found,
            nebulae,
            nebulaKinds,
            sites
        );
    }

    // The definition's baseName, else the model named like the class; null when there is none (a baseName
    // naming a model the install lacks is a gap, with no fallback).
    static string? ModelOf(string cls, ResolvedDefinition definition, Func<string, bool> hasModel, Gaps gaps)
    {
        var baseName = definition.Find("baseName")?.Tokens.FirstOrDefault();
        if (baseName == null)
            return hasModel(cls) ? cls : null;
        var name = WithoutExtension(baseName);
        if (hasModel(name))
            return name;
        gaps.Add(MissingModel, name);

        return null;
    }

    // The definition's scaleSOD (Armada.exe 0x46b01d, default 1). stand-in: a value that is no number gives 1; the
    // game keeps whatever its scan leaves in the value.
    static float ScaleOf(string cls, ResolvedDefinition definition, Gaps gaps)
    {
        var value = definition.Find("scaleSOD");
        if (value == null)
            return 1f;
        if (value.Tokens.Count == 1 && ResolvedDefinition.TryNumber(value.Tokens[0], out var scale))
            return scale;
        gaps.Add(BadScale, cls);

        return 1f;
    }

    static string? BackgroundOf(BznMap map, Func<string, bool> hasModel, Gaps gaps)
    {
        if (map.BackgroundArt.Length == 0)
            return null;
        var name = WithoutExtension(map.BackgroundArt);
        if (hasModel(name))
            return name;
        gaps.Add(MissingModel, name);

        return null;
    }

    static string WithoutExtension(string model) =>
        model.EndsWith(".sod", StringComparison.OrdinalIgnoreCase) ? model[..^4] : model;

    // The map's axes (Right, Up, Front) in Godot's frame: the same mirror as SodBuilder's node transform
    // (S * M * S with S = diag(1, 1, -1)). A zero, non-finite, or collapsed set of axes is a gap and gives
    // the identity.
    static (Vector3, Vector3, Vector3) Orientation(MapObject o, Gaps gaps)
    {
        var finite = new[] { o.Right, o.Up, o.Front }.All(v =>
            float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z)
        );
        // The determinant of the unit axes is 1 for orthogonal axes and 0 for collinear or coplanar axes, whatever the
        // scale of the axes. stand-in: under 0.001 counts as collapsed (the game's own limit is not known).
        var lengths = o.Right.Length() * o.Up.Length() * o.Front.Length();
        if (!finite || lengths == 0 || Math.Abs(Vector3.Dot(o.Right, Vector3.Cross(o.Up, o.Front))) / lengths < 0.001f)
        {
            gaps.Add(DegenerateOrientation, $"{o.Label} ({o.Class})");

            return (Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ);
        }

        return (
            new Vector3(o.Right.X, o.Right.Y, -o.Right.Z),
            new Vector3(o.Up.X, o.Up.Y, -o.Up.Z),
            new Vector3(-o.Front.X, -o.Front.Y, o.Front.Z)
        );
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
        if (record == 0)
            return own;
        if (record < 0 || record >= slots)
        {
            gaps.Add(BadSlot, record.ToString());

            return own;
        }

        return record;
    }
}
