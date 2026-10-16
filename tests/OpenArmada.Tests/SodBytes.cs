using System.IO;
using System.Text;

namespace OpenArmada.Tests;

// Writes SOD files in code (INV-0001): the layout of versions 1.5 to 1.8, which is all the model
// tests need. Every method returns the writer, so a file reads top to bottom.
internal sealed class SodBytes
{
    readonly MemoryStream _ms = new();
    readonly BinaryWriter _w;
    readonly float _version;

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
                _w.Write(0.5f); // ambient, diffuse, specular
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
                _w.Write(i % 4 == 0 ? 1f : 0f);
            if (n.Mesh is { } m)
                Mesh(m);
        }
        return this;
    }

    void Mesh(MeshSpec m)
    {
        if (_version >= 1.65f)
            Id(m.TextureMaterial);
        Id(m.Texture);
        _w.Write((ushort)m.Vertices);
        _w.Write((ushort)m.TexCoords);
        _w.Write((ushort)1);
        for (var i = 0; i < m.Vertices * 3; i++)
            _w.Write((float)i);
        for (var i = 0; i < m.TexCoords * 2; i++)
            _w.Write(0.5f);
        _w.Write((ushort)(m.Corners.Length / 3));
        Id(m.Lighting);
        foreach (var (v, t) in m.Corners)
        {
            _w.Write((ushort)v);
            _w.Write((ushort)t);
        }
        _w.Write((byte)1); // backface cull
        _w.Write((ushort)0);
    }

    // No channels, and the animation references (1.7 has none with an offset; 1.8 has one).
    internal byte[] End(params (byte Type, string Node, string Animation, float Offset)[] references)
    {
        _w.Write((ushort)0); // channels
        if (_version < 1.55f)
            return _ms.ToArray();
        _w.Write((ushort)references.Length);
        foreach (var r in references)
        {
            _w.Write(r.Type);
            Id(r.Node);
            Id(r.Animation);
            if (_version >= 1.75f)
                _w.Write(r.Offset);
        }
        return _ms.ToArray();
    }

    internal sealed record MeshSpec(
        string TextureMaterial,
        string Texture,
        string Lighting,
        int Vertices,
        int TexCoords,
        (int Vertex, int TexCoord)[] Corners
    );

    internal sealed record NodeSpec(string Name, string Parent, MeshSpec? Mesh = null);

    internal static MeshSpec Triangle(
        string textureMaterial = "default",
        string texture = "tex",
        string lighting = "lit"
    ) => new(textureMaterial, texture, lighting, 3, 3, new[] { (0, 0), (1, 1), (2, 2) });
}
