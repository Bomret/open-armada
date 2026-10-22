using System;
using System.Linq;
using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Formats;
using OpenArmada.Sandbox;

namespace OpenArmada.Tests;

[TestClass]
public class ShieldEllipsoidTests
{
    static readonly Sod.Matrix34 Identity = At(Vector3.Zero);

    static Sod.Matrix34 At(Vector3 position) =>
        new()
        {
            Right = Vector3.UnitX,
            Up = Vector3.UnitY,
            Front = Vector3.UnitZ,
            Position = position,
        };

    static Sod.Node Root() => new() { Name = "root", Transform = Identity };

    static Sod.Node Mesh(string name, Vector3 offset, params Vector3[] vertices) =>
        new()
        {
            Name = name,
            Parent = "root",
            Type = Sod.NodeType.Mesh,
            Transform = At(offset),
            Mesh = new Sod.Mesh { Vertices = vertices },
        };

    static Sod.Node Sprite(string name, Sod.Matrix34 transform) =>
        new()
        {
            Name = name,
            Parent = "root",
            Type = Sod.NodeType.Sprite,
            Transform = transform,
        };

    static Sod Model(params Sod.Node[] nodes) => new() { Version = 1.8f, Nodes = [Root(), .. nodes] };

    static SpriteNodeLine Line(string node, float w, float h, bool billboard) =>
        new()
        {
            Node = node,
            Sprite = "s",
            Animation = "const",
            Width = w,
            Height = h,
            Colour = Vector3.One,
            Billboard = billboard,
            Table = "lights.spr",
            Line = 1,
        };

    // The eight corners of a box of half extents `h` around `c`.
    static Vector3[] Corners(Vector3 c, Vector3 h) =>
        [
            .. from x in new[] { -1f, 1f }
            from y in new[] { -1f, 1f }
            from z in new[] { -1f, 1f }
            select c + h * new Vector3(x, y, z),
        ];

    static void AreClose(Vector3 expected, Vector3 actual)
    {
        Assert.AreEqual(expected.X, actual.X, 1e-4, $"x of {actual}");
        Assert.AreEqual(expected.Y, actual.Y, 1e-4, $"y of {actual}");
        Assert.AreEqual(expected.Z, actual.Z, 1e-4, $"z of {actual}");
    }

    [TestMethod]
    public void BoxCornersLieOnTheEllipsoidAndTheLongestRadiusGetsThePad()
    {
        var model = Model(Mesh("hull", Vector3.Zero, Corners(Vector3.Zero, new Vector3(2, 1, 4))));

        var e = ShieldEllipsoid.Of(model, [], 0.5f)!;

        // half extents times √3 pass through the corners, so the fit leaves them; the pad is 0.5 on the longest radius
        var first = new Vector3(2, 1, 4) * MathF.Sqrt(3);
        var k = 0.5f / first.Z + 1;
        AreClose(first * k, e.Radii);
        Assert.AreEqual(first.Z + 0.5f, e.Radii.Z, 1e-4);
        AreClose(Vector3.Zero, e.Center);
    }

    [TestMethod]
    public void FarthestVertexSetsTheFit()
    {
        // an octahedron: its tips touch the box's faces, not its corners
        var h = new Vector3(2, 1, 4);
        var model = Model(
            Mesh(
                "hull",
                Vector3.Zero,
                [new(h.X, 0, 0), new(-h.X, 0, 0), new(0, h.Y, 0), new(0, -h.Y, 0), new(0, 0, h.Z), new(0, 0, -h.Z)]
            )
        );

        var e = ShieldEllipsoid.Of(model, [], 0)!;

        // the tips lie at 1/√3 of the first radii, so the fit shrinks the ellipsoid onto them
        AreClose(h, e.Radii);
    }

