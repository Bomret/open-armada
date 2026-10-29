using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using OpenArmada.Formats;

namespace OpenArmada.Sandbox;

/// <summary>
/// The live debris pieces of a combat world and the game's rule for making and moving them (shatter 0x58cef0, walk
/// 0x57cee0, faces 0x57cf90, piece 0x57d270, update 0x57cd90). The draws come from the world's random source in the
/// game's order. A piece is no unit: it takes no part in the fight.
/// </summary>
sealed class DebrisField(DebrisLimits limits)
{
    // A direction shorter than this is drawn again (0x5ccf20).
    const float LeastDirection = 0.1f;

    // The corners of a face in the drawing order (the file's winding reversed).
    static readonly int[] Drawn = [0, 2, 1];

    readonly List<DebrisPiece> _pieces = [];

    /// <summary>The live pieces, the oldest first.</summary>
    public IReadOnlyList<DebrisPiece> Pieces => _pieces;

    /// <summary>
    /// Shatters the unit's model: each mesh that shows at the unit's end has a density accumulator that starts at 0, and
    /// each face adds the density and spawns a piece from the face while the accumulator is above 1.0 (strict), taking 1.0
    /// away per piece. A mesh shows unless it lies under the model's <c>damage</c> group at a node
    /// <see cref="DamageNodes.ShowingNodes"/> does not show, or under a LOD control node at a level other than the one
    /// <see cref="Sod.ShownLodLevel"/> shows (inferred: inactive LOD levels are hidden). The meshes are walked from the
    /// model's root, a mesh before its children. A cap, a density, or a lifetime of 0 or less leaves no pieces.
    /// </summary>
    /// <remarks>
    /// stand-in: the game's node flags (bit 0 skips a node's own mesh, bit 1 its children) are taken to be the hidden
    /// damage-group state, which is inferred; the engine's shown LOD level is taken to be the level with the most
    /// triangle corners, the first on a tie, and the other levels are taken to be hidden (a LOD node without children
    /// shows none), which is inferred and decides the dump's debris count; the game's
    /// check of the cap is "above the cap" before each face, which leaves one piece over it after a face, where the
    /// acceptance bounds the live count by the cap, so a shatter leaves the newest pieces within the cap, oldest removed
    /// first, as a removal before each spawn that would pass it would (removed in batches, not one per spawn).
    /// </remarks>
    public void Shatter(PlacedObject unit, Sod model, UnitCombatState state, ShipPose pose, Func<double> random)
    {
        if (!limits.AllowsDebris)
            return;
        var showing = DamageNodes.ShowingNodes(model, state).ToHashSet();
        var damage = model.Nodes.FirstOrDefault(n => n.Name.Equals("damage", StringComparison.OrdinalIgnoreCase));
        var hidden =
            damage == null
                ? new HashSet<Sod.Node>()
                : model.Descendants(damage).Where(n => !showing.Contains(n)).ToHashSet();
        foreach (var lod in model.Nodes.Where(n => n.Type == Sod.NodeType.LodControl))
        {
            var shown = model.ShownLodLevel(lod);
            foreach (var level in model.ChildrenOf(lod).Where(l => l != shown))
            {
                hidden.Add(level);
                hidden.UnionWith(model.Descendants(level));
            }
        }

        var (scale, turned) = HardpointGeometry.Placement(unit, pose);
        // the unit's matrix: its axes carry the scale and the turn (Godot's frame)
        Vector3 Unit(Vector3 axis) => Vector3.Transform(axis, turned);
        var unitX = Unit(Vector3.UnitX * scale.X);
        var unitY = Unit(Vector3.UnitY * scale.Y);
        var unitZ = Unit(Vector3.UnitZ * scale.Z);
        Vector3 ByUnit(Vector3 v) => unitX * v.X + unitY * v.Y + unitZ * v.Z;

        // the oldest pieces over the cap go in batches, at most every cap spawns and once at the end, which leaves the
        // pieces a removal before each spawn would: the newest within the cap, in order
        try
        {
            foreach (var node in Walk(model).Where(n => n.Mesh != null && !hidden.Contains(n)))
            {
                // the mesh node's own matrix only: the game ignores its parents' (0x57d270)
                var m = node.Transform;
                var nodeX = new Vector3(m.Right.X, m.Right.Y, -m.Right.Z);
                var nodeY = new Vector3(m.Up.X, m.Up.Y, -m.Up.Z);
                var nodeZ = new Vector3(-m.Front.X, -m.Front.Y, m.Front.Z);
                var nodeAt = new Vector3(m.Position.X, m.Position.Y, -m.Position.Z);
                Vector3 ByNode(Vector3 v) => nodeX * v.X + nodeY * v.Y + nodeZ * v.Z;

                var mesh = node.Mesh!;
                var accumulator = 0f;
                var face = 0;
                for (var g = 0; g < mesh.Groups.Count; g++)
                {
                    var corners = mesh.Groups[g].Corners;
                    for (var i = 0; i + 2 < corners.Length; i += 3)
                    {
                        // a face whose indices pass the mesh's data is no face (reported by Sod.Problems)
                        if (
                            corners
                                .Skip(i)
                                .Take(3)
                                .Any(c => c.Vertex >= mesh.Vertices.Length || c.TexCoord >= mesh.TexCoords.Length)
                        )
                            continue;
                        accumulator += limits.Density;
                        while (accumulator > 1f)
                        {
                            accumulator -= 1f;
                            if (_pieces.Count >= 2 * limits.MaxChunks)
                            {
                                DropOldest();
                            }

                            var p = Enumerable.Range(0, 3).Select(c => mesh.Vertices[corners[i + c].Vertex]).ToArray();
                            var centroid = (p[0] + p[1] + p[2]) / 3;
                            // the file's Z mirrors into the world's, which reverses the winding
                            Vector3 Mirror(Vector3 v) => new(v.X, v.Y, -v.Z);
                            var centroidAt = Mirror(centroid);
                            var (life, velocity, tumble) = Draws(random);
                            _pieces.Add(
                                new DebrisPiece
                                {
                                    Unit = unit,
                                    Node = node,
                                    Group = g,
                                    Face = face,
                                    Vertices = Drawn.Select(c => Mirror(p[c] - centroid)).ToArray(),
                                    TexCoords = Drawn.Select(c => mesh.TexCoords[corners[i + c].TexCoord]).ToArray(),
                                    Position = pose.Position + ByUnit(nodeAt + ByNode(centroidAt)),
                                    AxisX = ByUnit(nodeX),
                                    AxisY = ByUnit(nodeY),
                                    AxisZ = ByUnit(nodeZ),
                                    Velocity = velocity,
                                    AngularVelocity = tumble,
                                    Life = life,
                                }
                            );
                        }

                        face++;
                    }
                }
            }
        }
        finally
        {
            DropOldest();
        }
    }

