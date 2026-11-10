using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Sandbox;
using static OpenArmada.Tests.CombatFixtures;

namespace OpenArmada.Tests;

// The special-weapon order over a combat world built in code: the Borg cube's Holding Beam (key slot 4, energy cost 50,
// range 300) held on a target.
[TestClass]
public class SpecialWeaponTests
{
    static SpecialWeaponProfile Hold(float range = 300, float cost = 50) =>
        new()
        {
            Slot = 4,
            Range = range,
            EnergyCost = cost,
            TargetRange = range,
            LifeSpan = 30,
            BoardingSize = 20,
            BoardingInterval = 1,
            BoardingDelay = 1,
        };

    sealed record Spec(PlacedObject Object, CombatProfile Profile, bool Starbase = false, FlightModel? Motion = null);

    static Spec Cube(string label, int team, float x, float energy = 1000, float rate = 0) =>
        new(
            Placed(label, team, x),
            Profile(maxSpecialEnergy: energy, specialEnergyRate: rate) with
            {
                SpecialWeapons = [Hold()],
            }
        );

    static Spec Ship(string label, int team, float x, bool moving = false)
    {
        var placed = Placed(label, team, x);

        return new Spec(placed, Profile(), Motion: moving ? FlightFixtures.Cruiser(placed.Position) : null);
    }

    static CombatWorld World(out UnitPoses poses, params Spec[] specs)
    {
        poses = new UnitPoses(specs.Select(s => s.Object));

        return new CombatWorld(
            poses,
            specs.Select(s => new CombatUnit
            {
                Unit = s.Object,
                Profile = s.Profile,
                DefinitionName = s.Object.Label + ".odf",
                Radius = 0,
                Mobile = s.Motion != null,
                Motion = s.Motion,
                Starbase = s.Starbase,
            }),
            1,
            () => 0,
            (_, _, pose) => pose.Position
        );
    }

    static CombatWorld World(params Spec[] specs) => World(out _, specs);

    static double Energy(CombatWorld world, Spec unit) => world.State(unit.Object)!.SpecialEnergy;

    [TestMethod]
    public void AnOrderOnAnEnemyShipInRangeSpendsTheCostOnceThenDrainsTheCostEachSecond()
    {
        var cube = Cube("cube", 1, 0);
        var ship = Ship("ship", 2, 100);
        var world = World(cube, ship);
        Assert.IsTrue(world.SpecialWeapon(cube.Object, 4, ship.Object));
        Assert.AreEqual(950, Energy(world, cube), 1e-9);
        world.Step(1);
        Assert.AreEqual(900, Energy(world, cube), 1e-6);
    }

    [TestMethod]
    public void AnOrderWithLessEnergyThanTheCostStartsNothing()
    {
        var cube = Cube("cube", 1, 0, energy: 49.9f);
        var ship = Ship("ship", 2, 100);
        var world = World(cube, ship);
        Assert.IsFalse(world.SpecialWeapon(cube.Object, 4, ship.Object));
        Assert.AreEqual(49.9, Energy(world, cube), 1e-5);
        world.Step(1);
        Assert.AreEqual(49.9, Energy(world, cube), 1e-5);
        Assert.IsNull(world.State(cube.Object)!.SpecialOrder);
    }

    [TestMethod]
    public void TheBeamEndsWhenTheEnergyCannotCoverATickAndTheEnergyNeverGoesBelowZero()
    {
        // 75.5 less the cost leaves 25.5: 25 ticks of 1.0 each, then 0.5 cannot cover a tick
        var cube = Cube("cube", 1, 0, energy: 75.5f);
        var ship = Ship("ship", 2, 100);
        var world = World(cube, ship);
        world.SpecialWeapon(cube.Object, 4, ship.Object);
        world.Step(0.5);
        Assert.IsNotNull(world.State(cube.Object)!.SpecialOrder);
        world.Step(CombatWorld.Tick);
        Assert.IsNull(world.State(cube.Object)!.SpecialOrder);
        Assert.AreEqual(0.5, Energy(world, cube), 1e-6);
    }

