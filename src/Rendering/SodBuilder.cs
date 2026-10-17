using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using OpenArmada.Assets;
using OpenArmada.Diagnostics;
using OpenArmada.Formats;
using OpenArmada.Sandbox;

namespace OpenArmada.Rendering;

/// <summary>
/// Turns a parsed SOD into a Godot node tree. Storm3D is left-handed (Direct3D);
/// Godot is right-handed, so Z is mirrored and triangle winding reversed. Surfaces follow ModelRules:
/// the texture material's blend, the lighting material's model and colors, the animation references.
/// Whatever the data holds that cannot be drawn as it says is listed in Problems, never hidden. Each sprite node the
/// install's sprite tables attach to a node is drawn under it (SpriteNodeQuad).
/// </summary>
public sealed class SodBuilder
{
    const string NoSpriteTexture = "sprite texture cannot be read, its sprite node is not drawn";

    readonly GameFileSystem _files;

    // Why a texture did not load, and the textures already logged as unavailable.
    readonly Dictionary<string, string?> _why = new(System.StringComparer.OrdinalIgnoreCase);

    // Mesh textures that failed to load (name to reason); logged after the sprite pass, see Build.
    readonly Dictionary<string, string?> _meshMisses = new(System.StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, Texture2D?> _textures = new(System.StringComparer.OrdinalIgnoreCase);
    readonly List<(ShaderMaterial Material, Surface Surface)> _team = new();
    readonly Dictionary<Sod.Node, List<ShaderMaterial>> _byNode = new();
    System.Numerics.Vector3 _teamColor = new(0.5f, 0.5f, 0.5f);

    /// <summary><c>files</c> is the install the builder reads textures and lighting materials from; it caches each texture it loads, by name.</summary>
    public SodBuilder(GameFileSystem files) => _files = files;

    /// <summary>What the last Build could not draw as the data says (ModelRules.Check), one line each.</summary>
    public IReadOnlyList<string> Problems { get; private set; } = new List<string>();

    /// <summary>
    /// Recolors the team materials of the model built last. Each Build makes its own materials, so a model
    /// built earlier keeps its color; Build(sod, color) colors a model as it is built.
    /// </summary>
    public void SetTeamColor(System.Numerics.Vector3 color)
    {
        _teamColor = color;
        foreach (var (material, surface) in _team)
        {
            SurfaceMaterial.Apply(material, surface.WithTeam(color));
        }
    }

    /// <summary>Builds the model with its team materials in <c>teamColor</c> (the last color set when null).</summary>
    public Node3D Build(Sod sod, System.Numerics.Vector3? teamColor = null)
    {
        if (teamColor != null)
        {
            _teamColor = teamColor.Value;
        }

        _team.Clear();
        _byNode.Clear();
        var analysis = ModelRules.Analyze(_files, sod);
        var nodes = new List<(Sod.Node Data, Node3D Node)>();
        foreach (var n in sod.Nodes)
        {
            Node3D node = n.Mesh != null ? BuildMesh(sod, n) : new Node3D();
            node.Name = NodeName.Of(n.Name);
            node.Transform = ToGodot(n.Transform);
            nodes.Add((n, node));
        }

        // The first node of a name is the parent; a node with no known parent, or in a parent cycle (reported
        // in Problems), hangs from the root.
        var byName = new Dictionary<string, Node3D>();
        foreach (var (data, node) in nodes)
        {
            byName.TryAdd(data.Name, node);
        }

        Node3D? root = null;
        foreach (var (data, node) in nodes)
        {
            if (data.Parent.Length > 0 && byName.TryGetValue(data.Parent, out var parent) && !sod.InParentCycle(data))
            {
                parent.AddChild(node);
            }
            else if (root == null)
            {
                root = node;
            }
            else
            {
                root.AddChild(node);
            }
        }

        root ??= new Node3D();

        // Damage and borgification overlays only appear in those game states.
        foreach (var node in root.FindChildren("*", "", true, false).OfType<Node3D>())
        {
            if (((string)node.Name).ToLowerInvariant() is "damage" or "borg")
            {
                node.Visible = false;
            }
        }

        // A LOD control node holds one child per detail level; show only the most detailed.
        foreach (var lod in sod.Nodes.Where(n => n.Type == Sod.NodeType.LodControl).Select(n => byName[n.Name]))
        {
            var levels = lod.GetChildren().OfType<Node3D>().ToList();
            var best = levels.OrderByDescending(VertexCount).FirstOrDefault();
            foreach (var level in levels)
            {
                level.Visible = level == best;
            }
        }

        var animator = new TextureAnimator();
        foreach (var (node, flipbook) in analysis.Runs)
        {
            animator.Add(_byNode[node], flipbook);
        }

        root.AddChild(animator);

        // A sprite node's gaps, and a sprite texture that cannot be read, are problems; the model still builds.
        var sprites = SpriteNodes.For(_files, sod);
        var gaps = new Gaps();
        var spriteMisses = new List<string>();
        var built = nodes.ToDictionary(n => n.Data, n => n.Node);
        foreach (var sprite in sprites.Nodes)
        {
            if (Texture(sprite.Sprite.Sprite.Texture) is { } texture)
            {
                built[sprite.Node].AddChild(SpriteNodeQuad.Create(sprite, texture));
            }
            else
            {
                var name = sprite.Sprite.Sprite.Texture;
                spriteMisses.Add(name);
                gaps.Add(NoSpriteTexture, _why.TryGetValue(name, out var why) ? $"{name} ({why})" : name, sprite.Node);
            }
        }

        Problems = analysis.Problems.Concat(sprites.Reports.Concat(gaps.Entries).Select(r => r.Text)).ToList();

        // A mesh texture that is also a sprite texture became a Problems entry, which the preview logs.
        ViewerLog.TexturesUnavailable(_meshMisses, spriteMisses);
        _meshMisses.Clear();

        return root;
    }

    static int VertexCount(Node node) =>
        (node is MeshInstance3D m ? m.Mesh.GetFaces().Length : 0) + node.GetChildren().Sum(VertexCount);

    MeshInstance3D BuildMesh(Sod sod, Sod.Node data)
    {
        var mesh = data.Mesh!;
        var look = ModelRules.Look(sod, mesh);
        // A blend the engine does not know is listed in Problems (ModelRules.Check) and drawn opaque.
        var blend = look.Blend ?? new BlendState { Mode = BlendMode.Opaque, Opacity = 1f };
        var texture = Texture(look.Texture);
        // An untextured model is logged once per texture after the sprite pass (Build).
        if (texture == null && _why.TryGetValue(look.Texture, out var reason))
        {
            _meshMisses[look.Texture] = reason;
        }

        var materials = new List<ShaderMaterial>();
        var array = new ArrayMesh();
        foreach (var group in mesh.Groups)
        {
            var st = new SurfaceTool();
            st.Begin(blend.Wireframe ? Mesh.PrimitiveType.Lines : Mesh.PrimitiveType.Triangles);
            for (int i = 0; i + 2 < group.Corners.Length; i += 3)
            {
                // A face whose indices pass the mesh's data is skipped (reported by Sod.Problems).
                if (
                    group
                        .Corners.Skip(i)
                        .Take(3)
                        .Any(c => c.Vertex >= mesh.Vertices.Length || c.TexCoord >= mesh.TexCoords.Length)
                )
                    continue;
                foreach (var c in blend.Wireframe ? new[] { 0, 1, 1, 2, 2, 0 } : new[] { 0, 2, 1 }) // reversed winding
                {
                    var (v, t) = group.Corners[i + c];
                    st.SetUV(new Vector2(mesh.TexCoords[t].X, mesh.TexCoords[t].Y));
                    var p = mesh.Vertices[v];
                    st.AddVertex(new Vector3(p.X, p.Y, -p.Z));
                }
            }

            if (!blend.Wireframe)
            {
                st.GenerateNormals();
            }

            var surface = SurfaceOf(sod, group.Material);
            // Lines carry no normals, so a wireframe shades constant whatever the lighting material says
            // (the game keeps the material's model and shades the lines with it).
            if (blend.Wireframe)
            {
                surface = surface with { Model = LightingModel.Constant };
            }

            var material = SurfaceMaterial.Create(surface, blend, texture, mesh.BackfaceCull);
            if (surface.Team)
            {
                _team.Add((material, surface));
                SurfaceMaterial.Apply(material, surface.WithTeam(_teamColor));
            }

            materials.Add(material);
            st.SetMaterial(material);
            st.Commit(array);
        }

        _byNode[data] = materials;

        return new MeshInstance3D { Mesh = array };
    }

    // A group names its lighting material, found in the model or any model the game loads. One no model
    // defines, no name at all, or of a model the engine does not know, is reported in Problems (ModelRules.Analyze) and
    // shades with a stand-in: Lambert, white diffuse. The stand-in's values are the engine's, not the game's.
    Surface SurfaceOf(Sod sod, string name)
    {
        var material = ModelRules.FindLightingMaterial(_files, sod, name);
        var surface = material == null ? null : ModelRules.Lighting(material);

        return surface
            ?? new Surface
            {
                Model = LightingModel.Lambert,
                Ambient = System.Numerics.Vector3.Zero,
                Diffuse = System.Numerics.Vector3.One,
                Specular = System.Numerics.Vector3.Zero,
                Power = 1f,
                Team = ModelRules.IsTeam(name),
            };
    }

    Texture2D? Texture(string name)
    {
        if (name.Length == 0 || name.Equals("noIcon", System.StringComparison.OrdinalIgnoreCase))
            return null;
        if (_textures.TryGetValue(name, out var cached))
            return cached;
        Texture2D? tex = null;
        // Textures come through the textures kind: full-color wins over 8-bit palette TGAs.
        string? why = null;
        try
        {
            // Base level only: a bad mip level must not leave the model untextured.
            var asset = new TextureKind().LoadBase(_files, name + ".tga");
            var image = new Image();
            if (image.LoadTgaFromBuffer(asset.Data) == Error.Ok)
            {
                image.GenerateMipmaps();
                tex = ImageTexture.CreateFromImage(image);
            }
            else
            {
                why = "not a readable TGA";
            }
        }
        catch (System.Exception e)
            when (e
                    is System.IO.IOException
                        or System.UnauthorizedAccessException
                        or UnsupportedFormatException
                        or System.IO.InvalidDataException
            )
        {
            why = e.Message;
        }

        if (tex == null)
        {
            _why[name] = why;
        }

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
