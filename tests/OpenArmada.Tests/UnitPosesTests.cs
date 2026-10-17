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
        Assert.AreEqual(MoveGeometry.PlacedPose(ship.Position, ship.AxisZ), poses.Pose(ship));
    }

    [TestMethod]
    public void AnObjectThatIsNoUnitThrows() =>
        Assert.ThrowsExactly<System.Collections.Generic.KeyNotFoundException>(() =>
            new UnitPoses(Array.Empty<PlacedObject>()).Pose(Placed("x", 0))
        );

    [TestMethod]
    public void AMovingShipReadsItsPoseAfterEachStep()
    {
        var ship = Placed("ship", 0);
        var poses = new UnitPoses(new[] { ship });
        var motion = new ShipMotion(
            new PhysicsValues
            {
                ImpulseSpeed = 10,
                YawRotationRate = 1,
                MaxRoll = 30,
            },
            ship.Position,
            MoveGeometry.BowHeading(ship.AxisZ)
        );
        motion.SetTarget(0, -100);
        var stepped = poses.Advance(ship, motion, 1);
        Assert.AreEqual(stepped, poses.Pose(ship));
        Assert.AreNotEqual(MoveGeometry.PlacedPose(ship.Position, ship.AxisZ), poses.Pose(ship));
    }

    [TestMethod]
    public void AStationKeepsItsPlacedPoseAfterAShipHasMoved()
    {
        var ship = Placed("ship", 0);
        var station = Placed("station", 50);
        var poses = new UnitPoses(new[] { ship, station });
        var motion = new ShipMotion(
            new PhysicsValues
            {
                ImpulseSpeed = 10,
                YawRotationRate = 1,
                MaxRoll = 30,
            },
            ship.Position,
            MoveGeometry.BowHeading(ship.AxisZ)
        );
        motion.SetTarget(0, -100);
        poses.Advance(ship, motion, 1);
        Assert.AreEqual(MoveGeometry.PlacedPose(station.Position, station.AxisZ), poses.Pose(station));
    }
}