    [TestMethod]
    public void TheDrainEndsOnTheSameTickWhateverTheLengthOfAStep()
    {
        var ends = new List<double>();
        foreach (var step in new[] { 0.02, 0.1, 0.33, 1.0 })
        {
            var cube = Cube("cube", 1, 0, energy: 53);
            var ship = Ship("ship", 2, 100);
            var world = World(cube, ship);
            world.SpecialWeapon(cube.Object, 4, ship.Object);
            while (world.State(cube.Object)!.SpecialOrder != null && world.Time < 10)
            {
                world.Step(step);
            }

            // a long step runs its whole ticks, so compare the energy left, which is the whole drain's sum
            ends.Add(Math.Round(Energy(world, cube), 6));
        }

        Assert.AreEqual(1, ends.Distinct().Count(), string.Join(", ", ends));
    }

    [TestMethod]
    public void OrderingTheSlotAgainEndsTheBeamWithNoRefund()
    {
        var cube = Cube("cube", 1, 0);
        var ship = Ship("ship", 2, 100);
        var world = World(cube, ship);
        world.SpecialWeapon(cube.Object, 4, ship.Object);
        world.Step(1);
        Assert.IsTrue(world.SpecialWeapon(cube.Object, 4, null));
        Assert.IsNull(world.State(cube.Object)!.SpecialOrder);
        Assert.AreEqual(900, Energy(world, cube), 1e-6);
        world.Step(1);
        Assert.AreEqual(900, Energy(world, cube), 1e-6);
    }

    [TestMethod]
    public void AnOrderOnAFriendlyANeutralTheCubeItselfAStationOrAFarShipStartsNothing()
    {
        var cube = Cube("cube", 1, 0);
        var friend = Ship("friend", 1, 50);
        var neutral = Ship("neutral", 0, 60);
        var station = Ship("station", 2, 70) with { Starbase = true };
        var far = Ship("far", 2, 301);
        var world = World(cube, friend, neutral, station, far);
        foreach (var target in new[] { friend, neutral, station, far, cube })
        {
            Assert.IsFalse(world.SpecialWeapon(cube.Object, 4, target.Object), target.Object.Label);
        }

        Assert.IsFalse(world.SpecialWeapon(cube.Object, 4, null));
        Assert.AreEqual(1000, Energy(world, cube));
        Assert.IsNull(world.State(cube.Object)!.SpecialOrder);
    }

    // A ship ordered to move 1000 units along +X at the start of the beam: it drives away unless the beam holds it.
    static (CombatWorld World, UnitPoses Poses, Spec Cube, Spec Ship) HeldShip(float cubeEnergy = 1000)
    {
        var cube = Cube("cube", 1, 0, energy: cubeEnergy);
        var ship = Ship("ship", 2, 100, moving: true);
        var world = World(out var poses, cube, ship);
        world.Move(ship.Object, 1000, 0);
        Assert.IsTrue(world.SpecialWeapon(cube.Object, 4, ship.Object));

        return (world, poses, cube, ship);
    }

    [TestMethod]
    public void TheHoldingBeamKeepsTheTargetsPositionAndHeadingWhileItRuns()
    {
        var (world, poses, _, ship) = HeldShip();
        world.Step(0.02);
        var held = poses.Pose(ship.Object);
        world.Step(3);
        var now = poses.Pose(ship.Object);
        Assert.AreEqual(0, Vector3.Distance(held.Position, now.Position), 1e-3);
        Assert.AreEqual((held.Pitch, held.Yaw, held.Roll), (now.Pitch, now.Yaw, now.Roll));
        Assert.AreEqual(0, Vector3.Distance(now.Position, world.State(ship.Object)!.Position!.Value), 1e-6);
    }

    [TestMethod]
    public void TheTargetMovesOnFromWhereItWasHeldWhenTheBeamIsTurnedOff()
    {
        var (world, poses, cube, ship) = HeldShip();
        world.Step(1);
        var held = poses.Pose(ship.Object).Position;
        world.SpecialWeapon(cube.Object, 4, null);
        world.Step(3);
        Assert.IsTrue(poses.Pose(ship.Object).Position.X > held.X + 1);
    }

