using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using OpenArmada.Assets;
using OpenArmada.Formats;

namespace OpenArmada.Sandbox;

/// <summary>
/// What a nebula kind (1 to 5) does to a ship's speed: the keys of the class named <c>mnebula&lt;kind&gt;</c> (0x44bf20),
/// whichever class the placed nebula has. A ship in the kind's area takes the lower of the coefficient and, when the
/// limit is 0 or more, the limit (0x4690c0).
/// </summary>
public sealed record NebulaKind
{
    /// <summary>The game's defaults for a key the definition lacks (0x44bfa0).</summary>
    public const float DefaultCoefficient = 0.5f;

    /// <inheritdoc cref="DefaultCoefficient"/>
    public const float DefaultLimit = -1f;

    /// <summary>The kind, 1 to 5.</summary>
    public required int Kind { get; init; }

    /// <summary>The class's <c>craft_speed_coefficient</c>: the factor of the forward speed.</summary>
    public float Coefficient { get; init; } = DefaultCoefficient;

    /// <summary>The class's <c>craft_speed_limit</c>: a cap on the forward speed; below 0 (the default) means none.</summary>
    public float Limit { get; init; } = DefaultLimit;
}

/// <summary>
/// A nebula of the map (a placed object whose definition's classLabel is <c>nebula_obj</c>). It is planned, not drawn
/// by the plan: its area is a sphere of <see cref="EffectRadius"/> around <see cref="Center"/>, cut by the plane
/// y = 0 (0x43ea20), and the path planner writes a square of cost <see cref="PathingCost"/> around it (0x451e00).
/// </summary>
public sealed record Nebula
{
    /// <summary>
    /// The path cost of a nebula whose definition sets no <c>pathing_cost</c> where the install's RTS_CFG.h has the
    /// stock <c>CANNOT_PATH_INTO_COST</c> of 200: that constant less 1 (0x44c2f4, 0x44c301).
    /// </summary>
    public const float DefaultPathingCost = StaticCannotPathIntoCost - 1;

    // The game's static value of CANNOT_PATH_INTO_COST, which it keeps where RTS_CFG.h gives none (0x5fb878, set by
    // 0x4453f0; confirmed). A gap is still reported for the missing constant.
    const float StaticCannotPathIntoCost = 200f;

    const string CostConstant = "CANNOT_PATH_INTO_COST";

    /// <summary>The classes whose values the five kinds take (0x44bf20), by kind 1 to 5.</summary>
    internal static readonly IReadOnlyList<string> KindClasses =
    [
        "mnebula1",
        "mnebula2",
        "mnebula3",
        "mnebula4",
        "mnebula5",
    ];

    const string ClassLabel = "nebula_obj"; // NebulaClass names itself so (0x44bfa0)
    const string BadRadius = "nebula effectRadius missing or not a number of 0 or more, nebulae left out";
    const string BadPathingCost = "nebula pathing_cost is no number, the game's default used";
    const string BadSpeedKey = "nebula speed key is no number, the game's default used";
    const string MissingCostConstant =
        "game constant CANNOT_PATH_INTO_COST missing or no number, the game's static 200 used for the nebula pathing_cost default";
    const string MissingKind = "nebula kind definition missing, the game's speed defaults used";

    /// <summary>The gap of a kind class that is in the install and cannot be read; display only, as its failure is logged where it happened.</summary>
    internal const string UnreadableKind = "nebula kind definition cannot be read, the game's speed defaults used";

    internal static bool IsNebula(ResolvedDefinition definition) =>
        definition.Find("classLabel")?.Tokens.FirstOrDefault()?.Equals(ClassLabel, StringComparison.OrdinalIgnoreCase)
        == true;

    // The kind of a nebula class (0x44bfa0 compares the class name with the names mnebula1 to mnebula5 byte for byte,
    // so by case; any other name gives 5). Whether the game changes the case of the class name before it compares is
    // not recovered.
    internal static int KindOf(string cls)
    {
        for (var i = 0; i < KindClasses.Count; i++)
        {
            if (KindClasses[i].Equals(cls, StringComparison.Ordinal))
                return i + 1;
        }

        return KindClasses.Count;
    }

    // A number key of a definition: the value, the default where the key is omitted, and null (a gap) where it is no number.
    static float? Number(ResolvedDefinition definition, string key, float fallback) =>
        definition.Find(key) is not { } value ? fallback
        : value.Tokens.Count == 1 && ResolvedDefinition.TryNumber(value.Tokens[0], out var number) ? number
        : null;

