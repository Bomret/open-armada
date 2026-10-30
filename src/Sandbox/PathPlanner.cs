using System;
using System.Collections.Generic;
using System.Numerics;
using OpenArmada.Formats;

namespace OpenArmada.Sandbox;

/// <summary>
/// One obstacle the planner writes (0x409900): the rectangle from (<c>X0</c>, <c>Z0</c>) to (<c>X1</c>, <c>Z1</c>) in the
/// map's frame (Z not mirrored), grown by <c>Pad</c> on every side, takes the cell value <c>Value</c>.
/// </summary>
public readonly record struct PathWrite(float X0, float Z0, float X1, float Z1, float Pad, int Value);

/// <summary>
/// The game's path planner (<c>A_Star_Search</c>, 0x40a310): the quadtree of cell costs over the map, the A* over the
/// region graph, and the rubber banding and funnel that straighten the route. A plan is a list of points, the first at
/// the ship and the last at the goal, those between at the middle of the combat band; null where there is no path (the
/// goal is in an impassable cell or off the grid, or no route leads to it) and the ship holds. docs/rules/path-planning.md
/// states the rules with their addresses.
/// </summary>
public sealed class PathPlanner
{
    readonly MapBounds _bounds;
    readonly PathPlanConfig _config;
    readonly PathQuadtree _tree;
    readonly PathSearch _regions;
    readonly PathSearch _portals;

    PathPlanner(MapBounds bounds, PathPlanConfig config, PathQuadtree tree)
    {
        _bounds = bounds;
        _config = config;
        _tree = tree;
        _regions = new PathSearch(config.DefaultTerrainValue);
        _portals = new PathSearch(config.DefaultTerrainValue);
    }

    /// <summary>
    /// The planner over <c>bounds</c> (the map's, in its own frame) with the obstacles <c>writes</c> in order; null where
    /// the config's grid resolution would need more cells than the planner allocates.
    /// </summary>
    public static PathPlanner? Create(MapBounds bounds, PathPlanConfig config, IEnumerable<PathWrite> writes)
    {
        if (PathQuadtree.Create(bounds.MaxX - bounds.MinX, bounds.MaxZ - bounds.MinZ, config) is not { } tree)
            return null;
        var planner = new PathPlanner(bounds, config, tree);
        foreach (var write in writes)
        {
            planner.Write(write);
        }

        return planner;
    }

    /// <summary>
    /// Writes one obstacle (0x409900): the rectangle grown by its pad goes into grid coordinates, each corner put back
    /// into the grid, and the quadtree takes the value on its rule (values only grow, impassable wins). Marks the region
    /// graph dirty; the next plan rebuilds it.
    /// </summary>
    public void Write(PathWrite w)
    {
        float Grid(float world, float min) => _tree.ClampX((world - min) + PathQuadtree.Border);
        float GridZ(float world, float min) => _tree.ClampZ((world - min) + PathQuadtree.Border);
        _tree.Write(
            Grid(w.X0 - w.Pad, _bounds.MinX),
            GridZ(w.Z0 - w.Pad, _bounds.MinZ),
            Grid(w.X1 + w.Pad, _bounds.MinX),
            GridZ(w.Z1 + w.Pad, _bounds.MinZ),
            w.Value
        );
    }

    /// <summary>
    /// The goal put back inside the map (0x44ab30): X and Z each at least <c>MAP_EDGE_BUFFER</c> from the lowest bound, and
    /// from the highest at most the buffer plus <c>MAP_EDGE_EPSILON</c> short of it. Points are in the map's frame.
    /// </summary>
    public Vector2 ClampToMapEdge(Vector2 map)
    {
        var x = map.X;
        var z = map.Y;
        var buffer = _config.MapEdgeBuffer;
        var high = _bounds.MaxX - buffer;
        if (high < x)
        {
            x = high - _config.MapEdgeEpsilon;
        }

        if (x < buffer + _bounds.MinX)
        {
            x = buffer + _bounds.MinX;
        }

        if (z < buffer + _bounds.MinZ)
        {
            z = buffer + _bounds.MinZ;
        }

        high = _bounds.MaxZ - buffer;
        if (z > high)
        {
            z = high - _config.MapEdgeEpsilon;
        }

        return new Vector2(x, z);
    }