    [TestMethod]
    public void TheBeamEndsWhenItsTargetIsOutOfRangeTimesOnePointThreeThree()
    {
        var cube = Cube("cube", 1, 0) with { Motion = FlightFixtures.Cruiser(Placed("cube", 1, 0).Position) };
        var ship = Ship("ship", 2, 100);
        var world = World(cube, ship);
        world.SpecialWeapon(cube.Object, 4, ship.Object);
        world.Step(0.02);
        Assert.IsNotNull(world.State(cube.Object)!.SpecialOrder);
        // the 300 range reaches 399 before the beam ends: the cube pushed to a gap of 398 keeps it, one of 400 ends it
        cube.Motion!.Push(new Vector3(-298, 0, 0));
        world.Step(0.02);
        Assert.IsNotNull(world.State(cube.Object)!.SpecialOrder);
        cube.Motion.Push(new Vector3(-2, 0, 0));
        world.Step(0.02);
        Assert.IsNull(world.State(cube.Object)!.SpecialOrder);
    }

    [TestMethod]
    public void TheBeamEndsWhenItsTargetJoinsTheCubesTeamOrIsDestroyed()
    {
        var cube = Cube("cube", 1, 0);
        var joined = Ship("joined", 2, 100);
        var doomed = Ship("doomed", 2, 120) with { Profile = Profile(maxHealth: 0, crew: 100) };
        var world = World(cube, joined, doomed);
        world.SpecialWeapon(cube.Object, 4, joined.Object);
        world.ChangeTeam(joined.Object, 1, "borg", Race);
        world.Step(0.02);
        Assert.IsNull(world.State(cube.Object)!.SpecialOrder);
        world.SpecialWeapon(cube.Object, 4, doomed.Object);
        world.State(doomed.Object)!.Destroyed = true;
        world.Step(0.02);
        Assert.IsNull(world.State(cube.Object)!.SpecialOrder);
    }

    [TestMethod]
    public void TheBeamEndsWhenTheCubeIsCapturedOrDestroyed()
    {
        var cube = Cube("cube", 1, 0);
        var ship = Ship("ship", 2, 100);
        var world = World(cube, ship);
        world.SpecialWeapon(cube.Object, 4, ship.Object);
        world.ChangeTeam(cube.Object, 2, "federation", Race);
        Assert.IsNull(world.State(cube.Object)!.SpecialOrder);
    }

    [TestMethod]
    public void TheTargetMovesOnWhenTheCubeIsDestroyed()
    {
        var (world, poses, cube, ship) = HeldShip();
        world.Step(0.5);
        var held = poses.Pose(ship.Object).Position;
        world.State(cube.Object)!.Dying = true;
        world.Step(3);
        Assert.IsTrue(poses.Pose(ship.Object).Position.X > held.X + 1);
    }

    [TestMethod]
    public void AMoveAttackOrStopOrderLeavesTheBeamOn()
    {
        var cube = Cube("cube", 1, 0);
        var ship = Ship("ship", 2, 100);
        var world = World(cube, ship);
        world.SpecialWeapon(cube.Object, 4, ship.Object);
        world.Move(cube.Object, 10, 10);
        world.ClearOrder(cube.Object);
        world.Step(0.02);
        Assert.IsNotNull(world.State(cube.Object)!.SpecialOrder);
    }

    [TestMethod]
    public void AnOrderForASlotTheUnitHasNoWeaponForStartsNothing()
    {
        var cube = Cube("cube", 1, 0);
        var ship = Ship("ship", 2, 100);
        var world = World(cube, ship);
        Assert.IsFalse(world.SpecialWeapon(cube.Object, 3, ship.Object));
        Assert.IsFalse(world.SpecialWeapon(ship.Object, 4, cube.Object));
        Assert.AreEqual(1000, Energy(world, cube));
    }
}
