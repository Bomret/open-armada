using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using OpenArmada.Assets;
using OpenArmada.Formats;

namespace OpenArmada.Rendering;

// Turns a parsed SOD into a Godot node tree. Storm3D is left-handed (Direct3D);
// Godot is right-handed, so Z is mirrored and triangle winding reversed.
public sealed class SodBuilder
{
    readonly GameFileSystem _files;
    readonly Dictionary<string, Texture2D?> _textures = new(System.StringComparer.OrdinalIgnoreCase);

    public SodBuilder(GameFileSystem files) => _files = files;

    public Node3D Build(Sod sod)
    {
        var byName = new Dictionary<string, Node3D>();
        Node3D? root = null;
        foreach (var n in sod.Nodes)
        {
            Node3D node = n.Mesh != null ? BuildMesh(sod, n.Mesh) : new Node3D();
            node.Name = n.Name.Length > 0 ? n.Name : "node";
            node.Transform = ToGodot(n.Transform);
            byName[n.Name] = node;
            if (n.Parent.Length > 0 && byName.TryGetValue(n.Parent, out var parent))
                parent.AddChild(node);
            else if (root == null)
                root = node;
            else
                root.AddChild(node);
        }
        root ??= new Node3D();

        // Damage and borgification overlays only appear in those game states.
        foreach (var node in root.FindChildren("*", "", true, false).OfType<Node3D>())
            if (((string)node.Name).ToLowerInvariant() is "damage" or "borg")
                node.Visible = false;

        // A LOD control node holds one child per detail level; show only the most detailed.
        foreach (var lod in sod.Nodes.Where(n => n.Type == Sod.NodeType.LodControl).Select(n => byName[n.Name]))
        {
            var levels = lod.GetChildren().OfType<Node3D>().ToList();
            var best = levels.OrderByDescending(VertexCount).FirstOrDefault();
            foreach (var level in levels) level.Visible = level == best;
        }
        return root;
    }

    static int VertexCount(Node node) =>
        (node is MeshInstance3D m ? m.Mesh.GetFaces().Length : 0) + node.GetChildren().Sum(VertexCount);

    MeshInstance3D BuildMesh(Sod sod, Sod.Mesh mesh)
    {
        var textureName = mesh.Texture;
        var blend = mesh.TextureMaterial;
        // Before 1.7 the mesh names an entry of the file's texture material table.
        var entry = sod.TextureMaterials.FirstOrDefault(t => t.Name.Equals(mesh.Texture, System.StringComparison.OrdinalIgnoreCase));
        if (entry != null) textureName = entry.Texture;

        var array = new ArrayMesh();
        foreach (var group in mesh.Groups)
        {
            var st = new SurfaceTool();
            st.Begin(Mesh.PrimitiveType.Triangles);
            for (int i = 0; i < group.Corners.Length; i += 3)
                foreach (var c in new[] { 0, 2, 1 }) // reversed winding
                {
                    var (v, t) = group.Corners[i + c];
                    if (t < mesh.TexCoords.Length)
                        st.SetUV(new Vector2(mesh.TexCoords[t].X, mesh.TexCoords[t].Y));
                    var p = mesh.Vertices[v];
                    st.AddVertex(new Vector3(p.X, p.Y, -p.Z));
                }
            st.GenerateNormals();
            st.SetMaterial(BuildMaterial(sod, group.Material, textureName, blend, mesh.BackfaceCull));
            st.Commit(array);
        }
        return new MeshInstance3D { Mesh = array };
    }

    Material BuildMaterial(Sod sod, string lighting, string textureName, string blend, bool cull)
    {
        var mat = new StandardMaterial3D
        {
            AlbedoTexture = Texture(textureName),
            CullMode = cull ? BaseMaterial3D.CullModeEnum.Back : BaseMaterial3D.CullModeEnum.Disabled,
        };
        var light = sod.LightingMaterials.FirstOrDefault(m => m.Name == lighting);
        if (light?.Model == 0) mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
        switch (blend.ToLowerInvariant())
        {
            case "additive":
                mat.BlendMode = BaseMaterial3D.BlendModeEnum.Add;
                mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
                break;
            case "alphathreshold":
                mat.Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor;
                break;
            case "alpha" or "translucent":
                mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
                break;
        }
        return mat;
    }

    Texture2D? Texture(string name)
    {
        if (name.Length == 0 || name.Equals("noIcon", System.StringComparison.OrdinalIgnoreCase)) return null;
        if (_textures.TryGetValue(name, out var cached)) return cached;
        Texture2D? tex = null;
        // The file system prefers full-color textures; some ship only as 8-bit palette TGAs.
        if (_files.Find(name + ".tga") != null)
        {
            var image = new Image();
            if (image.LoadTgaFromBuffer(_files.ReadAllBytes(name + ".tga")) == Error.Ok)
            {
                image.GenerateMipmaps();
                tex = ImageTexture.CreateFromImage(image);
            }
        }
        if (tex == null) GD.Print($"texture '{name}' not in install, rendering untextured");
        return _textures[name] = tex;
    }

    static Transform3D ToGodot(Sod.Matrix34 m)
    {
        // Mirror Z: S * M * S with S = diag(1, 1, -1).
        var x = new Vector3(m.Right.X, m.Right.Y, -m.Right.Z);
        var y = new Vector3(m.Up.X, m.Up.Y, -m.Up.Z);
        var z = new Vector3(-m.Front.X, -m.Front.Y, m.Front.Z);
        return new Transform3D(new Basis(x, y, z), new Vector3(m.Position.X, m.Position.Y, -m.Position.Z));
    }
}
