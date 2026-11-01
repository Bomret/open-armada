using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Sandbox;
using static OpenArmada.Tests.CombatFixtures;

namespace OpenArmada.Tests;

// The transport and boarding rule of the combat world (docs/rules/combat-rules.md, "Transport and boarding"), over
// units built in code. Every number is a value the test names; the transporter values are the stock ones (range 500,
// delay 1, action delay 1, party 5) unless a test sets its own.
[TestClass]
public class TransportTests
{
    const double Tick = CombatWorld.Tick;

    sealed record Spec(
        string Label,
        int Team,
        float X,
        float Crew = 100,
        float Max = 100,
        float Shields = 0,
        float ShieldDelay = 0,
        RaceValues? Race = null,
        WeaponProfile? Weapon = null
    );

    static (CombatWorld World, Dictionary<string, PlacedObject> Units) Build(
        TransporterValues? values = null,
        params Spec[] specs
    )
    {
        var units = specs.ToDictionary(s => s.Label, s => Placed(s.Label, s.Team, s.X));
        var poses = new UnitPoses(units.Values);
        var world = new CombatWorld(
            poses,
            specs.Select(s => new CombatUnit
            {
                Unit = units[s.Label],
                Profile = Profile(
                    s.Weapon == null ? null : [s.Weapon],
                    maxHealth: s.Shields,
                    shieldDelay: s.ShieldDelay,
                    crew: s.Crew,
                    maximumCrew: s.Max,
                    race: s.Race
                ),
                DefinitionName = s.Label + ".odf",
                Radius = 0,
                Mobile = false,
            }),
            1,
            () => 0,
            (_, _, pose) => pose.Position
        )
        {
            Transporter = values ?? TransporterValues.Stock,
        };

        return (world, units);
    }

    // Steps the world one tick at a time for `seconds` and returns every event.
    static List<CombatEvent> Run(CombatWorld world, double seconds)
    {
        var events = new List<CombatEvent>();
        for (var i = 0; i < (int)Math.Round(seconds / Tick); i++)
        {
            events.AddRange(world.Step(Tick));
        }

        return events;
    }

    static double CrewOf(CombatWorld world, PlacedObject unit) => world.State(unit)!.Crew;

    // --- the order and its validity ---

    [TestMethod]
    public void ASourceWithNineCrewIsRefusedTheOrderAndOneWithTenTakesIt()
    {
        var (world, u) = Build(null, new Spec("nine", 1, 0, Crew: 9), new Spec("ten", 1, 0, Crew: 10), new Spec("t", 2, 100));
        Assert.IsFalse(world.Transport(u["nine"], u["t"]));
        Assert.IsTrue(world.Transport(u["ten"], u["t"]));
    }

    [TestMethod]
    public void ASourceAtExactlyTheTransporterMaximumTakesTheOrderAndSendsNoParty()
    {
        var (world, u) = Build(
            new TransporterValues(500, 1, 1, 10),
            new Spec("s", 1, 0, Crew: 10),
            new Spec("t", 2, 100)
        );
        Assert.IsTrue(world.Transport(u["s"], u["t"]));
        var events = Run(world, 2);
        Assert.AreEqual(0, events.OfType<PartySent>().Count());
        Assert.AreEqual(0, events.OfType<TransportStarted>().Count());
    }

    [TestMethod]
    public void TheOrderAcceptsHostileDerelictOwnAndNeutralTargetsBelowTheirMaximum()
    {
        var (world, u) = Build(
            null,
            new Spec("s", 1, 0),
            new Spec("hostile", 2, 100),
            new Spec("derelictOther", 2, 100, Crew: 0),
            new Spec("derelictOwn", 1, 100, Crew: 0),
            new Spec("derelictNeutral", 0, 100, Crew: 0),
            new Spec("own", 1, 100, Crew: 50),
            new Spec("neutral", 0, 100, Crew: 50)
        );
        foreach (var t in new[] { "hostile", "derelictOther", "derelictOwn", "derelictNeutral", "own", "neutral" })
        {
            Assert.IsTrue(world.Transport(u["s"], u[t]), t);
        }
    }

