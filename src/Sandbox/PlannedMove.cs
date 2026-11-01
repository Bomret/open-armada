using System;
using System.Collections.Generic;
using System.Numerics;

namespace OpenArmada.Sandbox;

/// <summary>
/// A move order's list of points and the planning that follows it (the first half of 0x42ded0). The list of a player's
/// move is [the ship's position when it was ordered, the goal] and that of an attack's path move is [the target]. The
/// order index names the point the path is planned to: it starts at -1, a ship within the lead distance of its point
/// passes it, and the planner is asked for a new path from the ship's position whenever there is none. A ship that has not
/// moved more than 1 unit for more than 12 whole seconds while its last point is farther than the lead distance drops its
/// path and its index to 0, which plans the next path to the order's first point.
/// </summary>
internal sealed class PlannedMove(IReadOnlyList<Vector3> points)
{
    // the stuck test (0x5ca7d8, 0x5ca7d0): a squared distance over 1, and more than 12 seconds without it
    const double MovedSquared = 1.0;
    const int StuckSeconds = 12;

    int _storedSecond;
    Vector3? _storedAt;
    bool _hasPath;

    /// <summary>The order's points in the sandbox's frame.</summary>
    public IReadOnlyList<Vector3> Points { get; } = points;

    /// <summary>The index of the point the path leads to; -1 before the first update.</summary>
    public int Index { get; private set; } = -1;

    /// <summary>
    /// One pass of the order's planning for <c>ship</c> at <c>seconds</c> of world time. A new order, a passed point, or a
    /// dropped path asks <c>planner</c> (null: none, the path is the two points [ship, point]) for a path from the ship's
    /// position to the order's point and starts it as the ship's order, with the order's last point as its final point.
    /// False where the planner finds no path: the ship has no order to follow and holds.
    /// </summary>
    public bool Update(FlightModel ship, PathPlanner? planner, double seconds, CombatBand band)
    {
        var at = ship.Pose.Position;
        var lead = ship.PathLeadDistance;
        var leadSquared = lead * lead;
        if (Index < 0)
        {
            Index = 0;
            _hasPath = false;
        }
        else if (Index < Points.Count - 1 && Vector3.DistanceSquared(at, Points[Index]) < leadSquared)
        {
            Index++;
            _hasPath = false;
        }

        if (_hasPath && leadSquared < Vector3.DistanceSquared(at, Points[^1]))
        {
            // the clock in whole seconds (the game truncates it)
            var second = (int)Math.Truncate(seconds);
            if (_storedAt is not { } stored || Vector3.DistanceSquared(at, stored) > MovedSquared)
            {
                _storedSecond = second;
                _storedAt = at;
            }

            if (second - _storedSecond > StuckSeconds)
            {
                Index = 0;
                _hasPath = false;
            }
        }

        if (_hasPath)
            return true;
        var goal = Points[Index];
        IReadOnlyList<Vector3>? path = planner == null ? new[] { at, goal } : planner.Plan(at, goal, band.Middle);
        if (path == null)
            return false;
        ship.StartMove(path, Points[^1]);
        _hasPath = true;

        return true;
    }
}
