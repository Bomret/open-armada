using System;
using System.Collections.Generic;

namespace OpenArmada.Sandbox;

/// <summary>A cell of the quadtree: its level (0 is the whole grid), column (X) and row (Z).</summary>
internal readonly record struct PathCell(int Level, int Col, int Row);

/// <summary>
/// The planner's quadtree (0x408e30, 0x409900, 0x40c700) and the region graph over its leaves (0x4093b0): the grid is
/// the map plus 500 on every side, cut into cells of <c>HIGHEST_QUADTREE_GRID_RESOLUTION</c>, and a cell is split into
/// four wherever an obstacle covers it in part. Cell values only grow and impassable wins. Everything is in grid
/// coordinates, the map's X and Z less the map's minimum plus 500.
/// </summary>
internal sealed class PathQuadtree
{
    /// <summary>The offset of the grid's origin from the map's (0x5ca220).</summary>
    public const float Border = 500f;

    // 0x40c700 tests rectangles against the cells with this slack (the quadtree's field [9], 0 from its constructor)
    const float Epsilon = 0f;

    // The finest level has at most this many cells; a resolution that asks for more is no usable value (the game
    // allocates them all; a modified install would stall the sandbox).
    const double MostFinestCells = 4_000_000;

    sealed class Level(int cols, int rows, float size, int initial)
    {
        public int Cols { get; } = cols;
        public int Rows { get; } = rows;
        public float Size { get; } = size;
        public int[] Value { get; } = Enumerable(cols * rows, initial);
        public bool[] Split { get; } = new bool[cols * rows];
        public PathNode?[] Node { get; } = new PathNode?[cols * rows];

        static int[] Enumerable(int count, int value)
        {
            var values = new int[count];
            Array.Fill(values, value);

            return values;
        }
    }

    // a leaf of the region graph: the cell's box, value and node
    sealed record Leaf(float X0, float X1, float Z0, float Z1, int Value, PathNode Node);

    readonly Level[] _levels;
    readonly int _default;
    readonly int _impassable;

    /// <summary>The grid's extent along X as the game keeps it, a float (field [4], 0x408ee9).</summary>
    public float ExtentX { get; }

    /// <summary>The grid's extent along Z, a float (field [5], 0x408eec).</summary>
    public float ExtentZ { get; }

    // the extents cut down to a whole number of finest cells, kept as floats (fields [6] and [7], 0x409054, 0x409074): where
    // a point beyond the grid is put back
    readonly float _snapX,
        _snapZ;

    /// <summary>Set by every write; the region graph is rebuilt before the next plan while it is set.</summary>
    public bool Dirty { get; private set; } = true;

    /// <summary>
    /// The quadtree over the map of the given extent (<c>width</c> and <c>depth</c> in world units) with the four
    /// impassable strips along its edges (0x408e30), or null where the finest level would hold more than
    /// <see cref="MostFinestCells"/> cells.
    /// </summary>
    public static PathQuadtree? Create(float width, float depth, PathPlanConfig config)
    {
        var extentX = Border + Border + MathF.Abs(width);
        var extentZ = Border + Border + MathF.Abs(depth);
        var cell = config.QuadtreeResolution;
        var cols = (int)Math.Ceiling((double)(extentX / cell));
        var rows = (int)Math.Ceiling((double)(extentZ / cell));
        if ((double)cols * rows > MostFinestCells || cols < 1 || rows < 1)
            return null;

        var tree = new PathQuadtree(extentX, extentZ, cols, rows, config);
        // the border: strips of the cell plus 500 along each edge (0x408e30's last four writes)
        var strip = cell + Border;
        tree.Write(0, 0, extentX, strip, tree._impassable);
        tree.Write(0, extentZ - strip, extentX, extentZ, tree._impassable);
        tree.Write(0, 0, strip, extentZ, tree._impassable);
        tree.Write(extentX - strip, 0, extentX, extentZ, tree._impassable);

        return tree;
    }

    PathQuadtree(float extentX, float extentZ, int cols, int rows, PathPlanConfig config)
    {
        _default = config.DefaultTerrainValue;
        _impassable = config.ImpassableTerrain;
        ExtentX = extentX;
        ExtentZ = extentZ;
        // the levels: the finest holds cols x rows cells, each coarser level half as many (rounded up), down to one cell
        var depth = Math.Max(CeilLog2(cols), CeilLog2(rows)) + 1;
        _levels = new Level[depth];
        var size = config.QuadtreeResolution;
        for (var level = depth - 1; level >= 0; level--)
        {
            _levels[level] = new Level(cols, rows, size, _default);
            size += size;
            cols = (cols + 1) / 2;
            rows = (rows + 1) / 2;
        }

        var finest = _levels[^1].Size;
        _snapX = (float)Math.Floor((double)(extentX / finest)) * finest;
        _snapZ = (float)Math.Floor((double)(extentZ / finest)) * finest;
    }

