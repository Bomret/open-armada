using System;
using System.Collections.Generic;
using System.Numerics;

namespace OpenArmada.Sandbox;

/// <summary>
/// Where the game's group move sends each ship of a selection ordered to one point (0x480c30, 0x47fde0, 0x47fe90,
/// 0x480890, 0x480080, 0x480400, 0x4805d0; docs/rules/forced-motion.md). Plain geometry in the game's frame, the frame
/// of the layout files: Z runs the other way from the sandbox world's, so <see cref="CombatWorld.GroupMove"/> mirrors
/// it on the way in and out.
/// </summary>
public static class Formation
{
    /// <summary>A ship joins an existing group only when its centre is nearer than this (the 800 at 0x5cd740).</summary>
    public const float JoinDistance = 800;

    /// <summary>
    /// The group heading: the plain mean of the bearings to the click of every ship the command was given, with no
    /// wrap-around correction (0x47fde0). That is every selected ship, those that take no order too, which only the
    /// grouping (0x4848e0) leaves out. No ships have heading 0.
    /// </summary>
    public static double Heading(IReadOnlyList<Vector3> ships, Vector2 click)
    {
        if (ships.Count == 0)
            return 0;
        var heading = 0.0;
        foreach (var ship in ships)
        {
            heading += Math.Atan2(click.Y - ship.Z, click.X - ship.X);
        }

        return heading / ships.Count;
    }

    /// <summary>
    /// The destination (X, Z) of each ship, in the order of <paramref name="ships"/> (their positions in selection
    /// order, X, height, Z). The ships form groups by distance; each group is a square block of
    /// <paramref name="spacing"/>-apart cells centred (an even side half a cell off) on its origin, the first at the
    /// click and each next one the previous side wide along the perpendicular of <paramref name="heading"/>
    /// (<see cref="Heading"/>); the ships take their cells nearest-first at <paramref name="midY"/>, the middle of
    /// the combat band. A lone ship is a group of one and goes to the click.
    /// </summary>
    public static Vector2[] Destinations(
        IReadOnlyList<Vector3> ships,
        Vector2 click,
        float spacing,
        float midY,
        double heading
    )
    {
        var result = new Vector2[ships.Count];
        var along = new Vector2((float)Math.Sin(heading), (float)-Math.Cos(heading));

        // each ship joins the group whose running mean centre is nearest and under JoinDistance (the first on a tie),
        // else starts one (0x47fe90)
        var groups = new List<(List<int> Members, Vector3 Sum)>();
        for (var i = 0; i < ships.Count; i++)
        {
            var best = -1;
            var bestDistance = JoinDistance;
            for (var g = 0; g < groups.Count; g++)
            {
                var distance = Vector3.Distance(groups[g].Sum / groups[g].Members.Count, ships[i]);
                if (distance < bestDistance)
                {
                    best = g;
                    bestDistance = distance;
                }
            }

            if (best < 0)
            {
                groups.Add(([i], ships[i]));
            }
            else
            {
                groups[best].Members.Add(i);
                groups[best] = (groups[best].Members, groups[best].Sum + ships[i]);
            }
        }

        var origin = click;
        foreach (var (members, _) in groups)
        {
            var side = (int)Math.Ceiling(Math.Sqrt(members.Count));
            Place(ships, members, origin, side, spacing, midY, result);
            origin += along * (side * spacing);
        }

        return result;
    }

    // One group's block (0x480790, 0x480080, 0x480400, 0x4805d0): the cells in turn, row by row from the row of largest
    // Z and left to right, each take the nearest remaining ship to where the cell lies when the block is anchored at
    // the group's smallest X and Z.
    static void Place(
        IReadOnlyList<Vector3> ships,
        List<int> members,
        Vector2 origin,
        int side,
        float spacing,
        float midY,
        Vector2[] result
    )
    {
        var anchor = new Vector3(float.MaxValue, midY, float.MaxValue);
        foreach (var m in members)
        {
            anchor.X = Math.Min(anchor.X, ships[m].X);
            anchor.Z = Math.Min(anchor.Z, ships[m].Z);
        }

        var remaining = new List<int>(members);
        var half = side / 2; // integer division: the origin is the cell at this index
        for (var row = 0; row < side && remaining.Count > 0; row++)
        {
            for (var column = 0; column < side && remaining.Count > 0; column++)
            {
                var fromBottom = side - 1 - row;
                var cell = new Vector3(anchor.X + column * spacing, midY, anchor.Z + fromBottom * spacing);
                var pick = 0;
                var pickDistance = float.MaxValue;
                for (var k = 0; k < remaining.Count; k++)
                {
                    var distance = Vector3.Distance(cell, ships[remaining[k]]);
                    if (distance < pickDistance)
                    {
                        pick = k;
                        pickDistance = distance;
                    }
                }

                result[remaining[pick]] = new Vector2(
                    origin.X + (column - half) * spacing,
                    origin.Y + (fromBottom - half) * spacing
                );
                remaining.RemoveAt(pick);
            }
        }
    }
}
