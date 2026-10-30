using System;
using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Formats;
using OpenArmada.Sandbox;

namespace OpenArmada.Tests;

// The geometry of a move order: where a click's ray meets the combat plane, and which way a ship's bow faces.
// The SOD's +Z bow sits at node-local -Z (SodBuilder mirrors Z), so in Godot's world the bow is -AxisZ.
[TestClass]
public class MoveGeometryTests
{
    [TestMethod]
    public void ARayThatHitsThePlaneGivesThePointOnIt()
    {
        var target = MoveGeometry.PlaneTarget(new Vector3(10, 50, 20), Vector3.Normalize(new Vector3(0, -1, 1)), 30);
        Assert.IsNotNull(target);
        Assert.AreEqual(10, target.Value.X, 1e-3);
        Assert.AreEqual(40, target.Value.Y, 1e-3); // the plane's point, Z in the Y of the pair
    }

    [TestMethod]
    public void ARayParallelToThePlaneIsIgnored() =>
        Assert.IsNull(MoveGeometry.PlaneTarget(new Vector3(0, 50, 0), Vector3.UnitX, 30));

    [TestMethod]
    public void ARayPointingAwayFromThePlaneIsIgnored() =>
        Assert.IsNull(MoveGeometry.PlaneTarget(new Vector3(0, 50, 0), Vector3.UnitY, 30));

    [TestMethod]
    public void ARayPointingUpFromBelowThePlaneHitsIt()
    {
        var target = MoveGeometry.PlaneTarget(new Vector3(0, 10, 0), Vector3.Normalize(new Vector3(1, 1, 0)), 30);
        Assert.IsNotNull(target);
        Assert.AreEqual(20, target.Value.X, 1e-3);
    }

    [TestMethod]
    public void ARayPointingDownFromBelowThePlaneIsIgnored() =>
        Assert.IsNull(MoveGeometry.PlaneTarget(new Vector3(0, 10, 0), -Vector3.UnitY, 30));

    // The axes of a unit in Godot's frame, as SandboxPlan builds them from the game's right, up, and front axes
    // (x, y, -z; x, y, -z; -x, -y, z), so a test states a placement in the game's own axes.
    static PlacedObject Unit(Vector3 right, Vector3 up, Vector3 front) =>
        CombatFixtures.Placed("u", 1, 1, 2) with
        {
            AxisX = new Vector3(right.X, right.Y, -right.Z),
            AxisY = new Vector3(up.X, up.Y, -up.Z),
            AxisZ = new Vector3(-front.X, -front.Y, front.Z),
        };

    // A ship at height 5 with the bow bearing `heading` and the stock flight values.
    static FlightModel Motion(float heading) => FlightFixtures.Ship(new Vector3(0, 5, 0), heading);

    static void AreClose(Vector3 expected, Vector3 actual) =>
        Assert.IsTrue((expected - actual).Length() < 1e-5, $"expected {expected}, was {actual}");

    static void AreAngles(ShipPose pose, float pitch, float yaw, float roll)
    {
        Assert.AreEqual(pitch, pose.Pitch, 1e-5, "pitch");
        Assert.AreEqual(yaw, pose.Yaw, 1e-5, "yaw");
        Assert.AreEqual(roll, pose.Roll, 1e-5, "roll");
    }

    [TestMethod]
    public void AnUnrotatedShipOrderedToPlusZTurnsItsBowToPlusZ()
    {
        var ship = Motion(MoveGeometry.PlacedPose(Unit(Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ)).Heading);
        ship.StartMove([ship.Pose.Position, new Vector3(0, 38.5f, 100)]);
        for (var i = 0; i < 2000 && !ship.HasArrived; i++)
        {
            ship.Follow(CombatBand.Stock);
            ship.Step(0.02, false, true);
        }

        // heading 0 faces +Z; the ship arcs onto the line while it flies, so it has turned all but a small angle
        Assert.IsTrue(ship.HasArrived);
        Assert.AreEqual(0, MathF.IEEERemainder(ship.Pose.Heading, 2 * MathF.PI), 0.5);
        Assert.IsTrue(ship.Pose.Position.Z > 50);
    }