    /// <summary>
    /// The planned route from <c>start</c> to <c>goal</c> (the sandbox's frame, Z mirrored) as points: the first is
    /// <c>start</c> and the last is <c>goal</c> as given (the game restores the unclamped goal while the ship is not
    /// forced onto the map, 0x40a310's last step), those between at <c>height</c>. Null when there is no path.
    /// </summary>
    public IReadOnlyList<Vector3>? Plan(Vector3 start, Vector3 goal, float height)
    {
        if (_tree.Dirty)
        {
            _tree.Rebuild();
        }

        var s = new Vector2(start.X, -start.Z);
        var g = ClampToMapEdge(new Vector2(goal.X, -goal.Z));
        var gx = g.X - _bounds.MinX + PathQuadtree.Border;
        var gz = g.Y - _bounds.MinZ + PathQuadtree.Border;
        // 0x40a950: the goal must lie in the grid
        if (!(gx >= 0 && gx <= _tree.ExtentX && gz >= 0 && gz <= _tree.ExtentZ))
            return null;
        gx = _tree.ClampX(gx);
        gz = _tree.ClampZ(gz);
        var goalCell = _tree.Lookup(gx, gz);
        if (_tree.ValueOf(goalCell) == _config.ImpassableTerrain)
            return null;
        var sx = _tree.ClampX(s.X - _bounds.MinX + PathQuadtree.Border);
        var sz = _tree.ClampZ(s.Y - _bounds.MinZ + PathQuadtree.Border);
        var startNode = _tree.NodeOf(_tree.Lookup(sx, sz));
        var goalNode = _tree.NodeOf(goalCell);
        if (startNode == goalNode || !_config.UsePathPlanning)
            return [start, goal];
        if (startNode == null || goalNode == null)
            return null;
        if (_regions.Run(startNode, goalNode) is not { } region)
            return null;
        IReadOnlyList<(float X, float Z)> route = region.ConvertAll(n => (n.X, n.Z));
        if (_config.PerformRubberBanding && region.Count >= 3 && Band(region, (sx, sz), (gx, gz)) is { } banded)
        {
            route = banded;
        }

        var points = new Vector3[route.Count];
        for (var i = 0; i < points.Length; i++)
        {
            var p = ClampToMapEdge(
                new Vector2(
                    _bounds.MinX + route[i].X - PathQuadtree.Border,
                    _bounds.MinZ + route[i].Z - PathQuadtree.Border
                )
            );
            points[i] = new Vector3(p.X, height, -p.Y);
        }

        points[0] = start;
        points[^1] = goal;

        return points;
    }

    // The rubber banding (0x40ac80, 0x40b8c0): a portal graph through the cells of the region route, one pair of
    // endpoints per shared border, searched for the cheapest way through, then pulled straight by the funnel. Null where
    // the portal search fails, and the route stays the cell centers.
    List<(float X, float Z)>? Band(List<PathNode> region, (float X, float Z) start, (float X, float Z) goal)
    {
        var cells = new List<PathCell> { _tree.Lookup(start.X, start.Z) };
        var startNode = new PathNode(start.X, start.Z);
        PathNode first = startNode;
        PathNode? second = null;
        var previous = cells[0];
        for (var i = 1; i < region.Count; i++)
        {
            var cell = _tree.Lookup(region[i].X, region[i].Z);
            cells.Add(cell);
            if (Portal(previous, cell) is not { } portal)
                return null;
            var n1 = new PathNode(portal.X1, portal.Z1);
            var n2 = new PathNode(portal.X2, portal.Z2);
            float value = _tree.ValueOf(previous);
            first.Link(n1, value * Distance(n1, first));
            first.Link(n2, value * Distance(n2, first));
            if (second != null)
            {
                second.Link(n1, value * Distance(n1, second));
                second.Link(n2, value * Distance(n2, second));
            }

            second = n2;
            first = n1;
            previous = cell;
        }

        var goalNode = new PathNode(goal.X, goal.Z);
        float last = _tree.ValueOf(previous);
        first.Link(goalNode, Distance(goalNode, first) * last);
        second!.Link(goalNode, Distance(goalNode, second) * last);
        if (_portals.Run(startNode, goalNode) is not { } path)
            return null;
        cells.Add(_tree.Lookup(goal.X, goal.Z));

        return Funnel(path, cells);
    }

    static float Distance(PathNode a, PathNode b)
    {
        double dx = (double)b.X - a.X,
            dz = (double)b.Z - a.Z;

        return (float)Math.Sqrt(dx * dx + dz * dz);
    }

    // 0x40aa20: the endpoints of the border the route crosses from cell a to cell b, ordered by the direction of travel
    // (case 0: toward higher Z, 1: lower Z, 2: higher X, 3: lower X). Null for the same cell.
    (float X1, float Z1, float X2, float Z2, int Case)? Portal(PathCell a, PathCell b)
    {
        if (a == b)
            return null;
        var (ax0, ax1, az0, az1) = _tree.BoxOf(a);
        var (bx0, bx1, bz0, bz1) = _tree.BoxOf(b);
        var dx = (bx1 + bx0) * 0.5f - (ax0 + ax1) * 0.5f;
        var dz = (bz1 + bz0) * 0.5f - (az1 + az0) * 0.5f;
        if (MathF.Abs(dx) <= MathF.Abs(dz))
        {
            var lo = ax0 <= bx0 ? bx0 : ax0;
            var hi = bx1 <= ax1 ? bx1 : ax1;
            if (0f <= dz)
                return (lo, bz0, hi, bz0, 0);

            return (hi, bz1, lo, bz1, 1);
        }

        var zlo = az0 <= bz0 ? bz0 : az0;
        var zhi = bz1 <= az1 ? bz1 : az1;
        if (dx <= 0f)
            return (bx1, zlo, bx1, zhi, 3);

        return (bx0, zhi, bx0, zlo, 2);
    }

