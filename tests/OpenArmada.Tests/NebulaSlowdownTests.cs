using System;
using System.Collections.Generic;
using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Formats;
using OpenArmada.Sandbox;
using static OpenArmada.Tests.CombatFixtures;

namespace OpenArmada.Tests;

// The nebula pass of the combat world (0x43edc0): a ship with thrust in a nebula's cell flies under the kind's speed
// cap, and only its forward speed is capped (0x459440). The ship has an impulse speed of 10, flies +Z from (1000, 20,
// -1000) in Godot's frame (the map's (1000, 1000)), and the world ticks of 0.02 s.
[TestClass]
public class NebulaSlowdownTests
{
    static readonly MapBounds Bounds = new()
    {
        MinX = 0,
        MaxX = 6400,
        MinY = 0,
        MaxY = 0,
        MinZ = 0,
        MaxZ = 6400,
    };

    static readonly Vector3 Start = new(1000, 20, -1000);

    static Nebula Nebula(int kind, float x = 1000, float z = 1000, float y = 0, float radius = 3000) =>
        new()
        {
            Label = "n",
            Class = $"mnebula{kind}",
            Kind = kind,
            Center = new Vector3(x, y, -z),
            EffectRadius = radius,
        };

    static NebulaKind Kind(int kind, float coefficient, float limit = -1) =>
        new()
        {
            Kind = kind,
            Coefficient = coefficient,
            Limit = limit,
        };

    // The ship's speed after `seconds` of flight under a move order far ahead (so it holds full throttle), in a world with the
    // nebulae; a ship that starts as `start` (no engines: it coasts from `velocity`).
    static double SpeedAfter(
        int seconds,
        Nebula[] nebulae,
        NebulaKind[] kinds,
        StartingState? start = null,
        bool borg = false,
        bool move = true
    )
    {
        var unit = Placed("ship", 1, Start.X, Start.Z) with { Position = Start, Start = start };
        var motion = new FlightModel(
            FlightFixtures.Values(
                FlightFixtures.Fast.With(("impulseSpeed", 10)),
                FlightFixtures.Fast.With(("impulseSpeed", 10))
            ) with
            {
                BorgPhysics = borg,
            },
            FlightFixtures.Pose(Start)
        );
        var poses = new UnitPoses([unit]);
        var combat = new CombatUnit
        {
            Unit = unit,
            Profile = Profile(systems: [Sys("Engines")]),
            DefinitionName = "u.odf",
            Radius = 0,
            ModelRadius = 10,
            Mobile = true,
            Motion = motion,
            Model = null,
        };
        var world = new CombatWorld(poses, [combat], 1, new Random(7).NextDouble, (_, _, pose) => pose.Position)
        {
            Nebulae = new NebulaGrid(nebulae, kinds, Bounds),
        };
        if (move)
        {
            world.Move(unit, 1000, 5000);
        }

        for (var i = 0; i < seconds * 50; i++)
        {
            world.Step(CombatWorld.Tick);
        }

        return world.State(unit)!.Speed;
    }

    [TestMethod]
    public void AShipInAMutaraLikeNebulaReachesOnlyItsCoefficientOfItsForwardSpeed()
    {
        var free = SpeedAfter(40, [], []);
        var slowed = SpeedAfter(40, [Nebula(3)], [Kind(3, 0.6f)]);
        Assert.IsTrue(free > 5, $"free speed {free}");
        Assert.AreEqual(0.6f * free, slowed, 0.02f * free);
    }

    [TestMethod]
    public void AShipOutsideTheNebulaFliesAtItsFullSpeed()
    {
        var free = SpeedAfter(40, [], []);
        var apart = SpeedAfter(40, [Nebula(3, x: 4000, z: 4000, radius: 100)], [Kind(3, 0.6f)]);
        Assert.AreEqual(free, apart, 1e-5f);
    }

