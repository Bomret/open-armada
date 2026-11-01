using System;
using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Formats;
using OpenArmada.Sandbox;
using static OpenArmada.Tests.CombatFixtures;

namespace OpenArmada.Tests;

// Where a hardpoint is, in the model and in the world, from nodes and poses built in code (INV-0001).
[TestClass]
public class HardpointGeometryTests
{
    static Sod.Node Node(string name, string parent, Vector3 position) =>
        new()
        {
            Name = name,
            Parent = parent,
            Transform = new Sod.Matrix34
            {
                Right = Vector3.UnitX,
                Up = Vector3.UnitY,
                Front = Vector3.UnitZ,
                Position = position,
            },
        };

    static Sod Model(params Sod.Node[] nodes) => new() { Version = 1.8f, Nodes = nodes };

    static Vector3? Offset(Sod sod, string name) =>
        HardpointGeometry.Offsets(sod).TryGetValue(name, out var offset) ? offset : null;

    static void AreClose(Vector3 expected, Vector3? actual)
    {
        Assert.IsNotNull(actual);
        Assert.IsTrue(Vector3.Distance(expected, actual.Value) < 1e-4f, $"expected {expected}, got {actual}");
    }

    static readonly Sod.Node Root = Node("root", "", Vector3.Zero);

    [TestMethod]
    public void HardpointUnderParentsGetsTheComposedPositionWithZMirrored() =>
        AreClose(
            new Vector3(11, 2, -8),
            Offset(
                Model(Root, Node("mid", "root", new Vector3(1, 2, 3)), Node("hp01", "mid", new Vector3(10, 0, 5))),
                "hp01"
            )
        );

    [TestMethod]
    public void RotatedParentCarriesTheHardpointAround()
    {
        var mid = Node("mid", "root", Vector3.Zero);
        mid.Transform = new Sod.Matrix34
        {
            Right = Vector3.UnitZ,
            Up = Vector3.UnitY,
            Front = -Vector3.UnitX,
            Position = Vector3.Zero,
        };
        AreClose(new Vector3(-5, 0, 0), Offset(Model(Root, mid, Node("hp01", "mid", new Vector3(0, 0, 5))), "hp01"));
    }

    [TestMethod]
    public void RootTransformIsLeftOutBecauseThePlacementReplacesIt() =>
        AreClose(
            new Vector3(10, 0, -5),
            Offset(Model(Node("root", "", new Vector3(1, 2, 3)), Node("hp01", "root", new Vector3(10, 0, 5))), "hp01")
        );

    [TestMethod]
    public void HardpointNameMatchesWithoutRegardToCase() =>
        AreClose(new Vector3(1, 0, 0), Offset(Model(Root, Node("hp01", "root", new Vector3(1, 0, 0))), "HP01"));

    [TestMethod]
    public void HardpointTheModelLacksHasNoOffset() => Assert.IsNull(Offset(Model(Root), "hp99"));

    [TestMethod]
    public void ParentTheModelLacksEndsTheChainAtTheNode() =>
        AreClose(new Vector3(1, 0, 0), Offset(Model(Root, Node("hp01", "ghost", new Vector3(1, 0, 0))), "hp01"));

    [TestMethod]
    public void ParentCycleEndsAtTheNodeTheViewHangsFromTheRoot() =>
        AreClose(
            new Vector3(1, 0, 0),
            Offset(Model(Root, Node("a", "b", new Vector3(1, 0, 0)), Node("b", "a", new Vector3(0, 2, 0))), "a")
        );

    [TestMethod]
    public void HardpointBelowACycleEndsAtTheCycleNode() =>
        AreClose(
            new Vector3(1, 0, -5),
            Offset(
                Model(
                    Root,
                    Node("a", "b", new Vector3(1, 0, 0)),
                    Node("b", "a", new Vector3(0, 2, 0)),
                    Node("hp01", "a", new Vector3(0, 0, 5))
                ),
                "hp01"
            )
        );

    [TestMethod]
    public void NodeThatIsItsOwnParentEndsWithoutLooping() =>
        AreClose(new Vector3(1, 0, 0), Offset(Model(Root, Node("a", "a", new Vector3(1, 0, 0))), "a"));

    [TestMethod]
    [Timeout(5000)]
    public void ParentCycleWhoseNamesDifferOnlyInCaseEndsWithoutLooping() =>
        AreClose(
            new Vector3(1, 0, 0),
            Offset(Model(Root, Node("a", "B", new Vector3(1, 0, 0)), Node("b", "A", new Vector3(0, 2, 0))), "a")
        );

    [TestMethod]
    [Timeout(5000)]
    public void ParentNameMatchesWithRegardToCaseAsTheViewDoes() =>
        AreClose(
            new Vector3(10, 0, -5),
            Offset(
                Model(Root, Node("mid", "root", new Vector3(1, 2, 3)), Node("hp01", "MID", new Vector3(10, 0, 5))),
                "hp01"
            )
        );

    static (PlacedObject Unit, ShipPose Pose) Standing(Vector3 x, Vector3 y, Vector3 z)
    {
        var unit = Placed("u", 1, 10) with { AxisX = x, AxisY = y, AxisZ = z };

        return (unit, MoveGeometry.PlacedPose(unit));
    }