    /// <summary>
    /// The nebulae of the map, each from its definition (a class whose classLabel is <c>nebula_obj</c>), and the speed
    /// values of each kind some nebula has, read from the class of that kind. An <c>effectRadius</c> that is missing or
    /// no number (or below 0) leaves the nebula out; a class that cannot be read leaves out its objects (the plan's
    /// own gap says so). A speed key or <c>pathing_cost</c> that is no number, and a kind class that is missing or
    /// unreadable, are reported and take the game's default (INV-0002).
    /// </summary>
    internal static (IReadOnlyList<Nebula> Nebulae, IReadOnlyList<NebulaKind> Kinds) Collect(
        BznMap map,
        (
            IReadOnlyDictionary<string, ResolvedDefinition> Found,
            IReadOnlySet<string> Unreadable,
            float? CannotPathIntoCost
        ) definitions,
        Gaps gaps
    )
    {
        var nebulae = new List<Nebula>();
        foreach (var o in map.Objects)
        {
            if (!definitions.Found.TryGetValue(o.Class, out var d) || !IsNebula(d))
                continue;
            if (Number(d, "effectRadius", -1) is not { } radius || radius < 0)
            {
                gaps.Add(BadRadius, o.Class, o);

                continue;
            }

            // the default is the constant less 1 (0x44c2f4, 0x44c301), the constant being its static value where the
            // install's RTS_CFG.h gives none (reported where a nebula needs it)
            var fallback = (definitions.CannotPathIntoCost ?? StaticCannotPathIntoCost) - 1;
            var cost = Number(d, "pathing_cost", fallback);
            if (cost == null)
            {
                gaps.Add(BadPathingCost, $"{o.Class}: pathing_cost", o);
            }
            else if (d.Find("pathing_cost") == null && definitions.CannotPathIntoCost == null)
            {
                gaps.Add(MissingCostConstant, CostConstant);
            }

            nebulae.Add(
                new Nebula
                {
                    Label = o.Label,
                    Class = o.Class,
                    Kind = KindOf(o.Class),
                    // the plan's frame mirrors Z, as the placed objects' positions do
                    Center = new Vector3(o.Position.X, o.Position.Y, -o.Position.Z),
                    EffectRadius = radius,
                    PathingCost = cost ?? fallback,
                }
            );
        }

        var kinds = new List<NebulaKind>();
        foreach (var kind in nebulae.Select(n => n.Kind).Distinct().Order())
        {
            var cls = KindClasses[kind - 1];
            if (!definitions.Found.TryGetValue(cls, out var d))
            {
                // an unreadable class was logged as an ERROR where it failed to load (INV-0005); its gap is display only
                gaps.Add(definitions.Unreadable.Contains(cls) ? UnreadableKind : MissingKind, cls);
                kinds.Add(new NebulaKind { Kind = kind });

                continue;
            }

            float Read(string key, float fallback)
            {
                if (Number(d, key, fallback) is { } value)
                    return value;
                gaps.Add(BadSpeedKey, $"{cls}: {key}");

                return fallback;
            }

            kinds.Add(
                new NebulaKind
                {
                    Kind = kind,
                    Coefficient = Read("craft_speed_coefficient", NebulaKind.DefaultCoefficient),
                    Limit = Read("craft_speed_limit", NebulaKind.DefaultLimit),
                }
            );
        }

        return (nebulae, kinds);
    }

    /// <summary>The map object's label.</summary>
    public required string Label { get; init; }

    /// <summary>The map object's class, the name of its definition without ".odf".</summary>
    public required string Class { get; init; }

    /// <summary>The nebula kind, 1 to 5: the class name <c>mnebula&lt;kind&gt;</c>, else 5 (0x44bfa0).</summary>
    public required int Kind { get; init; }

    /// <summary>The nebula's center in Godot's frame (Z mirrored), as the units' positions are.</summary>
    public required Vector3 Center { get; init; }

    /// <summary>The definition's <c>effectRadius</c> (class +0x1ec).</summary>
    public required float EffectRadius { get; init; }

    /// <summary>The definition's <c>pathing_cost</c> (class +0x258), the cost the path planner writes around the nebula.</summary>
    public float PathingCost { get; init; } = DefaultPathingCost;
}

/// <summary>
/// The nebula grid of the game (0x43e880): 50-unit cells over the map's bounds, each counting per nebula kind how many
/// nebulae cover it. A nebula covers a cell when the cell's center, at y = 0, lies within the nebula's
/// <see cref="Nebula.EffectRadius"/> of its center (0x43ea20).
/// </summary>
public sealed class NebulaGrid
{
    /// <summary>The cell size (0x5cabe0).</summary>
    public const float CellSize = 50f;

    const float InverseCell = 1f / CellSize;

    /// <summary>The kinds, 1 to 5.</summary>
    const int KindCount = 5;

