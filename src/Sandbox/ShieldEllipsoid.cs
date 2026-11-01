using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using OpenArmada.Assets;
using OpenArmada.Formats;

namespace OpenArmada.Sandbox;

/// <summary>
/// The shield ellipsoid of a unit class, in the model's own SOD coordinates: axis-aligned, around the centre of the
/// model's box (0x46b8f0). The shield hit effect is scaled to it and sits at its centre (0x516540).
/// </summary>
/// <param name="Center">The centre of the model's box, the ellipsoid's centre (class +0x38 position).</param>
/// <param name="Radii">The ellipsoid's radii along the model's X, Y and Z axes (class +0x38 diagonal).</param>
/// <param name="HitCenter">
/// The centre of the ellipsoid the shield hit test uses (class +0x08): the game rescales the radii after it placed the
/// centre and leaves the test matrix's offset unscaled, so the test ellipsoid sits at <see cref="Center"/> times the
/// rescale factor.
/// </param>
public sealed record ShieldEllipsoid(Vector3 Center, Vector3 Radii, Vector3 HitCenter)
{
    /// <summary>The <c>shieldPad</c> a unit class has when its ODF sets none: the class prototype's 0.5 (0x46a39d).</summary>
    public const float DefaultShieldPad = 0.5f;

    // √3 (0x5cd0a4): the box's half extents times this make the ellipsoid through the box's corners.
    static readonly float Sqrt3 = MathF.Sqrt(3);

    /// <summary>
    /// The ellipsoid of <paramref name="model"/> (0x46b8f0). The box is the per-axis minimum and maximum of every mesh
    /// vertex and every sprite node's corners, through the node transforms below the root, without the root's own
    /// (0x5967f0, run once at model load by 0x580040). The first radii are the half extents times √3, around the box's
    /// centre. Then the radii are scaled so the mesh vertex farthest out, through the transforms including the root's
    /// own (0x46bae0), lies on the ellipsoid, and the longest radius grows by <paramref name="shieldPad"/> with the other
    /// two in proportion. A model without mesh vertices keeps the first radii, without the pad. Null for a model whose
    /// box is flat on an axis, which the game divides by zero.
    /// </summary>
    /// <param name="model">The unit's model.</param>
    /// <param name="spriteNodes">The <c>@sprite_node</c> lines of <c>sprites.spr</c> and its includes, in reading order.</param>
    /// <param name="shieldPad">The class's <c>shieldPad</c>.</param>
    public static ShieldEllipsoid? Of(Sod model, IReadOnlyList<SpriteNodeLine> spriteNodes, float shieldPad)
    {
        var root = model.Nodes.FirstOrDefault(n => n.Parent.Length == 0);
        var box = BoxPoints(model, spriteNodes);
        if (root == null || box.Count == 0)
            return null;
        var min = box.Aggregate(Vector3.Min);
        var max = box.Aggregate(Vector3.Max);
        var center = (min + max) * 0.5f;
        var first = (max - min) * 0.5f * Sqrt3;
        if (first.X <= 0 || first.Y <= 0 || first.Z <= 0)
            return null;

        // the farthest mesh vertex in the test ellipsoid's space, p / first − center / first (0x46bae0)
        var farthest = 0f;
        Walk(
            model,
            root,
            root.Transform,
            new HashSet<Sod.Node>(),
            (node, m) =>
            {
                foreach (var v in node.Mesh?.Vertices ?? [])
                {
                    farthest = Math.Max(farthest, ((Apply(m, v) - center) / first).LengthSquared());
                }
            }
        );
        if (farthest <= 0)
            return new ShieldEllipsoid(center, first, center);
        var fit = first * MathF.Sqrt(farthest);
        var pad = shieldPad / Math.Max(fit.X, Math.Max(fit.Y, fit.Z)) + 1;
        var radii = fit * pad;

        return new ShieldEllipsoid(center, radii, center * radii / first);
    }

    /// <summary>
    /// The game's shield test of the segment from <paramref name="from"/> to <paramref name="to"/>, both in the model's
    /// SOD coordinates (0x4ee860): in the test ellipsoid's unit-sphere space, the first point where the segment enters
    /// the unit sphere. Null when the segment misses it, starts inside it, or ends before it, and for a segment of no
    /// length. The point returned is the contact direction: a unit vector in the unit-sphere space, along the model's
    /// axes.
    /// </summary>
    public Vector3? Hit(Vector3 from, Vector3 to)
    {
        var a = (from - HitCenter) / Radii;
        var d = (to - HitCenter) / Radii - a;
        var length = d.Length();
        if (length <= 0)
            return null;
        var direction = d / length;
        var b = Vector3.Dot(-a, direction);
        var discriminant = 1 - (a.LengthSquared() - b * b);
        if (discriminant < 0)
            return null;
        var t = b - MathF.Sqrt(discriminant);
        if (t < 0 || t > length)
            return null;

        return a + direction * t;
    }

    /// <summary>
    /// The point of the ellipsoid, in the model's SOD coordinates, of a contact direction from <see cref="Hit"/>: the
    /// direction carried through the ellipsoid's own transform (class +0x38, 0x4eec49), so around <see cref="Center"/>.
    /// </summary>
    public Vector3 Surface(Vector3 direction) => direction * Radii + Center;

