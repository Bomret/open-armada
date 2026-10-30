using System;
using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Sandbox;

namespace OpenArmada.Tests;

// The pose every unit of a map reads: its placed pose from the moment the map opens, updated as a ship moves.
[TestClass]
public class UnitPosesTests
{
    // A unit placed at `x` on the X axis with the bow along -Z (axisZ +Z).
    static PlacedObject Placed(string label, float x) =>
        new()
        {
            Label = label,
            Class = "c",
            Model = "m",
            Team = 0,
            Slot = 0,
            Position = new Vector3(x, 5, 0),
            AxisX = Vector3.UnitX,
            AxisY = Vector3.UnitY,
            AxisZ = Vector3.UnitZ,
        };

    [TestMethod]
    public void EveryUnitReadsItsPlacedPoseFromTheStart()
    {
        var ship = Placed("ship", 3);
        var poses = new UnitPoses(new[] { ship });
        Assert.AreEqual(MoveGeometry.PlacedPose(ship), poses.Pose(ship));
    }

    // A unit whose game axes are given (the game's right, up, front), placed in Godot's frame as SandboxPlan does.
    static PlacedObject Oriented(Vector3 right, Vector3 up, Vector3 front) =>
        Placed("o", 0) with
        {
            AxisX = new Vector3(right.X, right.Y, -right.Z),
            AxisY = new Vector3(up.X, up.Y, -up.Z),
            AxisZ = new Vector3(-front.X, -front.Y, front.Z),
        };

    [TestMethod]
    public void AUnitPlacedLevelAndTurnedStartsWithTheGamesDecomposedAngles()
    {
        var level = Oriented(-Vector3.UnitZ, Vector3.UnitY, Vector3.UnitX); // yaw 90 degrees
        var pose = new UnitPoses([level]).Pose(level);
        Assert.AreEqual((0f, MathF.PI / 2, 0f), (pose.Pitch, pose.Yaw, pose.Roll));
    }

    [TestMethod]
    public void AUnitPlacedPitchedStartsPitchedAndOneRolledStartsRolled()
    {
        var (s, c) = MathF.SinCos(0.3f);
        var pitched = Oriented(Vector3.UnitX, new Vector3(0, c, s), new Vector3(0, -s, c));
        var rolled = Oriented(new Vector3(c, s, 0), new Vector3(-s, c, 0), Vector3.UnitZ);
        var poses = new UnitPoses([pitched, rolled]);
        Assert.AreEqual(0.3f, poses.Pose(pitched).Pitch, 1e-6);
        Assert.AreEqual(0f, poses.Pose(pitched).Roll, 1e-6);
        Assert.AreEqual(0.3f, poses.Pose(rolled).Roll, 1e-6);
        Assert.AreEqual(0f, poses.Pose(rolled).Pitch, 1e-6);
    }

    [TestMethod]
    public void AUnitPlacedPointingStraightUpStartsAtMinusHalfPiPitchAndStraightDownAtPlusHalfPi()
    {
        var up = Oriented(Vector3.UnitX, -Vector3.UnitZ, Vector3.UnitY);
        var down = Oriented(Vector3.UnitX, Vector3.UnitZ, -Vector3.UnitY);
        var poses = new UnitPoses([up, down]);
        Assert.AreEqual((-MathF.PI / 2, 0f, 0f), (poses.Pose(up).Pitch, poses.Pose(up).Yaw, poses.Pose(up).Roll));
        Assert.AreEqual((MathF.PI / 2, 0f, 0f), (poses.Pose(down).Pitch, poses.Pose(down).Yaw, poses.Pose(down).Roll));
    }

    [TestMethod]
    public void AUnitPlacedJustPastThePoleThresholdStartsAtTheHalfPiPitchWithNoRoll()
    {
        // game front (0, 0.9995, ~0.03): front.y > 0.999 reads as straight up, with the yaw from the right side
        var z = MathF.Sqrt(1 - 0.9995f * 0.9995f);
        var tilted = Oriented(Vector3.UnitX, new Vector3(0, z, -0.9995f), new Vector3(0, 0.9995f, z));
        var pose = new UnitPoses([tilted]).Pose(tilted);
        Assert.AreEqual((-MathF.PI / 2, 0f, 0f), (pose.Pitch, pose.Yaw, pose.Roll));
    }

    [TestMethod]
    public void AnObjectThatIsNoUnitThrows() =>
        Assert.ThrowsExactly<System.Collections.Generic.KeyNotFoundException>(() =>
            new UnitPoses(Array.Empty<PlacedObject>()).Pose(Placed("x", 0))
        );

    // A map can stack two objects of the same values; each is its own unit.
    [TestMethod]
    public void TwoIdenticalObjectsAreTwoUnits()
    {
        var (ship, twin) = (Placed("ship", 0), Placed("ship", 0));
        var poses = new UnitPoses(new[] { ship, twin });
        poses.Record(ship, Flown(ship));
        Assert.AreEqual(MoveGeometry.PlacedPose(twin), poses.Pose(twin));
    }

    // A ship that has flown for a second from the placed pose of `unit`, ordered ahead.
    static FlightModel Flown(PlacedObject unit)
    {
        var motion = FlightFixtures.Ship(unit.Position, MoveGeometry.PlacedPose(unit).Heading);
        motion.StartMove([motion.Pose.Position, new Vector3(0, 38.5f, -100)]);
        for (var i = 0; i < 50; i++)
        {
            motion.Follow(CombatBand.Stock);
            motion.Step(0.02, false, true);
        }

        return motion;
    }

    [TestMethod]
    public void AMovingShipReadsItsPoseAfterEachStep()
    {
        var ship = Placed("ship", 0);
        var poses = new UnitPoses(new[] { ship });
        var recorded = poses.Record(ship, Flown(ship));
        Assert.AreEqual(recorded, poses.Pose(ship));
        Assert.AreNotEqual(MoveGeometry.PlacedPose(ship), poses.Pose(ship));
    }

    [TestMethod]
    public void AStationKeepsItsPlacedPoseAfterAShipHasMoved()
    {
        var ship = Placed("ship", 0);
        var station = Placed("station", 50);
        var poses = new UnitPoses(new[] { ship, station });
        poses.Record(ship, Flown(ship));
        Assert.AreEqual(MoveGeometry.PlacedPose(station), poses.Pose(station));
    }
}