    readonly NebulaKind?[] _kinds = new NebulaKind?[KindCount + 1];
    readonly Dictionary<(int Col, int Row), int[]> _counts = new();
    readonly float _minX,
        _minZ;
    readonly int _cols,
        _rows;

    /// <summary>A grid without nebulae.</summary>
    public static NebulaGrid Empty { get; } =
        new(
            [],
            [],
            new MapBounds
            {
                MinX = 0,
                MaxX = 0,
                MinY = 0,
                MaxY = 0,
                MinZ = 0,
                MaxZ = 0,
            }
        );

    /// <summary>
    /// The grid of the nebulae over <c>bounds</c>, the map's bounds in the map's own frame (Z not mirrored): the
    /// loader reads them (0x44abb0) before it builds the grid, so they are the grid's origin and extent
    /// (<c>(max + 0.5 - min) / 50</c> cells each way, truncated, 0x43e880). <c>kinds</c> holds the values of each
    /// kind the plan could read; a nebula of another kind applies nothing.
    /// </summary>
    public NebulaGrid(IReadOnlyList<Nebula> nebulae, IReadOnlyList<NebulaKind> kinds, MapBounds bounds)
    {
        foreach (var kind in kinds.Where(k => k.Kind is >= 1 and <= KindCount))
        {
            _kinds[kind.Kind] = kind;
        }

        _minX = bounds.MinX;
        _minZ = bounds.MinZ;
        _cols = Cells((double)bounds.MaxX + 0.5 - bounds.MinX);
        _rows = Cells((double)bounds.MaxZ + 0.5 - bounds.MinZ);
        foreach (var nebula in nebulae.Where(n => n.Kind is >= 1 and <= KindCount))
        {
            Mark(nebula);
        }
    }

    static int Cells(double extent) => (int)Math.Clamp(Math.Truncate(extent / CellSize), 0, int.MaxValue);

    // 0x43ea20: the cells whose center at y = 0 lies within the radius of the nebula's center, over the box of the
    // radius clamped to the grid. The center is in the map's frame, Z not mirrored.
    void Mark(Nebula nebula)
    {
        double x = nebula.Center.X,
            y = nebula.Center.Y,
            z = -(double)nebula.Center.Z,
            r = nebula.EffectRadius;
        if (!double.IsFinite(x + y + z + r) || r < 0 || _cols == 0 || _rows == 0)
            return;
        var c0 = Index((x - r - _minX) / CellSize, _cols);
        var c1 = Index((x + r - _minX) / CellSize, _cols);
        var r0 = Index((z - r - _minZ) / CellSize, _rows);
        var r1 = Index((z + r - _minZ) / CellSize, _rows);
        for (var row = r0; row <= r1; row++)
        {
            for (var col = c0; col <= c1; col++)
            {
                var cx = col * (double)CellSize + CellSize * 0.5 + _minX;
                var cz = row * (double)CellSize + CellSize * 0.5 + _minZ;
                if ((x - cx) * (x - cx) + y * y + (z - cz) * (z - cz) <= r * r)
                {
                    if (!_counts.TryGetValue((col, row), out var counts))
                    {
                        _counts[(col, row)] = counts = new int[KindCount + 1];
                    }

                    counts[nebula.Kind]++;
                }
            }
        }
    }

    // a cell coordinate truncated toward zero and clamped to the grid
    static int Index(double cells, int count) => (int)Math.Clamp(Math.Truncate(cells), 0, count - 1);

    /// <summary>
    /// The kinds that apply at a ship's position (in Godot's frame, Z mirrored), each once however many nebulae of the
    /// kind cover its cell (0x43edc0). The ship's height and size do not count. The cell is the position's truncated
    /// offset from the grid origin, so a position less than a cell below the origin is in the first cell; one beyond
    /// the grid is in none.
    /// </summary>
    public IReadOnlyList<NebulaKind> At(Vector3 position)
    {
        var col = Truncated((position.X - _minX) * InverseCell);
        var row = Truncated((-position.Z - _minZ) * InverseCell);
        if (col < 0 || col >= _cols || row < 0 || row >= _rows || !_counts.TryGetValue((col, row), out var counts))
            return [];
        var applying = new List<NebulaKind>();
        for (var kind = 1; kind <= KindCount; kind++)
        {
            if (counts[kind] != 0 && _kinds[kind] is { } values)
            {
                applying.Add(values);
            }
        }

        return applying;
    }

    // _ftol: toward zero; a position that is no number or too far out of range is in no cell
    static int Truncated(float cells) =>
        float.IsFinite(cells) ? (int)Math.Clamp(MathF.Truncate(cells), -1e9f, 1e9f) : -1;
}
