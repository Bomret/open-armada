using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;

namespace OpenArmada.Formats;

/// <summary>
/// Storm3D Object Definition (.sod): a node hierarchy of meshes, emitters, sprites and
/// hardpoints, plus lighting materials and keyframe animations.
/// Version 1.8 follows the official Storm3D spec; older versions are decoded from the files.
/// </summary>
public sealed class Sod
{
    /// <summary>The file's version: 1.2, 1.4, 1.5, 1.6, 1.7 or 1.8.</summary>
    public required float Version { get; init; }

    /// <summary>The texture material table of a file before 1.7; empty from 1.7 on.</summary>
    public IReadOnlyList<TextureMaterial> TextureMaterials { get; init; } = [];

    /// <summary>The lighting materials in file order.</summary>
    public IReadOnlyList<LightingMaterial> LightingMaterials { get; init; } = [];

    /// <summary>The nodes in file order; a node names its parent by name.</summary>
    public IReadOnlyList<Node> Nodes { get; init; } = [];

    /// <summary>The keyframe animation channels; empty in a 1.2 file.</summary>
    public IReadOnlyList<AnimationChannel> Channels { get; init; } = [];

    /// <summary>The animation references; empty before 1.6.</summary>
    public IReadOnlyList<AnimationReference> References { get; init; } = [];

    /// <summary>
    /// Data the file holds that cannot be drawn (a face index past the mesh, an unknown parent); the
    /// reader keeps going and lists each one for the model.
    /// </summary>
    public IReadOnlyList<string> Problems { get; init; } = [];

    /// <summary>An entry of the texture material table of a file before 1.7.</summary>
    public sealed class TextureMaterial
    {
        /// <summary>
        /// The entry's name, which a mesh's <see cref="Mesh.Texture"/> names, and the texture file it uses.
        /// </summary>
        public string Name = "",
            Texture = "";

        /// <summary>
        /// Before 1.7 each entry carries its own state: a transparency float, then Direct3D source and
        /// destination blend values (1 zero, 2 one, 5 source alpha, 6 inverse source alpha) and a filter byte.
        /// </summary>
        public float Transparency;

        /// <summary>
        /// The Direct3D source and destination blend values and the filter byte, as the file stores them; see
        /// <see cref="Transparency"/>.
        /// </summary>
        public byte SrcBlend,
            DstBlend,
            Filter;
    }

    /// <summary>A lighting material: its colors, specular power and lighting model.</summary>
    public sealed class LightingMaterial
    {
        /// <summary>The material's name, which a face group's <see cref="Group.Material"/> names.</summary>
        public string Name = "";

        /// <summary>The ambient, diffuse and specular colors as RGB in X, Y and Z, as stored.</summary>
        public Vector3 Ambient,
            Diffuse,
            Specular;

        /// <summary>The specular power, as stored.</summary>
        public float SpecularPower;

        /// <summary>
        /// The lighting model: 0 constant, 1 Lambert, 2 Phong. Up to 1.4 the file stores the model plus one, so this is
        /// the stored byte minus one, and 255 when the stored byte is 0.
        /// </summary>
        public byte Model; // 0 constant, 1 Lambert, 2 Phong; 255 when an old file stores 0

        /// <summary>
        /// The lighting model byte as the file holds it, before the correction of <see cref="Model"/>.
        /// </summary>
        public byte StoredModel; // the byte as the file holds it
    }

    /// <summary>The kind of a node, as the file stores it; any other value makes <see cref="Read"/> throw.</summary>
    public enum NodeType : ushort
    {
        /// <summary>A node without data of its own after its transform.</summary>
        Null = 0,

        /// <summary>A node with a mesh.</summary>
        Mesh = 1,

        /// <summary>A sprite node; it holds no data of its own after its transform.</summary>
        Sprite = 3,

        /// <summary>A level-of-detail control node; it holds no data of its own after its transform.</summary>
        LodControl = 11,

        /// <summary>A node that names an emitter.</summary>
        Emitter = 12,
    }

