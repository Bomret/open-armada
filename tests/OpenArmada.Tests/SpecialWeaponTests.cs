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
