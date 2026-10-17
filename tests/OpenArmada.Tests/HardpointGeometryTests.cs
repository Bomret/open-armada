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

    static void AreClose(Vector3 expected, Vector3? actual)
    {
        Assert.IsNotNull(actual);
        Assert.IsTrue(Vector3.Distance(expected, actual.Value) < 1e-4f, $"expected {expected}, got {actual}");
    }

    [TestMethod]
    public void HardpointUnderParentsGetsTheComposedPositionWithZMirrored() =>
        AreClose(
            new Vector3(11, 2, -8),
            HardpointGeometry.ModelOffset(
                Model(Node("root", "", new Vector3(1, 2, 3)), Node("hp01", "root", new Vector3(10, 0, 5))),
                "hp01"
            )
        );

    [TestMethod]
    public void RotatedParentCarriesTheHardpointAround()
    {
        var root = Node("root", "", Vector3.Zero);
        root.Transform = new Sod.Matrix34
        {
            Right = Vector3.UnitZ,
            Up = Vector3.UnitY,
            Front = -Vector3.UnitX,
            Position = Vector3.Zero,
        };
        AreClose(
            new Vector3(-5, 0, 0),
            HardpointGeometry.ModelOffset(Model(root, Node("hp01", "root", new Vector3(0, 0, 5))), "hp01")
        );
    }

    [TestMethod]
    public void HardpointNameMatchesWithoutRegardToCase() =>
        AreClose(
            new Vector3(1, 0, 0),
            HardpointGeometry.ModelOffset(Model(Node("hp01", "", new Vector3(1, 0, 0))), "HP01")
        );

    [TestMethod]
    public void HardpointTheModelLacksHasNoOffset() =>
        Assert.IsNull(HardpointGeometry.ModelOffset(Model(Node("root", "", Vector3.Zero)), "hp99"));

    [TestMethod]
    public void ParentTheModelLacksEndsTheChainAtTheNode() =>
        AreClose(
            new Vector3(1, 0, 0),
            HardpointGeometry.ModelOffset(Model(Node("hp01", "ghost", new Vector3(1, 0, 0))), "hp01")
        );

    [TestMethod]
    public void ParentCycleEndsWithoutLooping() =>
        AreClose(
            new Vector3(1, 2, 0),
            HardpointGeometry.ModelOffset(
                Model(Node("a", "b", new Vector3(1, 0, 0)), Node("b", "a", new Vector3(0, 2, 0))),
                "a"
            )
        );

    [TestMethod]
    public void NodeThatIsItsOwnParentEndsWithoutLooping() =>
        AreClose(new Vector3(1, 0, 0), HardpointGeometry.ModelOffset(Model(Node("a", "a", new Vector3(1, 0, 0))), "a"));

    static (PlacedObject Unit, ShipPose Pose) Standing(Vector3 x, Vector3 y, Vector3 z)
    {
        var unit = Placed("u", 1, 10) with { AxisX = x, AxisY = y, AxisZ = z };

        return (unit, MoveGeometry.PlacedPose(unit.Position, unit.AxisZ));
    }

    [TestMethod]
    public void UnitAsPlacedPutsTheOffsetBesideItsPosition()
    {
        var (unit, pose) = Standing(Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ);
        AreClose(new Vector3(11, 2, 3), HardpointGeometry.WorldPosition(unit, pose, new Vector3(1, 2, 3)));
    }

    [TestMethod]
    public void UnitThatTurnedMovesTheOffsetWithIt()
    {
        var (unit, pose) = Standing(Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ);
        pose = pose with { Heading = pose.Heading + MathF.PI / 2 };
        AreClose(new Vector3(10, 0, -1), HardpointGeometry.WorldPosition(unit, pose, Vector3.UnitX));
    }

    [TestMethod]
    public void UnitThatBankedMovesTheOffsetWithIt()
    {
        var (unit, pose) = Standing(Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ);
        pose = pose with { Bank = MathF.PI / 2 };
        AreClose(new Vector3(10, -1, 0), HardpointGeometry.WorldPosition(unit, pose, Vector3.UnitX));
    }

    [TestMethod]
    public void UnitPlacedWithAMirroredBasisMirrorsTheOffset()
    {
        var (unit, pose) = Standing(-Vector3.UnitX, -Vector3.UnitY, -Vector3.UnitZ);
        AreClose(new Vector3(9, -2, -3), HardpointGeometry.WorldPosition(unit, pose, new Vector3(1, 2, 3)));
    }
}