    // The funnel (0x40b8c0): from an anchor, the portals ahead narrow a funnel of two sides (0x40b4f0 orders a portal's
    // endpoints by side, 0x40b680 narrows); the route's own endpoint of each portal must stay inside it. Where one falls
    // outside, the last endpoint inside becomes a waypoint and the new anchor. The goal ends the list.
    List<(float X, float Z)> Funnel(List<PathNode> path, List<PathCell> cells)
    {
        var output = new List<(float X, float Z)> { (path[0].X, path[0].Z) };
        var count = path.Count;
        if (count < 2)
            return output;
        var anchor = path[0];
        var anchorIndex = 0;
        var lastIndex = 0;
        PathNode? lastNode = null;
        var lastCell = cells[0];
        (float X, float Z) sideA = default,
            sideB = default,
            newA = default,
            newB = default;
        PathNode current = path[0];
        var inside = true;
        var i = 1;
        while (true)
        {
            // an accepted portal goes back to this point without the end test below (the game jumps to its loop head)
            if (inside && i < count)
            {
                current = path[i];
                var cell = cells[i];
                var portal = Portal(lastCell, cell);
                if (portal is { } p)
                {
                    // 0x40b4f0: the endpoints ordered by which side of the anchor they lie
                    var n1 = (X: p.X1, Z: p.Z1);
                    var n2 = (X: p.X2, Z: p.Z2);
                    var f5 = (n2.Z - anchor.Z) * (n1.X - anchor.X);
                    var f6 = (n2.X - anchor.X) * (n1.Z - anchor.Z);
                    (newA, newB) = f5 < f6 ? (n1, n2) : (n2, n1);
                }

                if (i == anchorIndex + 1)
                {
                    (sideA, sideB) = (newA, newB);
                    lastIndex = i;
                    lastNode = current;
                    lastCell = cell;
                    i++;

                    continue;
                }

                if (portal is { } q)
                {
                    inside = q.Case switch
                    {
                        0 => anchor.Z < newA.Z,
                        1 => !(anchor.Z <= newA.Z),
                        2 => !(newA.X <= anchor.X),
                        _ => !(anchor.X <= newA.X),
                    };
                    if (inside)
                    {
                        inside = Narrow(anchor, ref sideA, ref sideB, newA, newB);
                    }
                }

                if (inside)
                {
                    var tail =
                        (current.Z - anchor.Z) * (sideA.X - anchor.X) <= (current.X - anchor.X) * (sideA.Z - anchor.Z);
                    var across =
                        (current.Z - anchor.Z) * (sideB.X - anchor.X) < (current.X - anchor.X) * (sideB.Z - anchor.Z);
                    if (tail && !across)
                    {
                        lastIndex = i;
                        lastNode = current;
                        lastCell = cell;
                        i++;

                        continue;
                    }

                    inside = false;
                }
            }

            if (i == count)
            {
                output.Add((current.X, current.Z));
            }
            else
            {
                output.Add((lastNode!.X, lastNode.Z));
                anchor = lastNode;
                anchorIndex = lastIndex;
                i = lastIndex + 1;
                inside = true;
            }

            if (i >= count)
                break;
        }

        return output;
    }

    // 0x40b680: narrows the funnel (sideA, sideB) to the portal (newA, newB) as seen from the anchor, false where a side
    // would cross the other. A side moves in only when the new endpoint lies inside it.
    static bool Narrow(
        PathNode anchor,
        ref (float X, float Z) sideA,
        ref (float X, float Z) sideB,
        (float X, float Z) newA,
        (float X, float Z) newB
    )
    {
        float ax = anchor.X,
            az = anchor.Z;
        var t3 = (newA.Z - az) * (sideA.X - ax);
        var t4 = (newA.X - ax) * (sideA.Z - az);
        if (t3 < t4)
        {
            var u3 = (sideB.X - ax) * (newA.Z - az);
            var u4 = (newA.X - ax) * (sideB.Z - az);
            if (u3 <= u4)
                return false;
            sideA = newA;
        }

        var v3 = (newB.Z - az) * (sideB.X - ax);
        var v4 = (newB.X - ax) * (sideB.Z - az);
        if (v3 <= v4)
            return true;
        var w3 = (sideA.X - ax) * (newB.Z - az);
        var w4 = (newB.X - ax) * (sideA.Z - az);
        if (w3 < w4)
        {
            sideB = newB;

            return true;
        }

        return false;
    }
}