    [TestMethod]
    public void TheOrderRefusesAFullFriendlyUnitAUnitWithoutCrewAndItself()
    {
        var (world, u) = Build(
            null,
            new Spec("s", 1, 0),
            new Spec("full", 1, 100, Crew: 100, Max: 100),
            new Spec("noCrew", 2, 100, Crew: 0, Max: 0)
        );
        Assert.IsFalse(world.Transport(u["s"], u["full"]));
        Assert.IsFalse(world.Transport(u["s"], u["noCrew"]));
        Assert.IsFalse(world.Transport(u["s"], u["s"]));
    }

    [TestMethod]
    public void TheOrderRefusesADestroyedTarget()
    {
        var (world, u) = Build(null, new Spec("s", 1, 0), new Spec("t", 2, 100));
        world.State(u["t"])!.Dying = true;
        Assert.IsFalse(world.Transport(u["s"], u["t"]));
    }

    [TestMethod]
    public void TheCursorTestRefusesAHostileTargetWithShieldsAboveOneHundredth()
    {
        var (world, u) = Build(null, new Spec("s", 1, 0), new Spec("t", 2, 100, Shields: 100));
        var (s, t) = (world.State(u["s"])!, world.State(u["t"])!);
        Assert.IsFalse(CombatRules.CanTransportTo(s, t, 100, 500));
        t.Shields = 0.01;
        Assert.IsTrue(CombatRules.CanTransportTo(s, t, 100, 500));
        t.Shields = 0.011;
        Assert.IsFalse(CombatRules.CanTransportTo(s, t, 100, 500));
    }

    [TestMethod]
    public void TheCursorTestChecksRangeAndTheSourceCrew()
    {
        var (world, u) = Build(null, new Spec("s", 1, 0), new Spec("t", 2, 100));
        var (s, t) = (world.State(u["s"])!, world.State(u["t"])!);
        Assert.IsTrue(CombatRules.CanTransportTo(s, t, 500, 500));
        Assert.IsFalse(CombatRules.CanTransportTo(s, t, 500.5, 500));
        s.Crew = 9;
        Assert.IsFalse(CombatRules.CanTransportTo(s, t, 100, 500));
    }

    // --- the cycle ---

    static readonly RaceValues Strong = Race with { BoardingStrength = 2 };
    static readonly RaceValues Weak = Race with { BoardingStrength = 0.5f };

    // The 1-based ticks in which `world` sends a party, over `ticks` ticks.
    static List<int> SendTicks(CombatWorld world, int ticks)
    {
        var sent = new List<int>();
        for (var i = 1; i <= ticks; i++)
        {
            if (world.Step(Tick).OfType<PartySent>().Any())
            {
                sent.Add(i);
            }
        }

        return sent;
    }

    [TestMethod]
    public void APartyLeavesEveryTransporterDelayStartingWithTheFirstTick()
    {
        var (world, u) = Build(null, new Spec("s", 1, 0), new Spec("t", 1, 100, Crew: 10));
        world.Transport(u["s"], u["t"]);
        CollectionAssert.AreEqual(new[] { 1, 51, 101 }, SendTicks(world, 120));
        Assert.AreEqual(85, CrewOf(world, u["s"]));
    }

    [TestMethod]
    public void APartyLandsOnAFriendlyTargetOnTheNextTick()
    {
        var (world, u) = Build(null, new Spec("s", 1, 0), new Spec("t", 1, 100, Crew: 10));
        world.Transport(u["s"], u["t"]);
        world.Step(Tick);
        Assert.AreEqual(10, CrewOf(world, u["t"]));
        world.Step(Tick);
        Assert.AreEqual(15, CrewOf(world, u["t"]));
    }

