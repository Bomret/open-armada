using System.Collections.Generic;
using System.Linq;
using Godot;
using OpenArmada.Rendering;

namespace OpenArmada.Viewer;

/// <summary>
/// The visible meshes of a built model, each with its transform in the root's own frame (the root's transform
/// left out). A hidden node hides its whole subtree. Sprite-node quads are no part of the model's geometry; they are
/// listed apart (<see cref="SpriteNodesOf"/>).
/// </summary>
public static class VisibleMeshes
{
    /// <summary>The visible meshes under <c>root</c>, <c>root</c> included, in tree order, without sprite-node quads.</summary>
    public static List<(Mesh Mesh, Transform3D Transform)> Of(Node3D root) =>
        Placed(root).Where(p => p.Mesh is not SpriteNodeQuad).Select(p => (p.Mesh.Mesh, p.Transform)).ToList();

    /// <summary>The visible sprite-node quads under <c>root</c>, each with its transform in the root's own frame, by the same walk.</summary>
    public static List<(SpriteNodeQuad Quad, Transform3D Transform)> SpriteNodesOf(Node3D root) =>
        Placed(root).Where(p => p.Mesh is SpriteNodeQuad).Select(p => ((SpriteNodeQuad)p.Mesh, p.Transform)).ToList();

    static List<(MeshInstance3D Mesh, Transform3D Transform)> Placed(Node3D root)
    {
        var found = new List<(MeshInstance3D, Transform3D)>();
        void Walk(Node3D node, Transform3D transform)
        {
            if (!node.Visible)
                return;
            if (node != root)
            {
                transform *= node.Transform;
            }

            if (node is MeshInstance3D m && m.Mesh != null)
            {
                found.Add((m, transform));
            }

            foreach (var child in node.GetChildren().OfType<Node3D>())
            {
                Walk(child, transform);
            }
        }

        Walk(root, Transform3D.Identity);

        return found;
    }

    /// <summary>The box around the meshes, each placed by <c>frame</c> times its own transform; null when there are none.</summary>
    public static Aabb? Extent(IEnumerable<(Mesh Mesh, Transform3D Transform)> meshes, Transform3D frame)
    {
        Aabb? box = null;
        foreach (var (mesh, t) in meshes)
        {
            var placed = frame * t * mesh.GetAabb();
            box = box?.Merge(placed) ?? placed;
        }

        return box;
    }
}
