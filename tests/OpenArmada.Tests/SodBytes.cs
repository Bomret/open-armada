using System.IO;
using System.Text;

namespace OpenArmada.Tests;

// Writes SOD files in code (INV-0001): the layouts of versions 1.2 and 1.4 to 1.8. A 1.2 file has no
// channel count and no references; its meshes list polygons that carry their own corner count. Every
// method returns the writer, so a file reads top to bottom.
internal sealed class SodBytes
{
    readonly MemoryStream _ms = new();
    readonly BinaryWriter _w;
    readonly float _version;
    ChannelSpec[] _channels = [];

    internal SodBytes(float version)
    {
        _version = version;
        _w = new BinaryWriter(_ms, Encoding.Latin1);
        _w.Write(Encoding.ASCII.GetBytes("Storm3D_SW"));
        _w.Write(version);
    }

    void Id(string s)
    {
        _w.Write((ushort)s.Length);
        _w.Write(Encoding.Latin1.GetBytes(s));
    }

    // The texture material table that versions before 1.7 start with.
    internal SodBytes TextureMaterials(
        params (string Name, string Texture, float Transparency, byte Src, byte Dst, byte Filter)[] entries
    )
    {
        _w.Write((ushort)entries.Length);
        foreach (var e in entries)
        {
            Id(e.Name);
            Id(e.Texture);
            _w.Write(e.Transparency);
            _w.Write(e.Src);
            _w.Write(e.Dst);
            _w.Write(e.Filter);
        }

        return this;
    }

    internal SodBytes Lighting(params (string Name, float Power, byte Model)[] materials)
    {
        _w.Write((ushort)materials.Length);
        foreach (var m in materials)
        {
            Id(m.Name);
            for (var i = 0; i < 9; i++)
            {
                _w.Write(0.5f); // ambient, diffuse, specular
            }

            _w.Write(m.Power);
            _w.Write(m.Model);
        }

        return this;
    }

    // Every node of the file at once: a plain node or a mesh of one triangle group.
    internal SodBytes Nodes(params NodeSpec[] nodes)
    {
        _w.Write((ushort)nodes.Length);
        foreach (var n in nodes)
        {
            _w.Write((ushort)(n.Mesh == null ? 0 : 1));
            Id(n.Name);
            Id(n.Parent);
            for (var i = 0; i < 12; i++)
            {
                _w.Write(
                    i >= 9 ? n.Position[i - 9]
                    : i % 4 == 0 ? 1f
                    : 0f
                );
            }

            if (n.Mesh is { } m)
            {
                Mesh(m);
            }
        }

        return this;
    }

    // The animation channels End writes; a 1.2 file has none.
    internal SodBytes Channels(params ChannelSpec[] channels)
    {
        _channels = channels;
        return this;
    }

    // 1.2: texture name, lighting material, counts, an unknown 1.0, vertices, texture coordinates and
    // polygons (a corner count, then the corners); no cull byte.
    void MeshV12(MeshSpec m)
    {
        Id(m.Texture);
        Id(m.Lighting);
        var polygons = m.Polygons ?? [m.Corners];
        _w.Write((ushort)m.Vertices);
        _w.Write((ushort)polygons.Length);
        _w.Write((ushort)m.TexCoords);
        _w.Write(1f);
        for (var i = 0; i < m.Vertices * 3; i++)
        {
            _w.Write((float)i);
        }

        for (var i = 0; i < m.TexCoords * 2; i++)
        {
            _w.Write(0.5f);
        }

        foreach (var polygon in polygons)
        {
            _w.Write((ushort)polygon.Length);
            foreach (var (v, t) in polygon)
            {
                _w.Write((ushort)v);
                _w.Write((ushort)t);
            }
        }
    }

    void Mesh(MeshSpec m)
    {
        if (_version < 1.3f)
        {
            MeshV12(m);
            return;
        }

        if (_version >= 1.65f)
        {
            Id(m.TextureMaterial);
        }

        Id(m.Texture);
        _w.Write((ushort)m.Vertices);
        _w.Write((ushort)m.TexCoords);
        _w.Write((ushort)1);
        for (var i = 0; i < m.Vertices * 3; i++)
        {
            _w.Write((float)i);
        }

        for (var i = 0; i < m.TexCoords * 2; i++)
        {
            _w.Write(0.5f);
        }

        _w.Write((ushort)(m.Corners.Length / 3));
        Id(m.Lighting);
        foreach (var (v, t) in m.Corners)
        {
            _w.Write((ushort)v);
            _w.Write((ushort)t);
        }

        _w.Write(m.Cull);
        _w.Write((ushort)0);
    }

    // The channels (a 1.2 file has no channel count), then the animation references (1.7 has none with an
    // offset; 1.8 has one).
    internal byte[] End(params (byte Type, string Node, string Animation, float Offset)[] references)
    {
        if (_version >= 1.3f)
        {
            _w.Write((ushort)_channels.Length);
            foreach (var c in _channels)
            {
                Id(c.Node);
                _w.Write((ushort)c.Keys.Length);
                _w.Write(c.Period);
                _w.Write((ushort)0); // unused
                foreach (var key in c.Keys)
                {
                    foreach (var f in key)
                    {
                        _w.Write(f);
                    }
                }
            }
        }

        if (_version < 1.55f)
            return _ms.ToArray();
        _w.Write((ushort)references.Length);
        foreach (var r in references)
        {
            _w.Write(r.Type);
            Id(r.Node);
            Id(r.Animation);
            if (_version >= 1.75f)
            {
                _w.Write(r.Offset);
            }
        }

        return _ms.ToArray();
    }

    internal sealed record MeshSpec
    {
        public required string TextureMaterial { get; init; }
        public required string Texture { get; init; }
        public required string Lighting { get; init; }
        public required int Vertices { get; init; }
        public required int TexCoords { get; init; }
        public required (int Vertex, int TexCoord)[] Corners { get; init; }

        // The polygons of a 1.2 mesh, each with its own corner count; null makes Corners one polygon.
        public (int Vertex, int TexCoord)[][]? Polygons { get; init; } = null;

        // The stored cull byte.
        public byte Cull { get; init; } = 1;
    }

    // One animation channel: the node, the period and each keyframe's 12 floats (axes, then position).
    internal sealed record ChannelSpec
    {
        public required string Node { get; init; }
        public float Period { get; init; } = 1f;
        public float[][] Keys { get; init; } = [];
    }

    internal sealed record NodeSpec
    {
        public required string Name { get; init; }
        public required string Parent { get; init; }
        public MeshSpec? Mesh { get; init; } = null;

        // The node's position as stored: X, Y, Z.
        public float[] Position { get; init; } = [0, 0, 0];
    }

    internal static MeshSpec Triangle(
        string textureMaterial = "default",
        string texture = "tex",
        string lighting = "lit"
    ) =>
        new()
        {
            TextureMaterial = textureMaterial,
            Texture = texture,
            Lighting = lighting,
            Vertices = 3,
            TexCoords = 3,
            Corners = new[] { (0, 0), (1, 1), (2, 2) },
        };
}