    [TestMethod]
    public void APartyLandsOnAHostileTargetAfterTheActionDelay()
    {
        var (world, u) = Build(
            new TransporterValues(500, 1, 1, 5),
            new Spec("s", 1, 0),
            new Spec("t", 2, 100, Crew: 10)
        );
        world.Transport(u["s"], u["t"]);
        Run(world, 1.0 - Tick); // 49 ticks: the party sent in tick 1 is still in flight
        Assert.AreEqual(10, CrewOf(world, u["t"]));
        var events = Run(world, 2 * Tick); // ticks 50 and 51: it lands in tick 51
        Assert.AreEqual(1, events.OfType<BoardingFight>().Count());
        Assert.AreEqual(5, CrewOf(world, u["t"]));
    }

    [TestMethod]
    public void TheTimingIsTheSameAtThirtyAndSixtyFramesPerSecond()
    {
        double[] Crews(int fps)
        {
            var (world, u) = Build(null, new Spec("s", 1, 0), new Spec("t", 2, 100, Crew: 60));
            world.Transport(u["s"], u["t"]);
            for (var i = 0; i < fps * 5 / 2; i++)
            {
                world.Step(1.0 / fps);
            }

            return [CrewOf(world, u["s"]), CrewOf(world, u["t"])];
        }

        CollectionAssert.AreEqual(Crews(60), Crews(30));
        Assert.AreEqual(85, Crews(60)[0]); // parties in ticks 1, 51, and 101 of 125
    }

    [TestMethod]
    public void TheTransportWorksForAnArmedSourceAndAStation()
    {
        var (world, u) = Build(
            null,
            new Spec("armed", 1, 0, Weapon: Weapon()),
            new Spec("t", 1, 100, Crew: 10)
        );
        world.Transport(u["armed"], u["t"]);
        Assert.AreEqual(1, Run(world, Tick).OfType<PartySent>().Count());
    }

    // --- the fight ---

    [TestMethod]
    public void AHostilePartyThatDoesNotOutnumberTheCrewDiesAndTheTargetLosesTheStrength()
    {
        var (world, u) = Build(null, new Spec("s", 1, 0), new Spec("t", 2, 100, Crew: 10));
        world.Transport(u["s"], u["t"]);
        Run(world, 1.02);
        Assert.AreEqual(5, CrewOf(world, u["t"]));
        Assert.AreEqual(2, world.State(u["t"])!.Team);
    }

    [TestMethod]
    public void APartyThatMatchesTheCrewExactlyDiesAndLeavesTheTargetDerelict()
    {
        // a * P = d * C: 2 * 5 = 1 * 10
        var (world, u) = Build(
            null,
            new Spec("s", 1, 0, Race: Strong),
            new Spec("t", 2, 100, Crew: 10)
        );
        world.Transport(u["s"], u["t"]);
        var events = Run(world, 1.02);
        var target = world.State(u["t"])!;
        Assert.AreEqual(0, events.OfType<UnitCaptured>().Count());
        Assert.AreEqual(2, target.Team);
        Assert.IsTrue(target.Derelict);
        Assert.AreEqual(0, target.Crew);
    }

    [TestMethod]
    public void AnOverwhelmedTargetChangesTeamAndRaceAndKeepsTheSurvivors()
    {
        // E = (2 / 1) * 5 = 10; survivors = (1 / 2) * (10 - 4) = 3
        var (world, u) = Build(
            null,
            new Spec("s", 1, 0, Race: Strong),
            new Spec("t", 2, 100, Crew: 4)
        );
        world.Transport(u["s"], u["t"]);
        var events = Run(world, 1.02);
        var target = world.State(u["t"])!;
        Assert.AreEqual(1, events.OfType<UnitCaptured>().Count(e => e.Unit == u["t"] && e.Team == 1));
        Assert.AreEqual(1, target.Team);
        Assert.AreSame(Strong, target.Race);
        Assert.AreEqual(3, target.Crew);
        Assert.IsFalse(target.Derelict);
    }

