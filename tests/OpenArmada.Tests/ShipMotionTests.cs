using System;
using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Sandbox;

namespace OpenArmada.Tests;

// The kinematic model of a ship's move order (ADR 0003), with physics values built in code (INV-0001).
// Heading 0 faces +Z; a positive heading turns toward +X.
[TestClass]
public class ShipMotionTests
{
    const double Speed = 10, Yaw = 1;

    static ShipMotion Ship(double? maxRoll = 30, float heading = 0, float y = 5) =>
        new(new PhysicsValues(Speed, Yaw, maxRoll), new Vector3(0, y, 0), heading);

    // Runs the ship for `seconds` in steps of `dt` and returns the last pose and the largest bank and turn seen.
    static (ShipPose Pose, float MaxBank, float MaxTurn) Run(ShipMotion ship, double seconds, double dt)
    {
        var pose = ship.Step(0);
        float maxBank = 0, maxTurn = 0;
        for (double t = 0; t < seconds; t += dt)
        {
            var before = pose.Heading;
            pose = ship.Step(dt);
            maxBank = MathF.Max(maxBank, MathF.Abs(pose.Bank));
            maxTurn = MathF.Max(maxTurn, MathF.Abs(Wrap(pose.Heading - before)) / (float)dt);
        }
        return (pose, maxBank, maxTurn);
    }

    static float Wrap(float a) => MathF.IEEERemainder(a, 2 * MathF.PI);

    static void AssertAt(Vector3 expected, Vector3 actual)
    {
        Assert.AreEqual(expected.X, actual.X, 1e-3);
        Assert.AreEqual(expected.Y, actual.Y, 1e-3);
        Assert.AreEqual(expected.Z, actual.Z, 1e-3);
    }

    [TestMethod]
    public void AShipOrderedAheadTravelsAtImpulseSpeedAndStopsAtTheTarget()
    {
        var ship = Ship();
        ship.SetTarget(0, 100);
        var (pose, _, _) = Run(ship, 5, 0.125);
        AssertAt(new Vector3(0, 5, 50), pose.Position);
        (pose, _, _) = Run(ship, 10, 0.125);
        AssertAt(new Vector3(0, 5, 100), pose.Position);
    }

    [TestMethod]
    public void ALargeStepStopsAtTheTargetWithoutOvershooting()
    {
        var ship = Ship();
        ship.SetTarget(0, 100);
        AssertAt(new Vector3(0, 5, 100), ship.Step(1000).Position);
    }

    [TestMethod]
    public void AShipOrderedBehindInsideItsTurningCircleReachesIt()
    {
        // the turning circle has radius Speed / Yaw = 10; the target is 3 behind and 2 to the side
        var ship = Ship();
        ship.SetTarget(2, -3);
        var (pose, _, _) = Run(ship, 60, 0.05);
        AssertAt(new Vector3(2, 5, -3), pose.Position);
    }

    [TestMethod]
    public void TheTurnRateNeverExceedsYawRotationRate()
    {
        var ship = Ship();
        ship.SetTarget(-50, -50);
        var (_, _, maxTurn) = Run(ship, 30, 0.05);
        Assert.IsTrue(maxTurn <= Yaw + 1e-3, $"turn rate {maxTurn}");
        Assert.IsTrue(maxTurn > Yaw - 1e-3, $"turn rate {maxTurn}");
    }

    [TestMethod]
    public void TurnsTakeTheShorterWayAcrossPi()
    {
        // heading just under +pi, target just past it: a short turn through pi, not a long one back through 0
        var ship = Ship(heading: 3.0f);
        ship.SetTarget(-0.1f * 100, -100); // bearing = atan2(-10, -100), about -3.04
        var pose = ship.Step(0.1);
        Assert.IsTrue(pose.Heading > 3.0f, $"heading {pose.Heading}");
    }

    [TestMethod]
    public void TheBankLeansIntoTheTurnStaysWithinMaxRollAndReturnsToZeroWhenStraight()
    {
        var ship = Ship(maxRoll: 30);
        ship.SetTarget(100, 100); // a left turn: the heading grows
        var bank = ship.Step(0.1).Bank;
        Assert.IsTrue(bank < 0, $"bank {bank}");
        var (pose, maxBank, _) = Run(ship, 30, 0.05);
        Assert.IsTrue(maxBank <= 30 * MathF.PI / 180 + 1e-5f, $"bank {maxBank}");
        Assert.IsTrue(maxBank > 0);
        Assert.AreEqual(0, pose.Bank, 1e-6);
    }

