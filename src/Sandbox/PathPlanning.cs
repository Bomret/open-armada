using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using OpenArmada.Assets;
using OpenArmada.Diagnostics;
using OpenArmada.Formats;

namespace OpenArmada.Sandbox;

/// <summary>
/// Builds the map's path planner from the install and the plan: the tunables of RTS_CFG.h and every obstacle the map
/// writes (<see cref="PathObstacles"/>).
/// </summary>
public static class PathPlanning
{
    /// <summary>
    /// The planner over the plan's map: the tunables from the install's RTS_CFG.h (a key it lacks takes the game's default,
    /// and the header's own absence the defaults of all, each reported once through <c>gap</c> and as a WARN), the plan's
    /// asteroid fields, belts, nebulae, and black holes, and the starbases <c>stations</c>. Null, reported, where the
    /// header's grid resolution asks for more cells than the planner allocates: the sandbox's paths are then the two points
    /// [ship, goal].
    /// </summary>
    public static PathPlanner? Create(
        GameFileSystem files,
        SandboxPlan plan,
        float formationSpacing,
        IEnumerable<PathStation> stations,
        Action<string> gap
    )
    {
        GameConstants? constants = null;
        try
        {
            constants = GameConstantsKind.Of(files);
        }
        catch (Exception) { } // the combat plan reports a header that is missing or cannot be read

        var gaps = new Gaps();
        var config = PathPlanConfig.Read(constants, formationSpacing, gaps);
        var planner = PathPlanner.Create(
            plan.Bounds,
            config,
            PathObstacles.Writes(config, plan.AsteroidSites, plan.Nebulae, plan.BlackHoles, stations)
        );
        if (planner == null)
        {
            gaps.Add(
                "path planner not built (its grid would need too many cells), move orders are the two points [ship, goal]",
                $"HIGHEST_QUADTREE_GRID_RESOLUTION {config.QuadtreeResolution}"
            );
        }

        foreach (var report in gaps.Entries)
        {
            Log.Warn(Area.Combat, report.Text);
            gap(report.Text);
        }

        return planner;
    }

    /// <summary>
    /// A starbase's footprint (0x46d5c0): the box of its model scaled by its placed scale, at its position. stand-in: the
    /// game takes the object's own box (vtable +4), whose scaling and turning are not recovered; the model's box is not
    /// turned with the object. Null for a model without points.
    /// </summary>
    public static PathStation? Footprint(PlacedObject station, Sod model, IReadOnlyList<SpriteNodeLine> spriteNodes)
    {
        if (ShieldEllipsoid.ModelBox(model, spriteNodes) is not { } box)
            return null;
        var scale = Math.Abs(station.Scale);

        return new PathStation(station.Position, box.Min * scale, box.Max * scale);
    }
}
