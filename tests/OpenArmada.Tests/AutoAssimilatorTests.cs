using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Sandbox;
using static OpenArmada.Tests.CombatFixtures;

namespace OpenArmada.Tests;

// The Auto-Assimilator (key slot 3, range 400, 5 crew per second) over a combat world built in code, and the crew pool its
// surplus goes to.
[TestClass]
public class AutoAssimilatorTests
{
    static SpecialWeaponProfile Assimilator(float range = 400, float cost = 10, float rate = 5) =>
        new()
        {
            Kind = SpecialWeaponKind.AutoAssimilator,
            Slot = 3,
            Range = range,
            EnergyCost = cost,
            CrewRate = rate,
            TargetRange = 0,
            LifeSpan = 0,
            BoardingSize = 0,
            BoardingInterval = 0,
            BoardingDelay = 0,
        };

    sealed record Spec(PlacedObject Object, CombatProfile Profile, FlightModel? Motion = null);

    static Spec Borg(string label, int team, float x, float crew = 50, float maxCrew = 100, float energy = 1000) =>
        new(
            Placed(label, team, x),
            Profile(crew: crew, maximumCrew: maxCrew, maxSpecialEnergy: energy, raceName: "borg") with
            {
                SpecialWeapons = [Assimilator()],
            }
        );

    static Spec Ship(string label, int team, float x, float crew = 12) =>
        new(Placed(label, team, x), Profile(crew: crew, maximumCrew: 100, raceName: "klingon"));

    static CombatWorld World(double poolCap, params Spec[] specs) =>
        new(
            new UnitPoses(specs.Select(s => s.Object)),
            specs.Select(s => new CombatUnit
            {
                Unit = s.Object,
                Profile = s.Profile,
                DefinitionName = s.Object.Label + ".odf",
                Radius = 0,
                Mobile = s.Motion != null,
                Motion = s.Motion,
            }),
            1,
            () => 0,
            (_, _, pose) => pose.Position
        )
        {
            CrewPoolCap = poolCap,
        };

    static CombatWorld World(params Spec[] specs) => World(99999, specs);

    static double Crew(CombatWorld world, Spec unit) => world.State(unit.Object)!.Crew;

    [TestMethod]
    public void AnAssimilatorDrainsFiveCrewPerSecondIntoItselfAndLeavesTheShipItsTeamAndRace()
    {
        var borg = Borg("borg", 1, 0);
        var ship = Ship("ship", 2, 100);
        var world = World(borg, ship);
        Assert.IsTrue(world.SpecialWeapon(borg.Object, 3, ship.Object));
        world.Step(1);
        Assert.AreEqual(55, Crew(world, borg), 1e-6);
        Assert.AreEqual(7, Crew(world, ship), 1e-6);
        world.Step(5);
        var state = world.State(ship.Object)!;
        Assert.AreEqual((0.0, true, 2, "klingon"), (state.Crew, state.Derelict, state.Team, state.RaceName));
        // the last crew below one leaves with the derelict (CombatRules.CheckCrew): the total never grows
        Assert.IsTrue(Crew(world, borg) is > 61 and <= 62, $"borg crew {Crew(world, borg)}");
    }

    [TestMethod]
    public void TheDrainEndsOnADerelictTarget()
    {
        var borg = Borg("borg", 1, 0);
        var ship = Ship("ship", 2, 100, crew: 3);
        var world = World(borg, ship);
        world.SpecialWeapon(borg.Object, 3, ship.Object);
        world.Step(2);
        Assert.IsTrue(world.State(ship.Object)!.Derelict);
        Assert.IsNull(world.State(borg.Object)!.SpecialOrder);
    }

    [TestMethod]
    public void AFullAssimilatorSendsTheDrainedCrewToItsTeamsPoolAndNeverAboveTheCap()
    {
        var borg = Borg("borg", 1, 0, crew: 100);
        var ship = Ship("ship", 2, 100, crew: 40);
        var world = World(8, borg, ship);
        world.SpecialWeapon(borg.Object, 3, ship.Object);
        world.Step(4);
        Assert.AreEqual(100, Crew(world, borg), 1e-6);
        Assert.AreEqual(20, Crew(world, ship), 1e-6);
        Assert.AreEqual(8, world.CrewPools[1].Crew, 1e-6);
        Assert.AreEqual(0, world.CrewPools[2].Crew, 1e-6);
    }

    [TestMethod]
    public void TheShootersMaximumFillsBeforeThePool()
    {
        var borg = Borg("borg", 1, 0, crew: 98);
        var ship = Ship("ship", 2, 100, crew: 40);
        var world = World(borg, ship);
        world.SpecialWeapon(borg.Object, 3, ship.Object);
        world.Step(1);
        Assert.AreEqual(100, Crew(world, borg), 1e-6);
        Assert.AreEqual(3, world.CrewPools[1].Crew, 1e-6);
        Assert.AreEqual(138, Crew(world, borg) + Crew(world, ship) + world.CrewPools.Values.Sum(p => p.Crew), 1e-6);
    }