    [TestMethod]
    public void TheBankIsZeroOnAStraightRunBeforeTheShipArrives()
    {
        var ship = Ship(maxRoll: 30);
        ship.SetTarget(100, 100); // a 45 degree turn takes under one second at Yaw 1; the target is 141 away at speed 10
        var pose = ship.Step(0);
        for (var i = 0; i < 100; i++) pose = ship.Step(0.05); // 5 seconds
        Assert.IsTrue(new Vector2(pose.Position.X - 100, pose.Position.Z - 100).Length() > 50, "the ship has not arrived");
        Assert.AreEqual(0, pose.Bank, 1e-6);
    }

    [TestMethod]
    public void ATurnInPlaceFollowedByTravelTakesTheSameTimeWhateverTheStepSize()
    {
        // 15 behind, inside the turning circle: a pi turn (3.14 s) then 15 at speed 10; after 4 s it is 8.6 behind
        var fine = Ship();
        var coarse = Ship();
        fine.SetTarget(0, -15);
        coarse.SetTarget(0, -15);
        ShipPose pose = default;
        for (var i = 0; i < 400; i++) pose = fine.Step(0.01);
        Assert.AreEqual(-8.58, pose.Position.Z, 0.05);
        for (var i = 0; i < 8; i++) pose = coarse.Step(0.5);
        Assert.AreEqual(-8.58, pose.Position.Z, 0.05);
    }

    [TestMethod]
    public void TheHeadingStaysWithinMinusPiAndPi()
    {
        var ship = Ship(heading: 3.0f);
        ship.SetTarget(-10, -100);
        var pose = ship.Step(1);
        Assert.IsTrue(pose.Heading > -MathF.PI && pose.Heading <= MathF.PI, $"heading {pose.Heading}");
    }

    [TestMethod]
    public void ARightTurnBanksTheOtherWay()
    {
        var ship = Ship();
        ship.SetTarget(-100, 100);
        Assert.IsTrue(ship.Step(0.1).Bank > 0);
    }

    [TestMethod]
    public void WithoutMaxRollTheBankStaysZero()
    {
        var ship = Ship(maxRoll: null);
        ship.SetTarget(100, 100);
        var (pose, maxBank, _) = Run(ship, 30, 0.05);
        Assert.AreEqual(0, maxBank);
        Assert.AreEqual(0, pose.Bank);
    }

    [TestMethod]
    public void TheSameOrderWithTwoStepSizesEndsAtTheSamePoint()
    {
        var fine = Ship();
        var coarse = Ship();
        fine.SetTarget(-40, -70);
        coarse.SetTarget(-40, -70);
        AssertAt(new Vector3(-40, 5, -70), Run(fine, 60, 0.01).Pose.Position);
        AssertAt(new Vector3(-40, 5, -70), Run(coarse, 60, 1.7).Pose.Position);
    }

    [TestMethod]
    public void ATargetAtTheShipsPositionCausesNoNaNAndNoMotion()
    {
        var ship = Ship(heading: 0.5f);
        ship.SetTarget(0, 0);
        var pose = ship.Step(0.5);
        AssertAt(new Vector3(0, 5, 0), pose.Position);
        Assert.AreEqual(0.5f, pose.Heading);
        Assert.AreEqual(0f, pose.Bank);
    }

    [TestMethod]
    public void ANewOrderReplacesThePreviousOne()
    {
        var ship = Ship();
        ship.SetTarget(0, 100);
        ship.Step(2);
        ship.SetTarget(100, 20);
        AssertAt(new Vector3(100, 5, 20), Run(ship, 60, 0.1).Pose.Position);
    }

    [TestMethod]
    public void TheShipKeepsItsHeight()
    {
        var ship = Ship(y: 7);
        ship.SetTarget(30, 30);
        Assert.AreEqual(7f, Run(ship, 20, 0.1).Pose.Position.Y);
    }

    [TestMethod]
    public void ANegativeOrZeroStepChangesNothing()
    {
        var ship = Ship();
        ship.SetTarget(0, 100);
        AssertAt(new Vector3(0, 5, 0), ship.Step(-1).Position);
    }
}