    [TestMethod]
    public void EllipsoidSitsAtTheBoxCentreAndTheHitTestAtTheRescaledCentre()
    {
        var model = Model(Mesh("hull", new Vector3(0, 0, 10), Corners(Vector3.Zero, new Vector3(1, 1, 1))));

        var e = ShieldEllipsoid.Of(model, [], 0.5f)!;

        AreClose(new Vector3(0, 0, 10), e.Center);
        var first = MathF.Sqrt(3);
        AreClose(new Vector3(0, 0, 10) * e.Radii.Z / first, e.HitCenter);
    }

    [TestMethod]
    public void BillboardSpriteWidensTheBoxByItsFullSize()
    {
        var mesh = Mesh("hull", Vector3.Zero, Corners(Vector3.Zero, new Vector3(1, 1, 1)));
        var model = Model(mesh, Sprite("redstrobe_4", At(new Vector3(1, 0, 0))));

        var e = ShieldEllipsoid.Of(model, [Line("redstrobe", 0.5f, 0.25f, billboard: true)], 0)!;

        // box x from −1 to 1.5, y from −1 to 1: centre x 0.25
        AreClose(new Vector3(0.25f, 0, 0), e.Center);
    }

    [TestMethod]
    public void TurnedSpriteAddsHalfItsSizeInItsOwnPlane()
    {
        var mesh = Mesh("hull", Vector3.Zero, Corners(Vector3.Zero, new Vector3(1, 1, 1)));
        // the sprite's X axis along the model's Z: its width reaches along Z
        var turned = new Sod.Matrix34
        {
            Right = Vector3.UnitZ,
            Up = Vector3.UnitY,
            Front = -Vector3.UnitX,
            Position = new Vector3(0, 0, 1),
        };
        var model = Model(mesh, Sprite("yellowconst_1_2", turned));

        var e = ShieldEllipsoid.Of(model, [Line("yellowconst", 2, 0.5f, billboard: false)], 0)!;

        // box z from −1 to 1 + 1: centre z 0.5
        AreClose(new Vector3(0, 0, 0.5f), e.Center);
    }

    [TestMethod]
    public void SpriteNodeTakesTheFirstLineOfItsNameUpToTheFirstUnderscore()
    {
        var lines = new[]
        {
            Line("yellowconst", 1, 1, false),
            Line("YELLOWCONST", 3, 3, true),
            Line("yellowconst_1", 9, 9, true),
        };

        Assert.AreSame(lines[0], ShieldEllipsoid.SpriteNodeOf("yellowconst_1_2", lines));
        Assert.IsNull(ShieldEllipsoid.SpriteNodeOf("greenconst", lines));
    }

    [TestMethod]
    public void ModelWithoutMeshesKeepsTheFirstRadiiWithoutThePad()
    {
        var model = Model(
            Sprite("redstrobe_1", At(new Vector3(0, 0, -1))),
            Sprite("redstrobe_2", At(new Vector3(0, 0, 1)))
        );

        var e = ShieldEllipsoid.Of(model, [Line("redstrobe", 1, 1, billboard: true)], 0.5f)!;

        AreClose(new Vector3(1, 1, 1) * MathF.Sqrt(3), e.Radii);
    }

    [TestMethod]
    public void FlatBoxHasNoEllipsoid()
    {
        // one billboard: its corners make a box of half extents (1, 1, 0), which the game divides by zero
        var model = Model(Sprite("redstrobe", Identity));

        Assert.IsNull(ShieldEllipsoid.Of(model, [Line("redstrobe", 1, 1, billboard: true)], 0.5f));
    }

    [TestMethod]
    public void BoundingRadiusIsFromTheBoxCentreToTheFarthestPoint()
    {
        // a cap: a ring at z 0 of radius 4 and a tip at z 2
        var model = Model(
            Mesh(
                "cap",
                Vector3.Zero,
                new Vector3(4, 0, 0),
                new Vector3(-4, 0, 0),
                new Vector3(0, 4, 0),
                new Vector3(0, -4, 0),
                new Vector3(0, 0, 2)
            )
        );

        // centre (0, 0, 1); the ring is √17 away
        Assert.AreEqual(MathF.Sqrt(17), ShieldEllipsoid.BoundingRadius(model, []), 1e-4);
    }
}