    [TestMethod]
    public void OverlappingNebulaeOfOneKindDoNotSlowAShipBelowTheCoefficient()
    {
        // that each kind applies once is pinned by NebulaGridTests.TwoNebulaeOfOneKindApplyOnce
        var once = SpeedAfter(40, [Nebula(3)], [Kind(3, 0.6f)]);
        var twice = SpeedAfter(40, [Nebula(3), Nebula(3, x: 1020, z: 1020)], [Kind(3, 0.6f)]);
        Assert.AreEqual(once, twice, 1e-5f);
    }

    [TestMethod]
    public void NebulaeOfTwoKindsSlowAShipByTheLowerCoefficient()
    {
        var lower = SpeedAfter(40, [Nebula(3)], [Kind(3, 0.6f)]);
        var both = SpeedAfter(40, [Nebula(2), Nebula(3)], [Kind(2, 0.7f), Kind(3, 0.6f)]);
        Assert.AreEqual(lower, both, 1e-5f);
    }

    [TestMethod]
    public void TheLowerCoefficientWinsWhetherItBelongsToTheLowerOrTheHigherKind()
    {
        var lower = SpeedAfter(40, [Nebula(2)], [Kind(2, 0.5f)]);
        var both = SpeedAfter(40, [Nebula(2), Nebula(3)], [Kind(2, 0.5f), Kind(3, 0.6f)]);
        Assert.AreEqual(lower, both, 1e-5f);
    }

    [TestMethod]
    public void AKindWithASpeedLimitOfZeroHoldsTheShipStillAndTheWorldStaysFinite()
    {
        var speed = SpeedAfter(10, [Nebula(5)], [Kind(5, 1f, limit: 0f)]);
        Assert.IsTrue(double.IsFinite(speed), $"speed {speed}");
        Assert.IsTrue(speed < 0.1, $"speed {speed}");
    }

    [TestMethod]
    public void AKindsSpeedLimitCapsTheForwardSpeed()
    {
        var limited = SpeedAfter(40, [Nebula(5)], [Kind(5, 1f, limit: 2f)]);
        Assert.AreEqual(2f, limited, 0.05f);
    }

    [TestMethod]
    public void ACoastingShipInANebulaIsNotSlowed()
    {
        var coast = new StartingState { DestroyedSystems = ["Engines"], Velocity = new Vector3(0, 0, 6) };
        var free = SpeedAfter(2, [], [], coast, move: false);
        var inside = SpeedAfter(2, [Nebula(3)], [Kind(3, 0.6f)], coast, move: false);
        Assert.IsTrue(free > 1);
        Assert.AreEqual(free, inside, 1e-5f);
    }

    [TestMethod]
    public void ABorgShipInANebulaIsNotSlowed()
    {
        var free = SpeedAfter(40, [], [], borg: true);
        var inside = SpeedAfter(40, [Nebula(3)], [Kind(3, 0.6f)], borg: true);
        Assert.AreEqual(free, inside, 1e-5f);
    }

    [TestMethod]
    public void ANebulaAboveThePlaneByMoreThanItsRadiusSlowsNoShip()
    {
        var free = SpeedAfter(40, [], []);
        var above = SpeedAfter(40, [Nebula(3, y: 3001)], [Kind(3, 0.6f)]);
        var below = SpeedAfter(40, [Nebula(3, y: -3500)], [Kind(3, 0.6f)]);
        Assert.AreEqual(free, above, 1e-5f);
        Assert.AreEqual(free, below, 1e-5f);
    }

    [TestMethod]
    public void TheSlowdownLastsOnlyWhileTheShipIsInTheNebula()
    {
        // the cap is given each tick and reset after each step: a nebula that ends leaves the ship free to speed up again
        var nebulae = new[] { Nebula(3, x: 1000, z: 1000, radius: 100) };
        var inside = SpeedAfter(8, nebulae, [Kind(3, 0.6f)]); // within 100 of the nebula all this time
        var free = SpeedAfter(8, [], []);
        Assert.IsTrue(inside < free);
        var out30 = SpeedAfter(60, nebulae, [Kind(3, 0.6f)]);
        Assert.AreEqual(SpeedAfter(60, [], []), out30, 0.01f);
    }
}