    /// <summary>A node of the hierarchy.</summary>
    public sealed class Node
    {
        /// <summary>The node's kind.</summary>
        public NodeType Type;

        /// <summary>
        /// The node's name and the name of its parent; <c>Parent</c> is empty for a root node. The parent may be
        /// missing from the model, which <see cref="Problems"/> lists.
        /// </summary>
        public string Name = "",
            Parent = "";

        /// <summary>The node's transform, as stored.</summary>
        public Matrix34 Transform;

        /// <summary>The mesh of a <see cref="NodeType.Mesh"/> node; null for every other kind.</summary>
        public Mesh? Mesh;

        /// <summary>The emitter name of a <see cref="NodeType.Emitter"/> node; empty for every other kind.</summary>
        public string Emitter = "";
    }

    /// <summary>The geometry of a mesh node: vertices, texture coordinates and face groups.</summary>
    public sealed class Mesh
    {
        /// <summary>
        /// From 1.7 on, <c>TextureMaterial</c> names the texture material and <c>Texture</c> the texture; before 1.7,
        /// <c>TextureMaterial</c> is empty and <c>Texture</c> names an entry of <see cref="TextureMaterials"/>.
        /// </summary>
        public string TextureMaterial = "",
            Texture = "";

        /// <summary>The vertex positions, which <see cref="Group.Corners"/> index.</summary>
        public Vector3[] Vertices = Array.Empty<Vector3>();

        /// <summary>The texture coordinates, which <see cref="Group.Corners"/> index.</summary>
        public Vector2[] TexCoords = Array.Empty<Vector2>();

        /// <summary>The face groups; a 1.2 mesh has exactly one.</summary>
        public IReadOnlyList<Group> Groups = Array.Empty<Group>();

        /// <summary>
        /// True when the stored cull byte is not 0; always false for a 1.2 mesh, which stores no cull byte.
        /// </summary>
        public bool BackfaceCull;
    }

    /// <summary>The faces of a mesh that share one lighting material.</summary>
    public sealed class Group
    {
        /// <summary>The name of the group's lighting material; may be empty.</summary>
        public string Material = "";

        /// <summary>Three corners per face; each corner indexes Vertices and TexCoords separately.</summary>
        public (ushort Vertex, ushort TexCoord)[] Corners = Array.Empty<(ushort, ushort)>();
    }

    /// <summary>The keyframes of one node's animation.</summary>
    public sealed class AnimationChannel
    {
        /// <summary>The name of the animated node.</summary>
        public string Node = "";

        /// <summary>The channel's period, as stored.</summary>
        public float Period;

        /// <summary>The node's transform at each keyframe, in file order.</summary>
        public Matrix34[] Keyframes = Array.Empty<Matrix34>();
    }

    /// <summary>A reference from a node to an animation by name.</summary>
    public sealed class AnimationReference
    {
        /// <summary>The reference's type byte, as stored; the reader does not interpret it.</summary>
        public byte Type;

        /// <summary>
        /// The name of the node the reference applies to, and the name of the animation it refers to.
        /// </summary>
        public string Node = "",
            Animation = "";

        /// <summary>The time offset the reference stores from 1.8 on; 0 before.</summary>
        public float Offset;
    }

    /// <summary>A transform as the file stores it: three axis vectors and a position, 12 floats.</summary>
    public struct Matrix34
    {
        /// <summary>The right, up and front axes and the position, in file order.</summary>
        public Vector3 Right,
            Up,
            Front,
            Position;
    }

    static readonly float[] KnownVersions = { 1.2f, 1.4f, 1.5f, 1.6f, 1.7f, 1.8f };

    /// <summary>Reads the whole file at <c>path</c> and parses it with <see cref="Read"/>.</summary>
    /// <exception cref="InvalidDataException">
    /// when the data is not a SOD file the reader decodes; see <see cref="Read"/>.
    /// </exception>
    /// <exception cref="UnsupportedFormatException">when the file is of version 1.0.</exception>
    public static Sod Load(string path) => Read(File.ReadAllBytes(path));

