using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;

namespace OpenArmada.Formats;

// Storm3D Object Definition (.sod): a node hierarchy of meshes, emitters, sprites and
// hardpoints, plus lighting materials and keyframe animations.
// Version 1.8 follows the official Storm3D spec; older versions are decoded from the files.
public sealed class Sod
{
    public required float Version { get; init; }
    public IReadOnlyList<TextureMaterial> TextureMaterials { get; init; } = [];
    public IReadOnlyList<LightingMaterial> LightingMaterials { get; init; } = [];
    public IReadOnlyList<Node> Nodes { get; init; } = [];
    public IReadOnlyList<AnimationChannel> Channels { get; init; } = [];
    public IReadOnlyList<AnimationReference> References { get; init; } = [];

    // Data the file holds that cannot be drawn (a face index past the mesh, an unknown parent); the
    // reader keeps going and lists each one for the model.
    public IReadOnlyList<string> Problems { get; init; } = [];

    public sealed class TextureMaterial
    {
        public string Name = "",
            Texture = "";

        // Before 1.7 each entry carries its own state: a transparency float, then Direct3D source and
        // destination blend values (1 zero, 2 one, 5 source alpha, 6 inverse source alpha) and a filter byte.
        public float Transparency;
        public byte SrcBlend,
            DstBlend,
            Filter;
    }

    public sealed class LightingMaterial
    {
        public string Name = "";
        public Vector3 Ambient,
            Diffuse,
            Specular;
        public float SpecularPower;
        public byte Model; // 0 constant, 1 Lambert, 2 Phong; 255 when an old file stores 0
        public byte StoredModel; // the byte as the file holds it
    }

    public enum NodeType : ushort
    {
        Null = 0,
        Mesh = 1,
        Sprite = 3,
        LodControl = 11,
        Emitter = 12,
    }

    public sealed class Node
    {
        public NodeType Type;
        public string Name = "",
            Parent = "";
        public Matrix34 Transform;
        public Mesh? Mesh;
        public string Emitter = "";
    }

    public sealed class Mesh
    {
        public string TextureMaterial = "",
            Texture = "";
        public Vector3[] Vertices = Array.Empty<Vector3>();
        public Vector2[] TexCoords = Array.Empty<Vector2>();
        public IReadOnlyList<Group> Groups = Array.Empty<Group>();
        public bool BackfaceCull;
    }

    public sealed class Group
    {
        public string Material = "";

        // Three corners per face; each corner indexes Vertices and TexCoords separately.
        public (ushort Vertex, ushort TexCoord)[] Corners = Array.Empty<(ushort, ushort)>();
    }

    public sealed class AnimationChannel
    {
        public string Node = "";
        public float Period;
        public Matrix34[] Keyframes = Array.Empty<Matrix34>();
    }

    public sealed class AnimationReference
    {
        public byte Type;
        public string Node = "",
            Animation = "";
        public float Offset;
    }

    public struct Matrix34
    {
        public Vector3 Right,
            Up,
            Front,
            Position;
    }

    static readonly float[] KnownVersions = { 1.2f, 1.4f, 1.5f, 1.6f, 1.7f, 1.8f };

    public static Sod Load(string path) => Read(File.ReadAllBytes(path));

    public static Sod Read(byte[] data)
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

    // Whether following the parent names from a node (the first node of a name is the parent) comes
    // back to it: a node naming itself, or a loop of nodes. Such a node is no child of any node.
    public bool InParentCycle(Node node) => InParentCycle(Nodes, node);

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

    static string Id(BinaryReader r) => Windows1252.Encoding.GetString(r.ReadBytes(r.ReadUInt16()));

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
