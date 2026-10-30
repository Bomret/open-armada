using System;
using System.Collections.Generic;

namespace OpenArmada.Sandbox;

/// <summary>
/// A node of the planner's graph (<c>A_Star_Graph_Node</c>, 0x4070c0): a position in the grid, the directed edges out of
/// it with their costs, and the state of the search that last reached it.
/// </summary>
internal sealed class PathNode(float x, float z)
{
    /// <summary>The node's grid X (vtable +0xc).</summary>
    public float X { get; } = x;

    /// <summary>The node's grid Z (vtable +0x10).</summary>
    public float Z { get; } = z;

    /// <summary>The nodes the edges out of this node lead to, in the order the edges were added.</summary>
    public List<PathNode> Neighbours { get; } = new();

    /// <summary>The cost of each edge of <see cref="Neighbours"/>.</summary>
    public List<float> Costs { get; } = new();

    internal int SearchId { get; set; }
    internal float F { get; set; }
    internal float G { get; set; }
    internal PathNode? Parent { get; set; }
    internal bool Closed { get; set; }

    /// <summary>Adds the edge from this node to <c>to</c> (0x407b10 on this node).</summary>
    public void Link(PathNode to, float cost)
    {
        Neighbours.Add(to);
        Costs.Add(cost);
    }
}

/// <summary>
/// The planner's A* (0x408500, 0x408940, 0x407990, 0x408660), quirks included: a binary heap on the nodes' live f, a
/// heuristic of <c>weight</c> times the straight distance, a node that is open and visited is never improved, a closed
/// node is reopened when the new route beats its f, no node limit.
/// </summary>
internal sealed class PathSearch(float weight)
{
    int _id;
    PathNode _goal = null!;
    readonly List<PathNode> _heap = new();

    /// <summary>
    /// The route from <c>start</c> to <c>goal</c> as the nodes from the first to the last, or null when the goal is
    /// unreachable. Each run is a new search id, so the nodes' states from earlier runs reset on first touch.
    /// </summary>
    public List<PathNode>? Run(PathNode start, PathNode goal)
    {
        _id++;
        _goal = goal;
        _heap.Clear();
        // 0x408940: the start is relaxed from itself at cost 0 (and so enters the heap), closed, and expanded
        Relax(start, start, 0);
        var current = start;
        start.Closed = true;
        Expand(start);
        while (current != goal)
        {
            if (_heap.Count == 0)
                break;
            current = _heap[0];
            var last = _heap[^1];
            _heap[^1] = _heap[0];
            AdjustHeap(0, _heap.Count - 1, last);
            _heap.RemoveAt(_heap.Count - 1);
            current.Closed = true;
            Expand(current);
        }

        if (current != goal)
            return null;
        // the parents back from the goal, so the list runs start to goal; a node without a parent ends the walk early
        var route = new List<PathNode>();
        var node = goal;
        while (node != start)
        {
            route.Add(node);
            if (node.Parent is not { } parent)
            {
                route.Reverse();

                return route;
            }

            node = parent;
        }

        route.Add(start);
        route.Reverse();

        return route;
    }

    void Expand(PathNode node)
    {
        for (var i = 0; i < node.Neighbours.Count; i++)
        {
            Relax(node, node.Neighbours[i], node.Costs[i]);
        }
    }

    void Relax(PathNode from, PathNode node, float cost)
    {
        if (node.SearchId != _id)
        {
            node.SearchId = _id;
            node.F = 0;
            node.G = 0;
            node.Parent = null;
            node.Closed = false;
        }

        if (node.Closed)
        {
            // g(from) + cost + h(n), with h(n) as the node stores it, f - g
            var parent = node.Parent!;
            var viaParent = (float)((double)node.F - parent.G);
            var f = (float)(from.G + (float)(viaParent - ((double)node.G - parent.G)) + cost);
            if (f < node.F)
            {
                node.G = (float)(((double)node.G - node.F) + f);
                node.F = f;
                node.Parent = from;
                Push(node);
                node.Closed = false;
            }
        }

        if (node.F == 0)
        {
            node.G = (float)(from.G + (double)cost);
            var dx = (double)node.X - _goal.X;
            var dz = (double)node.Z - _goal.Z;
            node.F = (float)(node.G + (float)(Math.Sqrt(dx * dx + dz * dz) * weight));
            node.Parent = from;
            Push(node);
        }
    }

    // push_heap with the live f of the nodes: the parent moves down while its f exceeds the new node's
    void Push(PathNode node)
    {
        _heap.Add(node);
        var hole = _heap.Count - 1;
        while (hole > 0)
        {
            var parent = (hole - 1) / 2;
            if (_heap[parent].F <= node.F)
                break;
            _heap[hole] = _heap[parent];
            hole = parent;
        }

        _heap[hole] = node;
    }

    // 0x408d50: the hole walks down to the smaller child (the right one on a tie), then the value sifts back up
    void AdjustHeap(int top, int length, PathNode value)
    {
        var hole = top;
        int child = 0,
            left = 0;
        while (true)
        {
            left = hole * 2;
            child = left + 2;
            if (length <= child)
                break;
            if (_heap[child - 1].F < _heap[child].F)
            {
                child = left + 1;
            }

            _heap[hole] = _heap[child];
            hole = child;
        }

        if (child == length)
        {
            _heap[hole] = _heap[child - 1];
            hole = left + 1;
        }

        while (top < hole)
        {
            var parent = (hole - 1) / 2;
            if (_heap[parent].F <= value.F)
                break;
            _heap[hole] = _heap[parent];
            hole = parent;
        }

        _heap[hole] = value;
    }
}