    /// <summary>
    /// Parses a SOD file. Problems the model can still be drawn with go to <see cref="Problems"/>; the read does not
    /// throw for them.
    /// </summary>
    /// <exception cref="InvalidDataException">
    /// when the magic is neither "Storm3D_SW" nor "StarTrekDB", the version is not one the reader decodes, a node has
    /// an unknown type, bytes follow the last section, or the file is cut off.
    /// </exception>
    /// <exception cref="UnsupportedFormatException">when the file is of version 1.0.</exception>
    public static Sod Read(byte[] data)
    {
        try
        {
            return ReadFile(data);
        }
        catch (EndOfStreamException)
        {
            throw new InvalidDataException($"SOD is cut off ({data.Length} bytes)");
        }
    }

    static Sod ReadFile(byte[] data)
    {
        var r = new BinaryReader(new MemoryStream(data), Encoding.ASCII);
        var magic = Encoding.ASCII.GetString(r.ReadBytes(10));
        if (magic != "Storm3D_SW" && magic != "StarTrekDB")
            throw new InvalidDataException($"not a SOD file: '{magic}'");

        var version = r.ReadSingle();
        var textureMaterials = new List<TextureMaterial>();
        var lightingMaterials = new List<LightingMaterial>();
        var nodeList = new List<Node>();
        var channelList = new List<AnimationChannel>();
        var references = new List<AnimationReference>();
        // Only Wviracod.SOD uses 1.0, and nothing in the game references it.
        if (version == 1.0f)
            throw new UnsupportedFormatException($"SOD version {version} is not supported");
        // Any version outside the decoded set (including NaN and infinities) is corrupt.
        if (Array.IndexOf(KnownVersions, version) < 0)
            throw new InvalidDataException($"unrecognised SOD version {version}");

        // Versions before 1.7 start with a texture material table.
        if (version < 1.65f)
        {
            int count = r.ReadUInt16();
            for (int i = 0; i < count; i++)
            {
                textureMaterials.Add(
                    new TextureMaterial
                    {
                        Name = Id(r),
                        Texture = Id(r),
                        Transparency = r.ReadSingle(),
                        SrcBlend = r.ReadByte(),
                        DstBlend = r.ReadByte(),
                        Filter = r.ReadByte(),
                    }
                );
            }
        }

        int materials = r.ReadUInt16();
        for (int i = 0; i < materials; i++)
        {
            var material = new LightingMaterial
            {
                Name = Id(r),
                Ambient = Vec3(r),
                Diffuse = Vec3(r),
                Specular = Vec3(r),
                SpecularPower = r.ReadSingle(),
            };
            material.StoredModel = r.ReadByte();
            // Versions up to 1.4 store the model plus one (Armada.exe 0x59bd80; the installed files hold 1 to 3),
            // so a stored 0 there is no model: 255, which no lighting model matches.
            material.Model =
                version < 1.45f
                    ? (byte)(material.StoredModel == 0 ? 255 : material.StoredModel - 1)
                    : material.StoredModel;
            lightingMaterials.Add(material);
        }

        int nodes = r.ReadUInt16();
        for (int i = 0; i < nodes; i++)
        {
            var node = new Node
            {
                Type = (NodeType)r.ReadUInt16(),
                Name = Id(r),
                Parent = Id(r),
                Transform = Matrix(r),
            };
            switch (node.Type)
            {
                case NodeType.Mesh:
                    node.Mesh = ReadMesh(r, version);

                    break;
                case NodeType.Emitter:
                    node.Emitter = Id(r);

                    break;
                case NodeType.Null or NodeType.Sprite or NodeType.LodControl:
                    break;
                default:
                    throw new InvalidDataException($"unknown node type {(ushort)node.Type} at {r.BaseStream.Position}");
            }

            nodeList.Add(node);
        }

        // 1.2 files end after the nodes, 1.4 and 1.5 after the animation channels.
        int channels = version >= 1.3f ? r.ReadUInt16() : 0;
        for (int i = 0; i < channels; i++)
        {
            var ch = new AnimationChannel { Node = Id(r) };
            int keys = r.ReadUInt16();
            ch.Period = r.ReadSingle();
            r.ReadUInt16(); // unused
            ch.Keyframes = new Matrix34[keys];
            for (int k = 0; k < keys; k++)
            {
                ch.Keyframes[k] = Matrix(r);
            }

            channelList.Add(ch);
        }

        int refs = version >= 1.55f ? r.ReadUInt16() : 0;
        for (int i = 0; i < refs; i++)
        {
            references.Add(
                new AnimationReference
                {
                    Type = r.ReadByte(),
                    Node = Id(r),
                    Animation = Id(r),
                    Offset = version >= 1.75f ? r.ReadSingle() : 0f, // 1.8 added the time offset
                }
            );
        }

        if (r.BaseStream.Position != data.Length)
            throw new InvalidDataException($"{data.Length - r.BaseStream.Position} trailing bytes");

        return new Sod
        {
            Version = version,
            TextureMaterials = textureMaterials,
            LightingMaterials = lightingMaterials,
            Nodes = nodeList,
            Channels = channelList,
            References = references,
            Problems = FindProblems(nodeList),
        };
    }