    [TestMethod]
    public void TheDefenderStrengthWeighsTheFight()
    {
        // a = 1, d = 2: E = 2.5, so a target of 2 crew falls and (2 / 1) * (2.5 - 2) = 1 boarder survives
        var (world, u) = Build(
            null,
            new Spec("s", 1, 0),
            new Spec("t", 2, 100, Crew: 2, Race: Strong)
        );
        world.Transport(u["s"], u["t"]);
        Run(world, 1.02);
        var target = world.State(u["t"])!;
        Assert.AreEqual(1, target.Team);
        Assert.AreEqual(1, target.Crew);
    }

    [TestMethod]
    public void TheSurvivorsAreCappedAtTheMaximumCrew()
    {
        var (world, u) = Build(
            null,
            new Spec("s", 1, 0, Race: Strong),
            new Spec("t", 2, 100, Crew: 1, Max: 2)
        );
        world.Transport(u["s"], u["t"]);
        Run(world, 1.02);
        Assert.AreEqual(2, world.State(u["t"])!.Crew);
    }

    [TestMethod]
    public void ACaptureWithFewerThanOneSurvivorLeavesTheUnitDerelictOnTheNewTeam()
    {
        // E = 5, C = 4.5: 0.5 survive
        var (world, u) = Build(null, new Spec("s", 1, 0), new Spec("t", 2, 100, Crew: 4.5f));
        world.Transport(u["s"], u["t"]);
        Run(world, 1.02);
        var target = world.State(u["t"])!;
        Assert.AreEqual(1, target.Team);
        Assert.IsTrue(target.Derelict);
        Assert.AreEqual(0, target.Crew);
    }

    [TestMethod]
    public void ACapturedUnitKeepsItsAlertAndItsOwnTransport()
    {
        // E = 4 * 5 = 20 against 12 crew
        var (world, u) = Build(
            null,
            new Spec("s", 1, 0, Race: Race with { BoardingStrength = 4 }),
            new Spec("t", 2, 100, Crew: 12, Max: 12, Weapon: Weapon()),
            new Spec("v", 2, 200, Crew: 50)
        );
        var target = world.State(u["t"])!;
        target.Alert = AlertStance.Yellow;
        world.Transport(u["s"], u["t"]); // sends in tick 1 and lands in tick 51
        world.Step(Tick);
        world.Transport(u["t"], u["v"]); // sends in tick 2, not in tick 51
        Run(world, 50 * Tick);
        Assert.AreEqual(1, target.Team);
        Assert.AreEqual(AlertStance.Yellow, target.Alert);
        Assert.IsTrue(target.Transporting);
    }

    [TestMethod]
    public void TheFormerTeammatesOfACapturedUnitAttackIt()
    {
        var (world, u) = Build(
            null,
            new Spec("s", 1, 0, Race: Strong),
            new Spec("t", 2, 100, Crew: 4, Shields: 100),
            new Spec("mate", 2, 110, Weapon: Weapon(range: 100), Shields: 100)
        );
        world.State(u["t"])!.Shields = 0; // the shields are down, so the transport may start
        world.Transport(u["s"], u["t"]);
        var before = Run(world, 1.0);
        Assert.AreEqual(0, before.OfType<ShotFired>().Count(e => e.Shot.Target.Unit == u["t"]));
        var after = Run(world, 0.5);
        Assert.IsTrue(after.OfType<ShotFired>().Any(e => e.Shot.Shooter.Unit == u["mate"] && e.Shot.Target.Unit == u["t"]));
    }

    // --- derelicts and friendly targets ---