    [TestMethod]
    public void AnOrderOnATargetBeyondTheAttackRangeStartsNothing()
    {
        var borg = Borg("borg", 1, 0);
        var near = Ship("near", 2, 400);
        var far = Ship("far", 2, 401);
        var world = World(borg, near, far);
        Assert.IsFalse(world.SpecialWeapon(borg.Object, 3, far.Object));
        Assert.IsTrue(world.SpecialWeapon(borg.Object, 3, near.Object));
    }

    [TestMethod]
    public void TheDrainEndsOnceTheTargetIsBeyondTheAttackRange()
    {
        var motion = FlightFixtures.Cruiser(Placed("ship", 2, 100).Position);
        var borg = Borg("borg", 1, 0);
        var ship = Ship("ship", 2, 100) with { Motion = motion };
        var world = World(borg, ship);
        world.SpecialWeapon(borg.Object, 3, ship.Object);
        world.Step(0.02);
        motion.Push(new Vector3(300, 0, 0));
        world.Step(0.02);
        Assert.IsNotNull(world.State(borg.Object)!.SpecialOrder, "a gap of 400 is in range");
        motion.Push(new Vector3(1, 0, 0));
        world.Step(0.02);
        Assert.IsNull(world.State(borg.Object)!.SpecialOrder);
        var drained = Crew(world, ship);
        world.Step(1);
        Assert.AreEqual(drained, Crew(world, ship), 1e-9);
    }

    [TestMethod]
    public void TheDrainEndsOnATargetCapturedToTheAssimilatorsTeam()
    {
        var borg = Borg("borg", 1, 0);
        var ship = Ship("ship", 2, 100);
        var world = World(borg, ship);
        world.SpecialWeapon(borg.Object, 3, ship.Object);
        world.Step(1);
        world.ChangeTeam(ship.Object, 1, "borg", Race);
        world.Step(1);
        Assert.IsNull(world.State(borg.Object)!.SpecialOrder);
        Assert.AreEqual(7, Crew(world, ship), 1e-6);
    }

    [TestMethod]
    public void TheDrainedTotalOverTenSecondsIsTheSameAtThirtyAndSixtyTicksPerSecond()
    {
        var totals = new List<(double, double, double)>();
        foreach (var step in new[] { 1.0 / 30, 1.0 / 60 })
        {
            var borg = Borg("borg", 1, 0, crew: 95);
            var ship = Ship("ship", 2, 100, crew: 200);
            var world = World(borg, ship);
            world.SpecialWeapon(borg.Object, 3, ship.Object);
            for (var t = 0.0; t < 10 - 1e-9; t += step)
            {
                world.Step(step);
            }

            totals.Add((Crew(world, borg), Crew(world, ship), world.CrewPools[1].Crew));
        }

        Assert.AreEqual(totals[0].Item1, totals[1].Item1, 1e-6);
        Assert.AreEqual(totals[0].Item2, totals[1].Item2, 1e-6);
        Assert.AreEqual(totals[0].Item3, totals[1].Item3, 1e-6);
        Assert.AreEqual(150, totals[0].Item2, 1e-6);
    }

    [TestMethod]
    public void AShooterThatIsAStarbaseDrainsNothing()
    {
        var borg = Borg("borg", 1, 0);
        var ship = Ship("ship", 2, 100);
        var world = new CombatWorld(
            new UnitPoses([borg.Object, ship.Object]),
            new[] { borg, ship }.Select(s => new CombatUnit
            {
                Unit = s.Object,
                Profile = s.Profile,
                DefinitionName = "x.odf",
                Radius = 0,
                Mobile = false,
                Starbase = s == borg,
            }),
            1,
            () => 0,
            (_, _, pose) => pose.Position
        );
        world.SpecialWeapon(borg.Object, 3, ship.Object);
        world.Step(1);
        Assert.AreEqual(12, Crew(world, ship), 1e-6);
    }

    [TestMethod]
    public void TheDumpShowsEachTeamsPoolAndExpectReadsIt()
    {
        var borg = Borg("borg", 1, 0, crew: 100);
        var ship = Ship("ship", 2, 100);
        var world = World(30, borg, ship);
        world.SpecialWeapon(borg.Object, 3, ship.Object);
        world.Step(1);
        var play = new PlayState(
            new UnitNames([borg.Object, ship.Object]),
            new UnitPoses([borg.Object, ship.Object]).Pose,
            world.Units.ToList(),
            pools: () => world.CrewPools
        );
        var lines = play.Dump().Split('\n');
        CollectionAssert.AreEqual(
            new[] { "pool team=1 crew=5.00/30.00", "pool team=2 crew=0.00/30.00", "debris=0" },
            lines.Skip(2).Take(3).ToArray()
        );
        Assert.IsNull(
            play.Expect(PlayScript.Parse("expect borg pool >= 4.9", "x.play").Steps.Cast<ExpectStep>().Single())
        );
        Assert.IsNotNull(
            play.Expect(PlayScript.Parse("expect borg pool > 5.1", "x.play").Steps.Cast<ExpectStep>().Single())
        );
    }
}