    /// <summary>
    /// Whether following the parent names from a node (the first node of a name is the parent) comes
    /// back to it: a node naming itself, or a loop of nodes. Such a node is no child of any node.
    /// </summary>
    public bool InParentCycle(Node node) => InParentCycle(Nodes, node);

    Dictionary<Node, int>? _indexes;
    ILookup<Node, Node>? _children;

    /// <summary>The position of a node in <see cref="Nodes"/>, or -1 for a node the model lacks.</summary>
    public int IndexOf(Node node)
    {
        _indexes ??= Nodes.Select((n, i) => (n, i)).ToDictionary(p => p.n, p => p.i);
        return _indexes.GetValueOrDefault(node, -1);
    }

    /// <summary>
    /// The nodes hung from a node, in model order, as <c>SodBuilder</c> hangs them: a node is the child of the first
    /// node named by its parent, and a node in a parent cycle is the child of none. Built once per model.
    /// </summary>
    public IEnumerable<Node> ChildrenOf(Node node)
    {
        if (_children == null)
        {
            var first = new Dictionary<string, Node>();
            foreach (var n in Nodes)
            {
                first.TryAdd(n.Name, n);
            }

            _children = Nodes
                .Where(n => n.Parent.Length > 0 && first.ContainsKey(n.Parent) && !InParentCycle(n))
                .ToLookup(n => first[n.Parent]);
        }

        return _children[node];
    }