    // The sandbox's frame is the game's with Z mirrored: the model's +Z bow stands at -Z in the world, its +X side at
    // +X, and +Y up. The offsets below are the model's in Godot's frame (SodBuilder mirrors Z), so the bow is -Z.
    static readonly Vector3 Bow = -Vector3.UnitZ;

    [TestMethod]
    public void AShipWithoutAnglesFacesTheGamesPlusZWhichIsMinusZInTheSandbox() =>
        AreClose(Vector3.UnitZ * -1, Vector3.Transform(Bow, MoveGeometry.Orientation(0, 0, 0)));

    [TestMethod]
    public void APositiveYawTurnsTheBowTowardsThePlusXOfBothFrames() =>
        AreClose(Vector3.UnitX, Vector3.Transform(Bow, MoveGeometry.Orientation(0, MathF.PI / 2, 0)));

    [TestMethod]
    public void APositivePitchLowersTheNose() =>
        AreClose(-Vector3.UnitY, Vector3.Transform(Bow, MoveGeometry.Orientation(MathF.PI / 2, 0, 0)));

    [TestMethod]
    public void APositiveRollRaisesTheRightSide() =>
        AreClose(Vector3.UnitY, Vector3.Transform(Vector3.UnitX, MoveGeometry.Orientation(0, 0, MathF.PI / 2)));

    [TestMethod]
    public void RollTurnsFirstAboutTheForwardAxisThenPitchThenYawAboutTheWorldsUp()
    {
        // game: roll 90 degrees takes the right side up, pitch 90 degrees then takes that up to the bow's old place
        AreClose(
            -Vector3.UnitZ,
            Vector3.Transform(Vector3.UnitX, MoveGeometry.Orientation(MathF.PI / 2, 0, MathF.PI / 2))
        );
        // game: yaw 90 degrees carries a pitched-down ship's right side from +X to -Z (+Z in the sandbox)
        AreClose(
            Vector3.UnitZ,
            Vector3.Transform(Vector3.UnitX, MoveGeometry.Orientation(MathF.PI / 2, MathF.PI / 2, 0))
        );
    }

    [TestMethod]
    public void AnUnrotatedPlacedUnitHasNoAnglesAndItsBowHeadingIsMinusZ()
    {
        var pose = MoveGeometry.PlacedPose(Unit(Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ));
        Assert.AreEqual((0f, 0f, 0f), (pose.Pitch, pose.Yaw, pose.Roll));
        Assert.AreEqual(MathF.PI, MathF.Abs(pose.Heading), 1e-5);
    }

    [TestMethod]
    public void AShipThatTurnedByAYawPointsItsBowAlongTravel()
    {
        // the sandbox's bearing of a bow at game yaw w is sin, cos of the heading pi - w
        var pose = MoveGeometry.PlacedPose(Unit(Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ)) with
        {
            Yaw = 0.7f,
        };
        var bow = Vector3.Transform(Bow, pose.Orientation);
        Assert.AreEqual(MathF.Sin(pose.Heading), bow.X, 1e-5);
        Assert.AreEqual(MathF.Cos(pose.Heading), bow.Z, 1e-5);
    }

    [TestMethod]
    public void AMirroredPlacedShipLeadsWithItsDrawnBow()
    {
        // The drawn bow of a unit is the model's bow offset carried through the placed basis; the ship travels along
        // the heading of its pose, so the two must agree for every mirror of the basis.
        var mirrors = new[]
        {
            (-Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ),
            (-Vector3.UnitX, -Vector3.UnitY, -Vector3.UnitZ),
            (Vector3.UnitX, Vector3.UnitY, -Vector3.UnitZ),
            (new Vector3(-0.8f, 0, 0.6f), Vector3.UnitY, new Vector3(0.6f, 0, 0.8f)),
        };
        foreach (var (x, y, z) in mirrors)
        {
            var unit = CombatFixtures.Placed("u", 1, 1, 2) with { AxisX = x, AxisY = y, AxisZ = z };
            var pose = MoveGeometry.PlacedPose(unit);
            var bow = HardpointGeometry.WorldPosition(unit, pose, Bow) - unit.Position;
            bow.Y = 0;
            bow = Vector3.Normalize(bow);
            Assert.AreEqual(MathF.Sin(pose.Heading), bow.X, 1e-5, $"bow x of {x} {y} {z}");
            Assert.AreEqual(MathF.Cos(pose.Heading), bow.Z, 1e-5, $"bow z of {x} {y} {z}");
        }
    }