    // Removes the oldest pieces over the cap.
    void DropOldest()
    {
        if (_pieces.Count > limits.MaxChunks)
        {
            _pieces.RemoveRange(0, _pieces.Count - limits.MaxChunks);
        }
    }

    // The model's nodes from its roots, each node before its children (0x57cee0), the children in model order.
    static IEnumerable<Sod.Node> Walk(Sod model)
    {
        var children = model.Nodes.SelectMany(model.ChildrenOf).ToHashSet();
        var pending = new Stack<Sod.Node>(model.Nodes.Where(n => !children.Contains(n)).Reverse());
        while (pending.Count > 0)
        {
            var node = pending.Pop();
            yield return node;
            foreach (var child in model.ChildrenOf(node).Reverse())
            {
                pending.Push(child);
            }
        }
    }

    // The draws of one piece in the game's order: life, direction (three signed draws, stored third, second, first, and
    // drawn again while shorter than a tenth, then normalized), speed, tumble (three signed draws, stored third, second,
    // first). A signed draw is in [-1, 1] and a scaled one in [0, the limit].
    (double Life, Vector3 Velocity, Vector3 Tumble) Draws(Func<double> random)
    {
        float Signed() => (float)(random() * 2 - 1);
        var life = random() * limits.MaxLifetime;
        Vector3 direction;
        do
        {
            var (a, b, c) = (Signed(), Signed(), Signed());
            direction = new Vector3(c, b, a);
        } while (direction.Length() < LeastDirection);

        var speed = (float)(random() * limits.MaxVelocity);
        var (x, y, z) = (Signed(), Signed(), Signed());

        return (life, Vector3.Normalize(direction) * speed, new Vector3(z, y, x) * limits.MaxAngularVelocity);
    }

    /// <summary>
    /// One tick: each piece moves by its velocity, turns about its own axes by the negative of its tumble rate, and loses
    /// the time from its life; a piece whose life is out is removed.
    /// </summary>
    public void Advance(double seconds)
    {
        var dt = (float)seconds;
        foreach (var piece in _pieces)
        {
            piece.Position += piece.Velocity * dt;
            var turn = -piece.AngularVelocity * dt;
            if (turn != Vector3.Zero)
            {
                var q = Quaternion.CreateFromAxisAngle(Vector3.Normalize(turn), turn.Length());
                Vector3 Local(Vector3 v) => piece.AxisX * v.X + piece.AxisY * v.Y + piece.AxisZ * v.Z;
                var (x, y, z) = (
                    Local(Vector3.Transform(Vector3.UnitX, q)),
                    Local(Vector3.Transform(Vector3.UnitY, q)),
                    Local(Vector3.Transform(Vector3.UnitZ, q))
                );
                (piece.AxisX, piece.AxisY, piece.AxisZ) = (x, y, z);
            }

            piece.Life -= seconds;
        }

        _pieces.RemoveAll(p => p.Life < 0);
    }
}