    // the smallest n with 2^n >= count (the game takes ceil(log(count) / log(2)); exact for every count)
    static int CeilLog2(int count)
    {
        var bits = 0;
        while ((1L << bits) < count)
        {
            bits++;
        }

        return bits;
    }

    /// <summary>The value of the cell.</summary>
    public int ValueOf(PathCell c) => _levels[c.Level].Value[Index(c)];

    /// <summary>The graph node of the cell, null while the graph has not been built.</summary>
    public PathNode? NodeOf(PathCell c) => _levels[c.Level].Node[Index(c)];

    /// <summary>The cell's box: lower and upper X and Z.</summary>
    public (float X0, float X1, float Z0, float Z1) BoxOf(PathCell c)
    {
        var size = _levels[c.Level].Size;
        var x0 = c.Col * size;
        var z0 = c.Row * size;

        return (x0, x0 + size, z0, z0 + size);
    }

    int Index(PathCell c) => c.Row * _levels[c.Level].Cols + c.Col;

    bool InRange(int level, int col, int row) =>
        col >= 0 && row >= 0 && col < _levels[level].Cols && row < _levels[level].Rows;

    /// <summary>
    /// A grid X put back into the grid: below 0 it is 0, and from the extent on it is the snapped extent
    /// (0x40a950, 0x40c5e0's clamps).
    /// </summary>
    public float ClampX(float x) =>
        x < 0 ? 0
        : ExtentX < x ? _snapX - Epsilon
        : x;

    /// <inheritdoc cref="ClampX"/>
    public float ClampZ(float z) =>
        z < 0 ? 0
        : ExtentZ < z ? _snapZ - Epsilon
        : z;

    // The write rule (0x40c700 with the force flag set): a value goes in where it exceeds the old one and the old one is
    // not impassable, where it is impassable, or where the old one is the default.
    bool Combines(int old, int value) => (old < value && old != _impassable) || value == _impassable || old == _default;

    /// <summary>
    /// Writes <c>value</c> into the rectangle (x0, z0) to (x1, z1) of the grid (0x40c700 from the root): a cell the
    /// rectangle covers whole takes the value, a cell it covers in part is split, and at the finest level any cell the
    /// rectangle touches, edges included, takes it.
    /// </summary>
    public void Write(float x0, float z0, float x1, float z1, int value) => Write(x0, z0, x1, z1, value, 0, 0, 0);

    void Write(float rx0, float rz0, float rx1, float rz1, int value, int level, int col, int row)
    {
        if (!InRange(level, col, row))
            return;
        var lv = _levels[level];
        var size = lv.Size;
        var cx0 = col * size;
        var cz0 = row * size;
        var cx1 = cx0 + size;
        var cz1 = cz0 + size;
        var at = row * lv.Cols + col;
        var covers = !(cx0 < rx0 - Epsilon || rx1 + Epsilon < cx1 || cz0 < rz0 - Epsilon || rz1 + Epsilon < cz1);
        if (covers && !lv.Split[at])
        {
            if (Combines(lv.Value[at], value))
            {
                lv.Value[at] = value;
            }

            Dirty = true;

            return;
        }

        var ix0 = Math.Max(cx0, rx0);
        var iz0 = Math.Max(rz0, cz0);
        var ix1 = Math.Min(cx1, rx1);
        var iz1 = Math.Min(cz1, rz1);
        if (ix0 <= ix1 && iz0 <= iz1)
        {
            if (level == _levels.Length - 1)
            {
                if (Combines(lv.Value[at], value))
                {
                    lv.Value[at] = value;
                    Dirty = true;

                    return;
                }
            }
            else
            {
                if (!lv.Split[at])
                {
                    // the four children take the cell's value on the same rule (0x40c700, 0x40cee0)
                    var below = _levels[level + 1];
                    foreach (var (cc, cr) in Children(col, row))
                    {
                        if (InRange(level + 1, cc, cr) && Combines(below.Value[cr * below.Cols + cc], lv.Value[at]))
                        {
                            below.Value[cr * below.Cols + cc] = lv.Value[at];
                        }
                    }
                }

                lv.Split[at] = true;
                foreach (var (cc, cr) in Children(col, row))
                {
                    Write(rx0, rz0, rx1, rz1, value, level + 1, cc, cr);
                }
            }
        }

        Dirty = true;
    }