    /// <summary>The nodes below a node at any depth, not the node itself, each once (<see cref="ChildrenOf"/>).</summary>
    public IEnumerable<Node> Descendants(Node node)
    {
        var seen = new HashSet<Node>();
        var pending = new Stack<Node>(ChildrenOf(node));
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (!seen.Add(current))
                continue;
            yield return current;
            foreach (var child in ChildrenOf(current))
            {
                pending.Push(child);
            }
        }
    }

    static bool InParentCycle(IReadOnlyList<Node> nodes, Node node)
    {
        var seen = new HashSet<Node>();
        for (var current = node; current.Parent.Length > 0; )
        {
            var parent = nodes.FirstOrDefault(p => p.Name == current.Parent);
            if (parent == null || !seen.Add(parent))
                return false;
            if (parent == node)
                return true;
            current = parent;
        }

        return false;
    }

    static List<string> FindProblems(IReadOnlyList<Node> nodes)
    {
        var problems = new List<string>();
        var names = nodes.Select(n => n.Name).ToHashSet();
        foreach (var n in nodes)
        {
            if (n.Parent.Length > 0 && !names.Contains(n.Parent))
            {
                problems.Add($"node '{n.Name}' names the parent '{n.Parent}', which the model lacks");
            }

            if (InParentCycle(nodes, n))
            {
                problems.Add(
                    $"node '{n.Name}' is its own ancestor through its parents (a parent cycle); it hangs from the root"
                );
            }

            if (n.Mesh is not { } m)
                continue;
            foreach (var (v, t) in m.Groups.SelectMany(g => g.Corners))
            {
                if (v >= m.Vertices.Length)
                {
                    problems.Add($"node '{n.Name}': a face corner uses vertex {v}, the mesh has {m.Vertices.Length}");

                    break;
                }

                if (t >= m.TexCoords.Length)
                {
                    problems.Add(
                        $"node '{n.Name}': a face corner uses texture coordinate {t}, the mesh has {m.TexCoords.Length}"
                    );

                    break;
                }
            }
        }

        return problems;
    }

    static Mesh ReadMesh(BinaryReader r, float version)
    {
        var mesh = new Mesh();
        if (version < 1.3f)
            return ReadMeshV12(r, mesh);

        if (version >= 1.65f)
        {
            mesh.TextureMaterial = Id(r);
            mesh.Texture = Id(r);
        }
        else
        {
            mesh.Texture = Id(r); // names an entry of the texture material table
        }

        int verts = r.ReadUInt16(),
            uvs = r.ReadUInt16(),
            groups = r.ReadUInt16();
        mesh.Vertices = new Vector3[verts];
        for (int i = 0; i < verts; i++)
        {
            mesh.Vertices[i] = Vec3(r);
        }

        mesh.TexCoords = new Vector2[uvs];
        for (int i = 0; i < uvs; i++)
        {
            mesh.TexCoords[i] = new Vector2(r.ReadSingle(), r.ReadSingle());
        }

        var groupList = new List<Group>();
        for (int g = 0; g < groups; g++)
        {
            int faces = r.ReadUInt16();
            var group = new Group { Material = Id(r), Corners = new (ushort, ushort)[faces * 3] };
            for (int c = 0; c < faces * 3; c++)
            {
                group.Corners[c] = (r.ReadUInt16(), r.ReadUInt16());
            }

            groupList.Add(group);
        }

        mesh.Groups = groupList;
        mesh.BackfaceCull = r.ReadByte() != 0;
        // Count + vertex indices, meaning unknown; the spec claims this is always 0 (Zentity.SOD disagrees).
        r.ReadBytes(r.ReadUInt16() * 2);

        return mesh;
    }

    // 1.2: one lighting material per mesh, faces carry their own corner count, no cull byte.
    static Mesh ReadMeshV12(BinaryReader r, Mesh mesh)
    {
        mesh.Texture = Id(r);
        var group = new Group { Material = Id(r) };
        int verts = r.ReadUInt16(),
            faces = r.ReadUInt16(),
            uvs = r.ReadUInt16();
        r.ReadSingle(); // always 1.0 so far, meaning unknown
        mesh.Vertices = new Vector3[verts];
        for (int i = 0; i < verts; i++)
        {
            mesh.Vertices[i] = Vec3(r);
        }

        mesh.TexCoords = new Vector2[uvs];
        for (int i = 0; i < uvs; i++)
        {
            mesh.TexCoords[i] = new Vector2(r.ReadSingle(), r.ReadSingle());
        }

        var corners = new List<(ushort, ushort)>();
        for (int f = 0; f < faces; f++)
        {
            int n = r.ReadUInt16();
            var poly = new (ushort V, ushort T)[n];
            for (int c = 0; c < n; c++)
            {
                poly[c] = (r.ReadUInt16(), r.ReadUInt16());
            }

            for (int c = 1; c + 1 < n; c++) // fan-triangulate
            {
                corners.Add(poly[0]);
                corners.Add(poly[c]);
                corners.Add(poly[c + 1]);
            }
        }

        group.Corners = corners.ToArray();
        mesh.Groups = new[] { group };

        return mesh;
    }

    static string Id(BinaryReader r)
    {
        int length = r.ReadUInt16();
        var bytes = r.ReadBytes(length);
        if (bytes.Length != length)
            throw new EndOfStreamException();

        return Windows1252.Encoding.GetString(bytes);
    }

    static Vector3 Vec3(BinaryReader r) => new(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());

    static Matrix34 Matrix(BinaryReader r) =>
        new()
        {
            Right = Vec3(r),
            Up = Vec3(r),
            Front = Vec3(r),
            Position = Vec3(r),
        };
}
