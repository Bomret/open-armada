using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace OpenArmada.Sandbox;

/// <summary>
/// A starbase's footprint for the planner: the placed position (the sandbox's frame, Z mirrored) and the box of its
/// model in the model's own coordinates (the game's frame), already scaled by the placed object's scale.
/// </summary>
public readonly record struct PathStation(Vector3 Position, Vector3 BoxMin, Vector3 BoxMax);

/// <summary>
/// The obstacles the game writes into the planner (docs/rules/path-planning.md, "Obstacles"): asteroid fields and belts
/// (0x44e7b0, 0x434c30), nebulae (0x451e00), black holes (0x450810), and starbases (0x46d5c0). Planets and ships are
/// no obstacles. Wormholes, construction objects, and scrap do not exist in the sandbox.
/// </summary>
public static class PathObstacles
{
    /// <summary>The value of a starbase's footprint (the literal 198 of 0x46d5c0).</summary>
    public const int StationCost = 198;

    // The belt is sampled in steps of 0.01 of its path parameter (0x5caa90).
    const float BeltStep = 0.01f;

    /// <summary>
    /// The writes the map's obstacles make, in the map's frame. The game writes each as its object is created, in the
    /// map's object order; the sandbox keeps the kinds apart and writes asteroid fields, nebulae, black holes, and
    /// starbases in that order (stand-in). Only a cell value below the default makes the order matter (a value never
    /// lowers a cell that already holds a higher one).
    /// </summary>
    public static IReadOnlyList<PathWrite> Writes(
        PathPlanConfig config,
        IEnumerable<AsteroidSite> sites,
        IEnumerable<Nebula> nebulae,
        IEnumerable<BlackHole> holes,
        IEnumerable<PathStation> stations
    )
    {
        var writes = new List<PathWrite>();
        var half = config.AsteroidBeltDistance * 0.5f;
        foreach (var site in sites)
        {
            if (site.Belt is { } belt)
            {
                Belt(config, belt, half, writes);

                continue;
            }

            // an outer ring at three times the default value, then the impassable core (0x44e7b0); the game's switch for the
            // ring (0x5fdfc8) is 1 and nothing sets it
            var (x, z) = (site.Center.X, -site.Center.Z);
            var ring = half + config.FormationSpacing;
            writes.Add(new PathWrite(x, z, x, z, ring, (int)(config.DefaultTerrainValue * 3.0)));
            writes.Add(new PathWrite(x, z, x, z, half, config.ImpassableTerrain));
        }

        foreach (var nebula in nebulae)
        {
            var (x, z) = (nebula.Center.X, -nebula.Center.Z);
            writes.Add(new PathWrite(x, z, x, z, config.NebulaeSafety * nebula.EffectRadius, (int)nebula.PathingCost));
        }

        foreach (var hole in holes)
        {
            // the hole's class copies its EventRadius over effectRadius (0x44b7d0), and 0x450810 pads by that radius
            var (x, z) = (hole.Center.X, -hole.Center.Z);
            writes.Add(new PathWrite(x, z, x, z, config.BlackholeSafety * hole.EventRadius, (int)config.BlackholeCost));
        }

        foreach (var station in stations)
        {
            var (x, z) = (station.Position.X, -station.Position.Z);
            writes.Add(
                new PathWrite(
                    station.BoxMin.X + x,
                    station.BoxMin.Z + z,
                    station.BoxMax.X + x,
                    station.BoxMax.Z + z,
                    0,
                    StationCost
                )
            );
        }

        return writes;
    }

    // 0x434c30: the path parameter walks from 0 in steps of 0.01 up to the point count; the first point and every point
    // farther than the half distance from the last written one (in 3D) gets a square of that half size.
    static void Belt(PathPlanConfig config, IReadOnlyList<Vector3> sandboxPoints, float half, List<PathWrite> writes)
    {
        if (sandboxPoints.Count == 0)
            return;
        var points = sandboxPoints.Select(p => new Vector3(p.X, p.Y, -p.Z)).ToArray();
        var value = (int)config.AsteroidBeltCost;
        Vector3? written = null;
        for (var t = 0f; t < points.Length; t += BeltStep)
        {
            var p = PointAt(points, t);
            if (written is not { } last || half < Math.Sqrt(Vector3.DistanceSquared(p, last)))
            {
                writes.Add(new PathWrite(p.X, p.Z, p.X, p.Z, half, value));
                written = p;
            }
        }
    }

    /// <summary>
    /// The point of a path at parameter <c>t</c> (0x4af590): the integer part picks the segment and the fraction goes
    /// through a cubic Hermite curve between its end points, whose tangents point along the neighbours' chords and are
    /// as long as the segment (0x4af2c0); the path's ends reflect the end segment for the missing neighbour. Past the
    /// last point it is the last point, and a segment shorter than 0.03 stays at its start.
    /// </summary>
    internal static Vector3 PointAt(IReadOnlyList<Vector3> points, float t)
    {
        var count = points.Count;
        var index = (int)Math.Floor(t);
        if (index < 0)
            return points[0];
        if (index >= count - 1)
            return points[count - 1];
        var p1 = points[index];
        var p2 = points[index + 1];
        if (Vector3.DistanceSquared(p1, p2) < 0.001f)
            return p1;
        var before = index == 0 ? p1 + p1 - p2 : points[index - 1];
        var after = index == count - 2 ? p2 + p2 - p1 : points[index + 2];
        var f = t - index; // fmod(t, 1) for t >= 0
        var t2 = f * f;
        var t3 = t2 * f;
        var h00 = t3 + t3 - t2 * 3f + 1f;
        var h01 = t3 * -2f + t2 * 3f;
        var h10 = t3 - (t2 + t2) + f;
        var h11 = t3 - t2;
        var length = Vector3.Distance(p1, p2);
        var m0 = Tangent(p2 - before) * length;
        var m1 = Tangent(after - p1) * length;

        return m0 * h10 + p1 * h00 + p2 * h01 + m1 * h11;
    }

    // The chord scaled to unit length (the game multiplies by 0x55dd50 of the squared length, an inverse root); a chord of
    // no length stays 0 (stand-in: the game would divide by 0).
    static Vector3 Tangent(Vector3 chord) =>
        chord.LengthSquared() > 0 ? chord * (1f / MathF.Sqrt(chord.LengthSquared())) : Vector3.Zero;
}