    // the four children of a cell in the order the game visits them: (2c, 2r), (2c, 2r+1), (2c+1, 2r), (2c+1, 2r+1)
    static IEnumerable<(int Col, int Row)> Children(int col, int row)
    {
        yield return (col * 2, row * 2);
        yield return (col * 2, row * 2 + 1);
        yield return (col * 2 + 1, row * 2);
        yield return (col * 2 + 1, row * 2 + 1);
    }

    /// <summary>
    /// The leaf cell that holds the grid point (0x40c5e0): the point is put back into the grid, then the tree is
    /// descended from the root, taking at each split cell the child on the side of the cell's center the point lies.
    /// </summary>
    public PathCell Lookup(float x, float z)
    {
        if (x < 0)
            x = 0;
        if (ExtentX <= x)
            x = ExtentX - Epsilon;
        if (z < 0)
            z = 0;
        if (ExtentZ <= z)
            z = ExtentZ - Epsilon;
        int level = 0,
            col = 0,
            row = 0;
        while (_levels[level].Split[row * _levels[level].Cols + col])
        {
            var size = _levels[level].Size;
            var east = col * size + size * 0.5f <= x;
            var north = row * size + size * 0.5f <= z;
            level++;
            col = Math.Min(col * 2 + (east ? 1 : 0), _levels[level].Cols - 1);
            row = Math.Min(row * 2 + (north ? 1 : 0), _levels[level].Rows - 1);
        }

        return new PathCell(level, col, row);
    }

    /// <summary>
    /// Rebuilds the region graph (0x4093b0 and the head of 0x40a310): the nodes are cleared, a split cell whose four
    /// children are leaves of one value becomes a leaf, a node is made at the center of every leaf, and the leaves that
    /// share a border segment are linked with the costs of 0x40e320. Clears <see cref="Dirty"/>.
    /// </summary>
    public void Rebuild()
    {
        foreach (var lv in _levels)
        {
            Array.Clear(lv.Node);
        }

        Merge(0, 0, 0, out _);
        var lists = Lists();
        if (_levels[0].Cols > 0 && _levels[0].Rows > 0)
        {
            Build(0, 0, 0, lists);
        }

        Dirty = false;
    }

    // 0x40d280: a split cell whose four children are leaves of equal value becomes a leaf with that value. Returns whether
    // the cell is still split. stand-in: a split cell with a child beyond the grid keeps its children (the game compares
    // the value an absent child leaves uninitialized).
    bool Merge(int level, int col, int row, out int value)
    {
        value = 0;
        if (!InRange(level, col, row))
            return false;
        var lv = _levels[level];
        var at = row * lv.Cols + col;
        if (!lv.Split[at])
        {
            value = lv.Value[at];

            return false;
        }

        var absent = false;
        var split = false;
        var values = new int[4];
        var i = 0;
        foreach (var (cc, cr) in Children(col, row))
        {
            // the game visits (2c, 2r), (2c+1, 2r), (2c, 2r+1), (2c+1, 2r+1); the order does not change the result
            absent |= !InRange(level + 1, cc, cr);
            split |= Merge(level + 1, cc, cr, out values[i]);
            i++;
        }

        if (!split && !absent && values[0] == values[1] && values[1] == values[2] && values[2] == values[3])
        {
            lv.Split[at] = false;
            lv.Value[at] = values[0];
            value = values[0];

            return false;
        }

        value = lv.Value[at];

        return true;
    }

    // The four border lists of a cell group in the order the game keeps them: lower Z, upper Z, upper X, lower X.
    static List<Leaf>[] Lists() => [new(), new(), new(), new()];