    [TestMethod]
    public void AMirroredPlacedShipThatTurnsAfterAMoveOrderStillLeadsWithItsBow()
    {
        var unit = CombatFixtures.Placed("u", 1, 1, 2) with { AxisX = -Vector3.UnitX };
        var ship = Motion(MoveGeometry.PlacedPose(unit).Heading);
        ship.StartMove([ship.Pose.Position, new Vector3(300, 5, 0)]);
        var before = ship.Pose.Position;
        for (var i = 0; i < 100; i++)
        {
            ship.Follow(CombatBand.Stock);
            ship.Step(0.02, false, true);
        }

        var travelled = ship.Pose.Position - before;
        travelled.Y = 0;
        var bow = HardpointGeometry.WorldPosition(unit, ship.Pose, Bow) - ship.Pose.Position;
        bow.Y = 0;
        Assert.IsTrue(travelled.Length() > 1, $"travelled {travelled}");
        Assert.AreEqual(MathF.Sin(ship.Pose.Heading), Vector3.Normalize(bow).X, 1e-4, "bow x");
        Assert.AreEqual(MathF.Cos(ship.Pose.Heading), Vector3.Normalize(bow).Z, 1e-4, "bow z");
        Assert.IsTrue(Vector3.Dot(Vector3.Normalize(bow), Vector3.Normalize(travelled)) > 0.5f, "leads with its bow");
    }

    [TestMethod]
    public void AForwardAxisOfExactlyPointNineNineNineAsAFloatTakesThePoleBranch()
    {
        // the float 0.999f is a hair above the double 0.999, so the compare in doubles puts it on the pole branch
        var (pitch, yaw, roll) = MoveGeometry.Decompose(
            Vector3.UnitX,
            new Vector3(0, 0.04f, -0.999f),
            new Vector3(0, 0.999f, 0.04f)
        );
        Assert.AreEqual(-MathF.PI / 2, pitch, 1e-6);
        Assert.AreEqual(0, yaw, 1e-6);
        Assert.AreEqual(0, roll, 1e-6);
    }

    [TestMethod]
    public void ALevelPlacedUnitTurnedByAKnownYawStartsWithThatYaw()
    {
        // game front (1, 0, 0) is yaw 90 degrees; the right side then points along game -z
        var pose = MoveGeometry.PlacedPose(Unit(-Vector3.UnitZ, Vector3.UnitY, Vector3.UnitX));
        Assert.AreEqual(0, pose.Pitch, 1e-6);
        Assert.AreEqual(MathF.PI / 2, pose.Yaw, 1e-6);
        Assert.AreEqual(0, pose.Roll, 1e-6);
    }

    [TestMethod]
    public void APlacedPitchedUnitStartsWithItsPitch()
    {
        var (s, c) = MathF.SinCos(0.3f);
        var pose = MoveGeometry.PlacedPose(
            Unit(Vector3.UnitX, new Vector3(0, c, s), new Vector3(0, -s, c)) // pitch 0.3 lowers the nose
        );
        AreAngles(pose, 0.3f, 0, 0);
    }

    [TestMethod]
    public void APlacedRolledUnitStartsWithItsRoll()
    {
        var (s, c) = MathF.SinCos(0.4f);
        var pose = MoveGeometry.PlacedPose(
            Unit(new Vector3(c, s, 0), new Vector3(-s, c, 0), Vector3.UnitZ) // roll 0.4 raises the right side
        );
        AreAngles(pose, 0, 0, 0.4f);
    }

    [TestMethod]
    public void APlacedUnitPointingStraightUpHasPitchMinusHalfPiAndTakesItsYawFromTheRightSide()
    {
        // game yaw 0.5, pitch -90 degrees: front +Y, right (cos, 0, -sin), up (-sin, 0, -cos)
        var (s, c) = MathF.SinCos(0.5f);
        var pose = MoveGeometry.PlacedPose(Unit(new Vector3(c, 0, -s), new Vector3(-s, 0, -c), Vector3.UnitY));
        AreAngles(pose, -MathF.PI / 2, 0.5f, 0);
    }

