using System;
using System.Linq;
using OpenArmada.Formats;

namespace OpenArmada.Sandbox;

/// <summary>
/// The tunables of the path planner, the <c>RTS_CFG.h</c> keys that 0x4453f0 reads into the globals the planner
/// uses. Each default is the value the exe holds before the file is read (docs/rules/path-planning.md, "Keys").
/// </summary>
public sealed record PathPlanConfig
{
    /// <summary><c>HIGHEST_QUADTREE_GRID_RESOLUTION</c> (0x5fbc48): the finest cell of the quadtree.</summary>
    public float QuadtreeResolution { get; init; } = 100;

    /// <summary><c>USE_PATH_PLANNING</c> (0x5fb86c): 0 makes every plan the two points [ship, goal].</summary>
    public bool UsePathPlanning { get; init; } = true;

    /// <summary><c>IMPASSABLE_TERRAIN</c> (0x5fb870): the cell value no path enters.</summary>
    public int ImpassableTerrain { get; init; } = -1;

    /// <summary><c>DEFAULT_TERRAIN_VALUE</c> (0x5fb874): a free cell's value, the heuristic's weight.</summary>
    public int DefaultTerrainValue { get; init; } = 10;

    /// <summary><c>PERFORM_RUBBER_BANDING</c> (0x5fb87c): 0 leaves the cell centres as the path.</summary>
    public bool PerformRubberBanding { get; init; } = true;

    /// <summary><c>NEBULAE_PATHING_SAFETY_COEFFICIENT</c> (0x5fb890): a nebula's pad in effect radii.</summary>
    public float NebulaeSafety { get; init; } = 1.5f;

    /// <summary><c>ASTEROID_BELT_PATHING_COST</c> (0x5fb8a4): the value along an asteroid belt.</summary>
    public float AsteroidBeltCost { get; init; } = 1000;

    /// <summary><c>ASTEROID_BELT_PATHING_DISTANCE</c> (0x5fb8a8): twice the half-size of a belt's or a field's squares.</summary>
    public float AsteroidBeltDistance { get; init; } = 200;

    /// <summary><c>BLACKHOLE_PATHING_COST</c> (0x5fb894): the value around a black hole.</summary>
    public float BlackholeCost { get; init; } = 1000;

    /// <summary><c>BLACKHOLE_PATHING_SAFETY_COEFFICIENT</c> (0x5fb898): a black hole's pad in event radii.</summary>
    public float BlackholeSafety { get; init; } = 1.5f;

    /// <summary><c>FORMATION_SPACING</c> (0x5fbc20): the asteroid field's outer ring adds it to the pad.</summary>
    public float FormationSpacing { get; init; } = 40;

    /// <summary><c>MAP_EDGE_BUFFER</c> (0x5fbc44): how far inside the map's edge a goal is clamped (0x44ab30).</summary>
    public float MapEdgeBuffer { get; init; } = 100;

    /// <summary><c>MAP_EDGE_EPSILON</c> (0x5fbc40): the step back from the high edges of the clamp (0x44ab30).</summary>
    public float MapEdgeEpsilon { get; init; } = 0.1f;

    /// <summary>The values the exe holds before RTS_CFG.h is read.</summary>
    public static PathPlanConfig Game { get; } = new();

    const string Missing = "path planning key missing, no number, or declared as another type, the game's default used";
    const string Unusable = "path planning key has a value the planner cannot use, the game's default used";

    /// <summary>
    /// The planner's tunables from <c>constants</c> (null where RTS_CFG.h could not be read: the defaults). A key the
    /// header lacks, a key that is no number, and a key declared with another type than the game's reader takes (an int
    /// is read through an <c>int</c> declaration only, a float through a <c>float</c> one, 0x4fc0d0 and 0x4fc1d0) take the
    /// game's default; each is reported once under one cause, with the key names (INV-0002). An identifier value
    /// (<c>BLACKHOLE_PATHING_COST = IMPASSABLE_TERRAIN</c>) is the number of the earlier declaration it names, which
    /// <see cref="GameConstants"/> evaluates.
    /// </summary>
    internal static PathPlanConfig Read(GameConstants? constants, float formationSpacing, Gaps gaps)
    {
        var defaults = Game with { FormationSpacing = formationSpacing };
        if (constants == null)
            return defaults;

        double? Number(string key, string type)
        {
            var declared = constants.Values.FirstOrDefault(v => v.Name == key);
            if (!constants.TryNumber(key, out var value, out _) || declared?.Type != type)
            {
                gaps.Add(Missing, key);

                return null;
            }

            return value;
        }

        float Float(string key, float fallback, Func<float, bool>? usable = null)
        {
            if (Number(key, "float") is not { } v)
                return fallback;
            if (!float.IsFinite((float)v) || usable?.Invoke((float)v) == false)
            {
                gaps.Add(Unusable, key);

                return fallback;
            }

            return (float)v;
        }

        int Int(string key, int fallback)
        {
            if (Number(key, "int") is not { } v)
                return fallback;
            if (v != Math.Truncate(v) || Math.Abs(v) > int.MaxValue)
            {
                gaps.Add(Unusable, key);

                return fallback;
            }

            return (int)v;
        }

        var resolution = Float("HIGHEST_QUADTREE_GRID_RESOLUTION", defaults.QuadtreeResolution, v => v > 0);
        return defaults with
        {
            QuadtreeResolution = resolution,
            UsePathPlanning = Int("USE_PATH_PLANNING", 1) != 0,
            ImpassableTerrain = Int("IMPASSABLE_TERRAIN", defaults.ImpassableTerrain),
            DefaultTerrainValue = Int("DEFAULT_TERRAIN_VALUE", defaults.DefaultTerrainValue),
            PerformRubberBanding = Int("PERFORM_RUBBER_BANDING", 1) != 0,
            NebulaeSafety = Float("NEBULAE_PATHING_SAFETY_COEFFICIENT", defaults.NebulaeSafety),
            AsteroidBeltCost = Float("ASTEROID_BELT_PATHING_COST", defaults.AsteroidBeltCost),
            AsteroidBeltDistance = Float("ASTEROID_BELT_PATHING_DISTANCE", defaults.AsteroidBeltDistance),
            BlackholeCost = Float("BLACKHOLE_PATHING_COST", defaults.BlackholeCost),
            BlackholeSafety = Float("BLACKHOLE_PATHING_SAFETY_COEFFICIENT", defaults.BlackholeSafety),
            MapEdgeBuffer = Float("MAP_EDGE_BUFFER", defaults.MapEdgeBuffer),
            MapEdgeEpsilon = Float("MAP_EDGE_EPSILON", defaults.MapEdgeEpsilon),
        };
    }
}