    // 0x40dd70 and 0x40d450: a leaf gets its node and puts itself on every border list; a split cell builds its four
    // children, passes up the border lists of the outer sides, and links the children's facing sides.
    void Build(int level, int col, int row, List<Leaf>[] borders)
    {
        var lv = _levels[level];
        var at = row * lv.Cols + col;
        if (!lv.Split[at])
        {
            var size = lv.Size;
            var half = size * 0.5f;
            var x = col * size + half;
            var z = row * size + half;
            x =
                x < 0 ? 0
                : ExtentX < x ? _snapX - Epsilon
                : x;
            z =
                z < 0 ? 0
                : ExtentZ < z ? _snapZ - Epsilon
                : z;
            var node = new PathNode(x, z);
            lv.Node[at] = node;
            var (x0, x1, z0, z1) = BoxOf(new PathCell(level, col, row));
            var leaf = new Leaf(x0, x1, z0, z1, lv.Value[at], node);
            foreach (var list in borders)
            {
                list.Add(leaf);
            }

            return;
        }

        var kids = new List<Leaf>[4][];
        var k = 0;
        foreach (var (cc, cr) in Children(col, row))
        {
            kids[k] = Lists();
            if (InRange(level + 1, cc, cr))
            {
                Build(level + 1, cc, cr, kids[k]);
            }

            k++;
        }

        // child 0 is (2c, 2r), 1 is (2c, 2r+1), 2 is (2c+1, 2r), 3 is (2c+1, 2r+1); lists: 0 lower Z, 1 upper Z, 2 upper X, 3 lower X
        borders[0].AddRange(kids[0][0]);
        borders[0].AddRange(kids[2][0]);
        borders[1].AddRange(kids[1][1]);
        borders[1].AddRange(kids[3][1]);
        borders[3].AddRange(kids[0][3]);
        borders[3].AddRange(kids[1][3]);
        borders[2].AddRange(kids[2][2]);
        borders[2].AddRange(kids[3][2]);
        Sweep(kids[0][1], kids[1][0], alongZ: false);
        Sweep(kids[0][2], kids[2][3], alongZ: true);
        Sweep(kids[3][0], kids[2][1], alongZ: false);
        Sweep(kids[3][3], kids[1][2], alongZ: true);
    }

    // The walk over two facing border lists that links the leaves whose intervals overlap (0x40d450's four loops, 0x40dfd0).
    // It alternates between the lists as the interval ends pass each other; the overlap test is not symmetric (a leaf that
    // lies strictly inside the other one's interval links only when it is the second of the pair).
    void Sweep(List<Leaf> first, List<Leaf> second, bool alongZ)
    {
        var a = first;
        var b = second;
        int i = 0,
            j = 0;
        if (a.Count == 0)
            return;
        while (j < b.Count)
        {
            var x = a[i];
            Leaf y;
            do
            {
                y = b[j];
                if (Overlaps(x, y, alongZ))
                {
                    Link(x, y);
                }
            } while (End(y, alongZ) - Epsilon < End(x, alongZ) && ++j < b.Count);

            var more = j < b.Count;
            (a, b) = (b, a);
            var next = i + 1;
            i = j;
            j = next;
            if (!more)
                break;
        }
    }

    static float End(Leaf l, bool alongZ) => alongZ ? l.Z1 : l.X1;

    static bool Overlaps(Leaf x, Leaf y, bool alongZ)
    {
        var (x0, x1, y0, y1) = alongZ ? (x.Z0, x.Z1, y.Z0, y.Z1) : (x.X0, x.X1, y.X0, y.X1);

        return (x0 <= Epsilon + y0 && y0 - Epsilon < x1) || (x0 < y1 + Epsilon && y1 - Epsilon <= x1);
    }

    // 0x40e320: the edges between two leaves that share a border. Both passable: both ways. One impassable: only the edge
    // out of the impassable one. Both impassable: both ways at the default value times 10000. The cost is the values
    // blended by the leaves' widths times the distance of their centers.
    void Link(Leaf a, Leaf b)
    {
        var hA = (a.X1 - a.X0) * 0.5f;
        var w = hA / ((b.X1 - b.X0) * 0.5f + hA);
        var dz = (a.Z0 + a.Z1) * 0.5f - (b.Z0 + b.Z1) * 0.5f;
        var dx = (a.X0 + a.X1) * 0.5f - (b.X0 + b.X1) * 0.5f;
        var cost = (float)(
            ((float)a.Value * w + (1f - w) * (float)b.Value) * Math.Sqrt((double)dz * dz + (double)dx * dx)
        );
        if (a.Value == _impassable)
        {
            if (b.Value != _impassable)
            {
                a.Node.Link(b.Node, cost);

                return;
            }

            cost = _default * 10000f;
            a.Node.Link(b.Node, cost);
        }
        else
        {
            if (b.Value == _impassable)
            {
                b.Node.Link(a.Node, cost);

                return;
            }

            a.Node.Link(b.Node, cost);
        }

        b.Node.Link(a.Node, cost);
    }
}