    [TestMethod]
    public void APlacedUnitPointingStraightDownHasPitchHalfPiAndTakesItsYawFromTheRightSide()
    {
        // game yaw 0.5, pitch +90 degrees: front -Y, right (cos, 0, -sin), up (sin, 0, cos)
        var (s, c) = MathF.SinCos(0.5f);
        var pose = MoveGeometry.PlacedPose(Unit(new Vector3(c, 0, -s), new Vector3(s, 0, c), -Vector3.UnitY));
        AreAngles(pose, MathF.PI / 2, 0.5f, 0);
    }

    // The game's axes of a unit at the given yaw, pitch, and roll (the game's composition, 0x55d080).
    static (Vector3 Right, Vector3 Up, Vector3 Front) Axes(float yaw, float pitch, float roll)
    {
        var (sy, cy) = MathF.SinCos(yaw);
        var (sp, cp) = MathF.SinCos(pitch);
        var (sr, cr) = MathF.SinCos(roll);

        return (
            new Vector3(cr * cy + sr * sp * sy, sr * cp, -cr * sy + sr * sp * cy),
            new Vector3(-sr * cy + cr * sp * sy, cr * cp, sr * sy + cr * sp * cy),
            new Vector3(cp * sy, -sp, cp * cy)
        );
    }

    [TestMethod]
    public void AUnitPitchedRolledAndYawedAtOnceIsDecomposedAngleByAngle()
    {
        var (right, up, front) = Axes(0.4f, 0.3f, 0.2f);
        AreAngles(MoveGeometry.PlacedPose(Unit(right, up, front)), 0.3f, 0.4f, 0.2f);
    }

    [TestMethod]
    public void AForwardAxisJustInsideThePoleThresholdStillReadsAllThreeAngles()
    {
        // front.y = -sin(pitch) = 0.998 < 0.999: the ordinary branch, so the roll is kept
        var pitch = -MathF.Asin(0.998f);
        var (right, up, front) = Axes(0.5f, pitch, 0.2f);
        var pose = MoveGeometry.PlacedPose(Unit(right, up, front));
        Assert.AreEqual(pitch, pose.Pitch, 1e-3);
        Assert.AreEqual(0.5f, pose.Yaw, 1e-3);
        Assert.AreEqual(0.2f, pose.Roll, 1e-3);
    }

    [TestMethod]
    public void AForwardAxisBeyondThePoleThresholdButNotStraightUpTakesThePoleBranch()
    {
        // front.y = 0.9995 > 0.999: pitch -pi/2, roll 0, and the yaw from the right side
        var (right, up, front) = Axes(0.5f, -MathF.Asin(0.9995f), 0.2f);
        var pose = MoveGeometry.PlacedPose(Unit(right, up, front));
        Assert.AreEqual(-MathF.PI / 2, pose.Pitch, 1e-6);
        Assert.AreEqual(0, pose.Roll, 1e-6);
        Assert.IsTrue(MathF.Abs(pose.Yaw - MathF.Atan2(-right.Z, right.X)) < 1e-5, $"yaw {pose.Yaw}");
    }

    [TestMethod]
    public void ThePlacedPoseCarriesThePositionAndAHalfTurnAboutUpIsYawPi()
    {
        // game right (-1, 0, 0), up (0, 1, 0), front (0, 0, -1)
        var pose = MoveGeometry.PlacedPose(Unit(-Vector3.UnitX, Vector3.UnitY, -Vector3.UnitZ));
        Assert.AreEqual(new Vector3(1, 0, 2), pose.Position);
        Assert.AreEqual(MathF.PI, MathF.Abs(pose.Yaw), 1e-5);
    }

    [TestMethod]
    public void AShipWithMapFrontPlusXFacesPlusXAndFliesStraightAheadWithoutTurningOrBanking()
    {
        // game front (1, 0, 0): the bow points to +X in the sandbox too
        var heading = MoveGeometry.PlacedPose(Unit(-Vector3.UnitZ, Vector3.UnitY, Vector3.UnitX)).Heading;
        Assert.AreEqual(MathF.PI / 2, heading, 1e-5);
        var ship = Motion(heading);
        ship.StartMove([ship.Pose.Position, new Vector3(100, 5, 0)]);
        for (var i = 0; i < 50; i++)
        {
            ship.Follow(CombatBand.Stock);
            ship.Step(0.02, false, true);
        }

        var pose = ship.Pose;
        Assert.AreEqual(heading, pose.Heading, 1e-4);
        Assert.AreEqual(0, pose.Roll, 1e-5);
        Assert.IsTrue(pose.Position.X > 1, $"x {pose.Position.X}");
    }