    [TestMethod]
    public void UnitAsPlacedPutsTheOffsetBesideItsPosition()
    {
        var (unit, pose) = Standing(Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ);
        AreClose(new Vector3(11, 2, 3), HardpointGeometry.WorldPosition(unit, pose, new Vector3(1, 2, 3)));
    }

    [TestMethod]
    public void UnitThatYawedMovesTheBowTowardsPlusX()
    {
        var (unit, pose) = Standing(Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ);
        pose = pose with { Yaw = MathF.PI / 2 };
        AreClose(new Vector3(11, 0, 0), HardpointGeometry.WorldPosition(unit, pose, -Vector3.UnitZ)); // the bow
        AreClose(new Vector3(10, 0, 1), HardpointGeometry.WorldPosition(unit, pose, Vector3.UnitX)); // the right side
    }

    [TestMethod]
    public void UnitThatPitchedLowersItsNoseAndRaisesItsTail()
    {
        var (unit, pose) = Standing(Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ);
        pose = pose with { Pitch = MathF.PI / 2 };
        AreClose(new Vector3(10, -1, 0), HardpointGeometry.WorldPosition(unit, pose, -Vector3.UnitZ)); // the bow
        AreClose(new Vector3(10, 1, 0), HardpointGeometry.WorldPosition(unit, pose, Vector3.UnitZ)); // the tail
    }

    [TestMethod]
    public void UnitThatRolledRaisesItsRightSide()
    {
        var (unit, pose) = Standing(Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ);
        pose = pose with { Roll = MathF.PI / 2 };
        AreClose(new Vector3(10, 1, 0), HardpointGeometry.WorldPosition(unit, pose, Vector3.UnitX));
        AreClose(new Vector3(10, -1, 0), HardpointGeometry.WorldPosition(unit, pose, -Vector3.UnitX));
    }

    [TestMethod]
    public void UnitThatRolledAndPitchedRollsFirstAboutItsForwardAxis()
    {
        var (unit, pose) = Standing(Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ);
        pose = pose with { Pitch = MathF.PI / 2, Roll = MathF.PI / 2 };
        AreClose(new Vector3(10, 0, -1), HardpointGeometry.WorldPosition(unit, pose, Vector3.UnitX));
    }

    [TestMethod]
    public void UnitAtItsPlacedPoseStandsWhereTheMapPutItWhateverItsOrientationAndMirror()
    {
        // game axes of a unit yawed 0.4, pitched 0.3, and rolled 0.2 (the game's composition), then mirrored or not
        var (sy, cy) = MathF.SinCos(0.4f);
        var (sp, cp) = MathF.SinCos(0.3f);
        var (sr, cr) = MathF.SinCos(0.2f);
        var right = new Vector3(cr * cy + sr * sp * sy, sr * cp, -cr * sy + sr * sp * cy);
        var up = new Vector3(-sr * cy + cr * sp * sy, cr * cp, sr * sy + cr * sp * cy);
        var front = new Vector3(cp * sy, -sp, cp * cy);
        foreach (var mirror in new[] { 1f, -1f })
        {
            var godotX = new Vector3(right.X, right.Y, -right.Z) * 2 * mirror;
            var godotY = new Vector3(up.X, up.Y, -up.Z) * 3 * mirror;
            var godotZ = new Vector3(-front.X, -front.Y, front.Z) * 4 * mirror;
            var unit = Placed("u", 1, 10) with { AxisX = godotX, AxisY = godotY, AxisZ = godotZ };
            var pose = MoveGeometry.PlacedPose(unit);
            var offset = new Vector3(1, 2, -3); // in Godot's frame: the node's basis applies to it as the scene does
            var world = HardpointGeometry.WorldPosition(unit, pose, offset);
            AreClose(unit.Position + godotX * 1 + godotY * 2 + godotZ * -3, world);
            AreClose(offset, HardpointGeometry.ModelOffset(unit, pose, world));
        }
    }

    [TestMethod]
    public void UnitPlacedWithAMirroredBasisMirrorsTheOffset()
    {
        var (unit, pose) = Standing(-Vector3.UnitX, -Vector3.UnitY, -Vector3.UnitZ);
        AreClose(new Vector3(9, -2, -3), HardpointGeometry.WorldPosition(unit, pose, new Vector3(1, 2, 3)));
    }

    [TestMethod]
    public void UnitPlacedWithScaledAxesScalesTheOffset()
    {
        var (unit, pose) = Standing(2 * Vector3.UnitX, 3 * Vector3.UnitY, 4 * Vector3.UnitZ);
        AreClose(new Vector3(12, 6, 12), HardpointGeometry.WorldPosition(unit, pose, new Vector3(1, 2, 3)));
    }

    [TestMethod]
    public void UnitPlacedWithOneMirroredAxisKeepsItsMirrorWhenItTurns()
    {
        var (unit, pose) = Standing(-Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ);
        pose = pose with { Yaw = pose.Yaw - MathF.PI / 2 };
        AreClose(new Vector3(10, 0, 1), HardpointGeometry.WorldPosition(unit, pose, Vector3.UnitX));
    }
}
