using System.Collections.Generic;
using System.Linq;
using Godot;

namespace OpenArmada.Viewer;

// The visible meshes of a built model, each with its transform in the root's own frame (the root's transform
// left out). A hidden node hides its whole subtree.
public static class VisibleMeshes
{
    public static List<(Mesh Mesh, Transform3D Transform)> Of(Node3D root)
    {
        var found = new List<(Mesh, Transform3D)>();
        void Walk(Node3D node, Transform3D transform)
        {
            if (!node.Visible) return;
            if (node != root) transform *= node.Transform;
            if (node is MeshInstance3D m && m.Mesh != null) found.Add((m.Mesh, transform));
            foreach (var child in node.GetChildren().OfType<Node3D>()) Walk(child, transform);
        }
        Walk(root, Transform3D.Identity);
        return found;
    }
}
