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

    [TestMethod]
    public void TheHeadingOfAnUnrotatedShipFacesMinusZBecauseItsBowIsMinusAxisZ() =>
        Assert.AreEqual(MathF.PI, MathF.Abs(MoveGeometry.BowHeading(Vector3.UnitZ)), 1e-5);

    [TestMethod]
    public void AnUnrotatedShipOrderedToPlusZTurnsItsBowToPlusZ()
    {
        var ship = new ShipMotion(
            new PhysicsValues(10, 1, 30),
            new Vector3(0, 5, 0),
            MoveGeometry.BowHeading(Vector3.UnitZ)
        );
        ship.SetTarget(0, 100);
        var pose = ship.Step(0);
        for (var i = 0; i < 400; i++)
        {
            pose = ship.Step(0.1);
        }

        // heading 0 faces +Z; the ship arcs onto the line while it drives, so it has turned all but a small angle
        Assert.AreEqual(0, MathF.IEEERemainder(pose.Heading, 2 * MathF.PI), 0.2);
        Assert.AreEqual(100, pose.Position.Z, 1e-3);
    }

    [TestMethod]
    public void AShipThatTurnedByAHeadingChangePointsItsBowAlongTravel()
    {
        // an unrotated ship's bow is -Z; a pose heading of PI + change travels along (sin, cos) of that heading
        var bow = Vector3.Transform(-Vector3.UnitZ, MoveGeometry.Orientation(Quaternion.Identity, 0.7f, 0));
        Assert.AreEqual(MathF.Sin(MathF.PI + 0.7f), bow.X, 1e-5);
        Assert.AreEqual(MathF.Cos(MathF.PI + 0.7f), bow.Z, 1e-5);
    }

    [TestMethod]
    public void InALeftTurnTheInnerLeftWingGoesDown()
    {
        // the ship turns left (heading grows) and ShipMotion banks it negative; the bow is -Z, so left is -X
        var orientation = MoveGeometry.Orientation(Quaternion.Identity, 0, -0.3f);
        Assert.IsTrue(Vector3.Transform(-Vector3.UnitX, orientation).Y < 0);
        Assert.IsTrue(Vector3.Transform(Vector3.UnitX, orientation).Y > 0);
    }

    [TestMethod]
    public void TheHeadingTurnsAboutWorldYAfterTheInitialOrientationAndTheBankRollsAboutTheShipsOwnBow()
    {
        // the initial orientation turns the bow (-Z) to +X and the left wing (-X) to -Z
        var initial = Quaternion.CreateFromAxisAngle(Vector3.UnitY, -MathF.PI / 2);
        var orientation = MoveGeometry.Orientation(initial, MathF.PI / 2, 0.5f);
        var bow = Vector3.Transform(-Vector3.UnitZ, orientation);
        Assert.AreEqual(0, bow.X, 1e-5);
        Assert.AreEqual(0, bow.Y, 1e-5);
        Assert.AreEqual(-1, bow.Z, 1e-5);
        var leftWing = Vector3.Transform(-Vector3.UnitX, orientation);
        Assert.AreEqual(-MathF.Cos(0.5f), leftWing.X, 1e-5);
        Assert.AreEqual(MathF.Sin(0.5f), leftWing.Y, 1e-5);
        Assert.AreEqual(0, leftWing.Z, 1e-5);
    }

    [TestMethod]
    public void AShipWithMapFrontPlusXFacesPlusXAndFliesStraightAheadWithoutTurningOrBanking()
    {
        // SandboxPlan's AxisZ for Front = (1, 0, 0) is (-1, 0, 0), so the bow points to +X
        var heading = MoveGeometry.BowHeading(new Vector3(-1, 0, 0));
        Assert.AreEqual(MathF.PI / 2, heading, 1e-5);
        var ship = new ShipMotion(new PhysicsValues(10, 1, 30), new Vector3(0, 5, 0), heading);
        ship.SetTarget(100, 0);
        var pose = ship.Step(1);
        Assert.AreEqual(heading, pose.Heading, 1e-5);
        Assert.AreEqual(0, pose.Bank, 1e-6);
        Assert.AreEqual(10, pose.Position.X, 1e-3);
    }

    // A map twice as wide as deep, not centred on zero, so a swapped axis or a missing Z mirror shows. In Godot's
    // frame its X runs 0..6400 and its Z runs -3200 (north) to 0 (south).
    static readonly MapBounds Map = new(0, 6400, -28.5f, 105.5f, 0, 3200);

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
    static readonly MapBounds InvertedMap = new(6400, 0, -28.5f, 105.5f, 3200, 0);

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

    [TestMethod]
    public void APlacedUnitsPoseIsItsPositionAndBowHeadingWithoutBank()
    {
        var pose = MoveGeometry.PlacedPose(new Vector3(1, 2, 3), new Vector3(-1, 0, 0));
        Assert.AreEqual(new ShipPose(new Vector3(1, 2, 3), MathF.PI / 2, 0), pose);
    }
}
