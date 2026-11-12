using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Formats;
using static OpenArmada.Tests.SodBytes;

namespace OpenArmada.Tests;

[TestClass]
public class SodV12AndChannelTests
{
    static SodBytes.MeshSpec Polygon(int corners, string lighting = "lit") =>
        new()
        {
            TextureMaterial = "",
            Texture = "tex",
            Lighting = lighting,
            Vertices = Math.Max(corners, 1),
            TexCoords = Math.Max(corners, 1),
            Corners = Enumerable.Range(0, corners).Select(i => (i, i)).ToArray(),
        };

    static SodBytes V12(params MeshSpec[] meshes) =>
        new SodBytes(1.2f)
            .TextureMaterials(("tex", "tex.tga", 0f, 0, 0, 0))
            .Lighting(("lit", 1f, 3))
            .Nodes(
                meshes
                    .Select(
                        (m, i) =>
                            new NodeSpec
                            {
                                Name = $"n{i}",
                                Parent = "",
                                Mesh = m,
                            }
                    )
                    .ToArray()
            );

    static Sod.Mesh OnlyMesh(Sod sod) => sod.Nodes.Single().Mesh!;

    [TestMethod]
    public void V12QuadGivesSixCorners()
    {
        var group = OnlyMesh(Sod.Read(V12(Polygon(4)).End())).Groups.Single();
        CollectionAssert.AreEqual(
            new (ushort, ushort)[] { (0, 0), (1, 1), (2, 2), (0, 0), (2, 2), (3, 3) },
            group.Corners
        );
    }

    [TestMethod]
    public void V12PentagonGivesNineCorners()
    {
        Assert.AreEqual(9, OnlyMesh(Sod.Read(V12(Polygon(5)).End())).Groups.Single().Corners.Length);
    }

    [TestMethod]
    public void V12PolygonOfFewerThanThreeCornersGivesNone()
    {
        var mesh = Polygon(2) with
        {
            Polygons =
            [
                [(0, 0), (1, 1)],
                [(0, 0)],
                [],
            ],
        };
        Assert.AreEqual(0, OnlyMesh(Sod.Read(V12(mesh).End())).Groups.Single().Corners.Length);
    }

    [TestMethod]
    public void V12MeshWithSeveralPolygonsHasOneGroupOfTheirTriangles()
    {
        var mesh = Polygon(4) with
        {
            Polygons =
            [
                [(0, 0), (1, 1), (2, 2)],
                [(0, 0), (2, 2), (3, 3), (1, 1)],
            ],
        };
        var m = OnlyMesh(Sod.Read(V12(mesh).End()));
        Assert.AreEqual(1, m.Groups.Count);
        Assert.AreEqual(3 + 6, m.Groups[0].Corners.Length);
        Assert.AreEqual("lit", m.Groups[0].Material);
    }

    [TestMethod]
    public void V12LightingModelReadsBackOneBelowTheStoredByte()
    {
        var sod = Sod.Read(V12(Polygon(3)).End());
        var m = sod.LightingMaterials.Single();
        Assert.AreEqual((byte)3, m.StoredModel);
        Assert.AreEqual((byte)2, m.Model);
    }

    [TestMethod]
    public void V12FileHasNoChannelsAndNoReferences()
    {
        var sod = Sod.Read(V12(Polygon(3)).End());
        Assert.AreEqual(0, sod.Channels.Count);
        Assert.AreEqual(0, sod.References.Count);
    }

    [TestMethod]
    public void CutOffV12FileIsInvalidData()
    {
        var bytes = V12(Polygon(4)).End();
        Assert.ThrowsExactly<InvalidDataException>(() => Sod.Read(bytes[..^3]));
    }

    [TestMethod]
    public void V12FileWithTrailingBytesIsInvalidData()
    {
        var bytes = V12(Polygon(4)).End().Append((byte)0).ToArray();
        Assert.ThrowsExactly<InvalidDataException>(() => Sod.Read(bytes));
    }

    static float[] Key(float seed) => Enumerable.Range(0, 12).Select(i => seed + i).ToArray();

    static float[] Flat(Sod.Matrix34 m) =>
        [
            m.Right.X,
            m.Right.Y,
            m.Right.Z,
            m.Up.X,
            m.Up.Y,
            m.Up.Z,
            m.Front.X,
            m.Front.Y,
            m.Front.Z,
            m.Position.X,
            m.Position.Y,
            m.Position.Z,
        ];

    static readonly ChannelSpec[] Channels =
    [
        new()
        {
            Node = "a",
            Period = 2.5f,
            Keys = [Key(0), Key(100), Key(200)],
        },
        new() { Node = "empty", Period = 7f },
        new()
        {
            Node = "b",
            Period = 0.5f,
            Keys = [Key(50)],
        },
    ];

    [TestMethod]
    [DataRow(1.4f)]
    [DataRow(1.5f)]
    [DataRow(1.6f)]
    [DataRow(1.7f)]
    [DataRow(1.8f)]
    public void ChannelsDecodeWithNodePeriodAndKeyframesInOrder(float version)
    {
        var file = new SodBytes(version);
        if (version < 1.65f)
            file.TextureMaterials();
        var sod = Sod.Read(file.Lighting().Nodes().Channels(Channels).End());

        Assert.AreEqual(3, sod.Channels.Count);
        for (var i = 0; i < Channels.Length; i++)
        {
            var got = sod.Channels[i];
            Assert.AreEqual(Channels[i].Node, got.Node);
            Assert.AreEqual(Channels[i].Period, got.Period);
            Assert.AreEqual(Channels[i].Keys.Length, got.Keyframes.Length);
            for (var k = 0; k < got.Keyframes.Length; k++)
            {
                CollectionAssert.AreEqual(Channels[i].Keys[k], Flat(got.Keyframes[k]));
            }
        }
    }

    [TestMethod]
    public void ReferencesFollowTheChannelsFromV15()
    {
        var sod = Sod.Read(new SodBytes(1.8f).Lighting().Nodes().Channels(Channels).End((2, "a", "walk", 0.25f)));
        var r = sod.References.Single();
        Assert.AreEqual(("a", "walk", 0.25f), (r.Node, r.Animation, r.Offset));
        Assert.AreEqual(3, sod.Channels.Count);
    }
}