    [TestMethod]
    public void APartyReachingAHostileDerelictCapturesItWithThePartyAsCrew()
    {
        var (world, u) = Build(null, new Spec("s", 1, 0, Race: Strong), new Spec("t", 2, 100, Crew: 0));
        world.Transport(u["s"], u["t"]);
        var events = Run(world, 1.02);
        var target = world.State(u["t"])!;
        Assert.AreEqual(1, events.OfType<UnitCaptured>().Count());
        Assert.AreEqual(0, events.OfType<BoardingFight>().Count());
        Assert.AreEqual(1, target.Team);
        Assert.AreSame(Strong, target.Race);
        Assert.AreEqual(5, target.Crew);
        Assert.IsFalse(target.Derelict);
    }

    [TestMethod]
    public void APartyOntoAnOwnTeamDerelictAddsCrewEndsItsDerelictStateAndSetsItsRace()
    {
        var (world, u) = Build(null, new Spec("s", 1, 0, Race: Strong), new Spec("t", 1, 100, Crew: 0));
        world.Transport(u["s"], u["t"]);
        Run(world, 2 * Tick);
        var target = world.State(u["t"])!;
        Assert.AreEqual(1, target.Team);
        Assert.AreSame(Strong, target.Race);
        Assert.AreEqual(5, target.Crew);
        Assert.IsFalse(target.Derelict);
    }

    [TestMethod]
    public void APartyOntoANeutralUnitOnlyAddsCrew()
    {
        var (world, u) = Build(
            null,
            new Spec("s", 1, 0, Race: Strong),
            new Spec("live", 0, 100, Crew: 10),
            new Spec("derelict", 0, 200, Crew: 0)
        );
        world.Transport(u["s"], u["live"]);
        Run(world, 2 * Tick);
        Assert.AreEqual(0, world.State(u["live"])!.Team);
        Assert.AreEqual(15, CrewOf(world, u["live"]));
        world.CancelTransport(u["s"]);
        world.Transport(u["s"], u["derelict"]);
        Run(world, 1.1); // the cycle timer of the first party runs out, and the next party lands a tick later
        var derelict = world.State(u["derelict"])!;
        Assert.AreEqual(0, derelict.Team);
        Assert.AreNotSame(Strong, derelict.Race);
        Assert.AreEqual(5, derelict.Crew);
        Assert.IsFalse(derelict.Derelict);
    }

    [TestMethod]
    public void AFriendlyTargetFillsToItsMaximumTheOverflowIsLostAndTheTransportStops()
    {
        var (world, u) = Build(null, new Spec("s", 1, 0), new Spec("t", 1, 100, Crew: 98));
        world.Transport(u["s"], u["t"]);
        var events = Run(world, 1.2);
        Assert.AreEqual(100, CrewOf(world, u["t"]));
        Assert.AreEqual(1, events.OfType<PartySent>().Count());
        Assert.AreEqual(1, events.OfType<TransportStopped>().Count());
        Assert.IsFalse(world.State(u["s"])!.Transporting);
        Assert.AreEqual(95, CrewOf(world, u["s"]));
    }

    [TestMethod]
    public void EveryHostileCycleHoldsTheTargetsShieldsDown()
    {
        var (world, u) = Build(null, new Spec("s", 1, 0), new Spec("t", 2, 100, Crew: 50, Shields: 100, ShieldDelay: 4));
        world.State(u["t"])!.Shields = 0;
        world.Transport(u["s"], u["t"]);
        world.Step(Tick);
        Assert.AreEqual(4, world.State(u["t"])!.ShieldDown);
    }

    [TestMethod]
    public void TwoRunsOfOneScriptEndInTheSameState()
    {
        double[] Run1()
        {
            var (world, u) = Build(null, new Spec("s", 1, 0, Race: Strong), new Spec("t", 2, 100, Crew: 12));
            world.Transport(u["s"], u["t"]);
            Run(world, 4);
            var t = world.State(u["t"])!;

            return [t.Crew, t.Team, CrewOf(world, u["s"])];
        }

        CollectionAssert.AreEqual(Run1(), Run1());
    }
}