    /// <summary>
    /// The radius of a model's bounding sphere, which the game computes once at load (0x5520f0, model +0x24): around the
    /// centre of the box of <see cref="Of"/>, out to the farthest of the same points. A shield effect divides the
    /// ellipsoid's radii by its geometry's radius (0x516540, through node +0x60 set by 0x58d190). 0 for a model
    /// without points.
    /// </summary>
    public static float BoundingRadius(Sod model, IReadOnlyList<SpriteNodeLine> spriteNodes)
    {
        var points = BoxPoints(model, spriteNodes);
        if (points.Count < 2)
            return 0;
        var center = (points.Aggregate(Vector3.Min) + points.Aggregate(Vector3.Max)) * 0.5f;

        return MathF.Sqrt(points.Max(p => (p - center).LengthSquared()));
    }

    /// <summary>
    /// The lower and upper corner of the box around a model's points (the box <see cref="BoundingRadius"/> centres), in
    /// the model's SOD coordinates; null for a model without points.
    /// </summary>
    public static (Vector3 Min, Vector3 Max)? ModelBox(Sod model, IReadOnlyList<SpriteNodeLine> spriteNodes)
    {
        var points = BoxPoints(model, spriteNodes);

        return points.Count < 1 ? null : (points.Aggregate(Vector3.Min), points.Aggregate(Vector3.Max));
    }

    // Every point the model load collects (0x5967f0): the root takes no transform of its own.
    static List<Vector3> BoxPoints(Sod model, IReadOnlyList<SpriteNodeLine> spriteNodes)
    {
        var points = new List<Vector3>();
        if (model.Nodes.FirstOrDefault(n => n.Parent.Length == 0) is { } root)
        {
            Walk(
                model,
                root,
                Identity,
                new HashSet<Sod.Node>(),
                (node, m) => points.AddRange(Points(node, m, spriteNodes))
            );
        }

        return points;
    }

    /// <summary>
    /// The <c>@sprite_node</c> lines of <c>sprites.spr</c> and its includes in reading order, the order the game
    /// registers them in (sprites.spr read at 0x440266, before any unit model loads); none when the table is missing.
    /// </summary>
    public static IReadOnlyList<SpriteNodeLine> SpriteNodeLines(GameFileSystem files) =>
        new SpriteTableKind()
            .Loaded(files)
            .FirstOrDefault(t => t.Name.Equals("sprites.spr", StringComparison.OrdinalIgnoreCase))
            ?.SpriteNodes
        ?? [];

    static readonly Sod.Matrix34 Identity = new()
    {
        Right = Vector3.UnitX,
        Up = Vector3.UnitY,
        Front = Vector3.UnitZ,
    };

    // Visits the node and every node below it with the transform it is drawn by: `m` for the node, each child's own
    // transform composed with its parent's below it (0x596b00). A node reached twice is visited once.
    static void Walk(
        Sod model,
        Sod.Node node,
        Sod.Matrix34 m,
        HashSet<Sod.Node> seen,
        Action<Sod.Node, Sod.Matrix34> visit
    )
    {
        if (!seen.Add(node))
            return;
        visit(node, m);
        foreach (var child in model.ChildrenOf(node))
        {
            Walk(model, child, Compose(child.Transform, m), seen, visit);
        }
    }

    // The points a node adds to the box: a mesh its vertices (0x590760), a sprite node its four corners (0x599000).
    static IEnumerable<Vector3> Points(Sod.Node node, Sod.Matrix34 m, IReadOnlyList<SpriteNodeLine> spriteNodes)
    {
        if (node.Mesh != null)
            return node.Mesh.Vertices.Select(v => Apply(m, v));
        if (node.Type == Sod.NodeType.Sprite && SpriteNodeOf(node.Name, spriteNodes) is { } line)
            return Corners(line, m);

        return [];
    }

    /// <summary>
    /// The <c>@sprite_node</c> line a sprite node of the model takes its size from (0x598f90): the first line, in
    /// reading order, whose name is the node's name up to its first underscore, ignoring case. Null when none is; such
    /// a node adds nothing to the box.
    /// </summary>
    public static SpriteNodeLine? SpriteNodeOf(string node, IReadOnlyList<SpriteNodeLine> spriteNodes)
    {
        var key = node.Split('_', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";

        return spriteNodes.FirstOrDefault(l => l.Node.Equals(key, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The four corners a sprite node adds to the box (0x599000): a billboard at the node's position plus and minus its
    /// full width along the model's X and its full height along Y, unturned; any other sprite the corners of a quad
    /// half its width and height in the node's own XY plane (0x599380), through the node's transform.
    /// </summary>
    public static IEnumerable<Vector3> Corners(SpriteNodeLine line, Sod.Matrix34 m)
    {
        float w = line.Width,
            h = line.Height;
        if (line.Billboard)
        {
            var t = m.Position;

            return
            [
                t + new Vector3(-w, -h, 0),
                t + new Vector3(w, -h, 0),
                t + new Vector3(w, h, 0),
                t + new Vector3(-w, h, 0),
            ];
        }

        float a = w * 0.5f,
            b = h * 0.5f;

        return new[]
        {
            new Vector3(a, -b, 0),
            new Vector3(a, b, 0),
            new Vector3(-a, b, 0),
            new Vector3(-a, -b, 0),
        }.Select(v => Apply(m, v));
    }

    static Vector3 Apply(Sod.Matrix34 m, Vector3 v) => v.X * m.Right + v.Y * m.Up + v.Z * m.Front + m.Position;

    // The child's transform followed by the parent's.
    static Sod.Matrix34 Compose(Sod.Matrix34 child, Sod.Matrix34 parent) =>
        new()
        {
            Right = Apply(parent, child.Right) - parent.Position,
            Up = Apply(parent, child.Up) - parent.Position,
            Front = Apply(parent, child.Front) - parent.Position,
            Position = Apply(parent, child.Position),
        };
}