    // A map twice as wide as deep, not centred on zero, so a swapped axis or a missing Z mirror shows. In Godot's
    // frame its X runs 0..6400 and its Z runs -3200 (north) to 0 (south).
    static readonly MapBounds Map = new()
    {
        MinX = 0,
        MaxX = 6400,
        MinY = -28.5f,
        MaxY = 105.5f,
        MinZ = 0,
        MaxZ = 3200,
    };

    [TestMethod]
    public void AClickFromAHighCameraThatHitsThePlaneBeyondTheMapIsClampedOntoItsEdge()
    {
        var hit = MoveGeometry.PlaneTarget(
            new Vector3(3200, 700, -1600),
            Vector3.Normalize(new Vector3(0, -0.05f, -1)),
            38.5f
        );
        Assert.IsNotNull(hit);
        Assert.IsTrue(hit.Value.Y < -3200); // the plane hit lies beyond the north edge
        var target = MoveGeometry.ClampToMap(hit.Value, Map);
        Assert.AreEqual(3200, target.X, 1e-3);
        Assert.AreEqual(-3200, target.Y, 1e-3);
    }

    [TestMethod]
    [DataRow(7000f, -1000f, 6400f, -1000f)] // beyond the east edge: X only
    [DataRow(-50f, -1000f, 0f, -1000f)] // beyond the west edge
    [DataRow(100f, -4000f, 100f, -3200f)] // beyond the north edge: Z only
    [DataRow(100f, 500f, 100f, 0f)] // beyond the south edge
    [DataRow(9000f, 900f, 6400f, 0f)] // beyond both: the nearest (south-east) corner
    [DataRow(-1e7f, -1e7f, 0f, -3200f)] // however far away: the north-west corner
    public void AHitOutsideTheMapIsClampedOnEachAxisOnItsOwn(float x, float z, float clampedX, float clampedZ)
    {
        var target = MoveGeometry.ClampToMap(new Vector2(x, z), Map);
        Assert.AreEqual(clampedX, target.X, 1e-3);
        Assert.AreEqual(clampedZ, target.Y, 1e-3);
    }

    // The same map with each pair inverted; the camera swaps an inverted pair, so the clamp does too.
    static readonly MapBounds InvertedMap = new()
    {
        MinX = 6400,
        MaxX = 0,
        MinY = -28.5f,
        MaxY = 105.5f,
        MinZ = 3200,
        MaxZ = 0,
    };

    [TestMethod]
    [DataRow(7000f, -1000f, 6400f, -1000f)] // beyond the east edge
    [DataRow(100f, -4000f, 100f, -3200f)] // beyond the north edge
    [DataRow(-1e7f, 1e7f, 0f, 0f)] // the south-west corner
    [DataRow(1234f, -567f, 1234f, -567f)] // inside: unchanged
    public void AMapWithInvertedBoundsClampsOntoTheSameEdgeAsTheUninvertedMap(
        float x,
        float z,
        float clampedX,
        float clampedZ
    )
    {
        var target = MoveGeometry.ClampToMap(new Vector2(x, z), InvertedMap);
        Assert.AreEqual(clampedX, target.X, 1e-3);
        Assert.AreEqual(clampedZ, target.Y, 1e-3);
    }

    [TestMethod]
    [DataRow(1234f, -567f)] // inside
    [DataRow(6400f, -3200f)] // on the north-east corner
    [DataRow(0f, 0f)] // on the south-west corner
    [DataRow(0f, -1500f)] // on the west edge
    public void AHitInsideTheMapOrOnItsEdgeIsUnchanged(float x, float z) =>
        Assert.AreEqual(new Vector2(x, z), MoveGeometry.ClampToMap(new Vector2(x, z), Map));
}
