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
        WeaponProfile? Weapon = null,
        float Radius = 0,
        bool Mobile = false,
        string RaceName = ""
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
                    race: s.Race,
                    raceName: s.RaceName
                ),
                DefinitionName = s.Label + ".odf",
                Radius = s.Radius,
                Mobile = s.Mobile,
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
        var (world, u) = Build(
            null,
            new Spec("nine", 1, 0, Crew: 9),
            new Spec("ten", 1, 0, Crew: 10),
            new Spec("t", 2, 100)
        );
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
        Assert.AreEqual(1, events.OfType<TransportOrdered>().Count(), "the order's voice line plays");
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
    public void TheOrderRefusesAHostileTargetWithShieldsAboveOneHundredth()
    {
        var (world, u) = Build(null, new Spec("s", 1, 0), new Spec("t", 2, 100, Shields: 100));
        var (s, t) = (world.State(u["s"])!, world.State(u["t"])!);
        Assert.IsFalse(CombatRules.TransportOrderOk(s, t));
        t.Shields = 0.01;
        Assert.IsTrue(CombatRules.TransportOrderOk(s, t));
        t.Shields = 0.011;
        Assert.IsFalse(CombatRules.TransportOrderOk(s, t));
    }

    [TestMethod]
    public void AClickOnAShieldedHostileGivesNoOrderAndChangesNoUnit()
    {
        var (world, u) = Build(null, new Spec("s", 1, 0), new Spec("t", 2, 100, Shields: 100));
        Assert.IsFalse(world.Transport(u["s"], u["t"]));
        var events = world.Step(Tick);
        Assert.AreEqual(0, events.OfType<TransportStarted>().Count());
        Assert.AreEqual(0, events.OfType<TransportOrdered>().Count());
        Assert.IsFalse(world.State(u["s"])!.Transporting);
        Assert.AreEqual(0, world.State(u["t"])!.BoardedBy.Count);
    }

    [TestMethod]
    public void TheOrderAcceptsAShieldedDerelict()
    {
        var (world, u) = Build(null, new Spec("s", 1, 0), new Spec("t", 2, 100, Crew: 0, Shields: 100));
        Assert.IsTrue(world.Transport(u["s"], u["t"]));
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
    public void TheTransportWorksForAnArmedSourceAnUnarmedMobileShipAndAStationAndOnAMobileShipTarget()
    {
        var (world, u) = Build(
            null,
            new Spec("armed", 1, 0, Weapon: Weapon()),
            new Spec("ship", 1, 0, Mobile: true),
            new Spec("station", 1, 0, Mobile: false),
            new Spec("t", 1, 100, Crew: 10),
            new Spec("shipTarget", 1, 100, Crew: 10, Mobile: true)
        );
        foreach (var source in new[] { "armed", "ship", "station" })
        {
            Assert.IsTrue(world.Transport(u[source], u["t"]), source);
        }

        Assert.IsTrue(world.Transport(u["station"], u["shipTarget"]));
        Assert.AreEqual(3, Run(world, Tick).OfType<PartySent>().Count());
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
        var (world, u) = Build(null, new Spec("s", 1, 0, Race: Strong), new Spec("t", 2, 100, Crew: 10));
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
        var (world, u) = Build(null, new Spec("s", 1, 0, Race: Strong), new Spec("t", 2, 100, Crew: 4));
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
        var (world, u) = Build(null, new Spec("s", 1, 0), new Spec("t", 2, 100, Crew: 2, Race: Strong));
        world.Transport(u["s"], u["t"]);
        Run(world, 1.02);
        var target = world.State(u["t"])!;
        Assert.AreEqual(1, target.Team);
        Assert.AreEqual(1, target.Crew);
    }

    [TestMethod]
    public void TheSurvivorsAreCappedAtTheMaximumCrew()
    {
        var (world, u) = Build(null, new Spec("s", 1, 0, Race: Strong), new Spec("t", 2, 100, Crew: 1, Max: 2));
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
        // E = 12 * 5 = 60 against the 35 crew left after its own party; at 15 or less (maximum 40, red status 0.25) its red-status stop would end it
        var (world, u) = Build(
            null,
            new Spec("s", 1, 0, Race: Race with { BoardingStrength = 12 }),
            new Spec("t", 2, 100, Crew: 40, Max: 40, Weapon: Weapon()),
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
        Assert.IsTrue(
            after.OfType<ShotFired>().Any(e => e.Shot.Shooter.Unit == u["mate"] && e.Shot.Target.Unit == u["t"])
        );
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
    public void ACapturedUnitTakesTheCaptorsRaceNameAndKeepsItsOwnDefinition()
    {
        var (world, u) = Build(
            null,
            new Spec("s", 1, 0, Race: Strong, RaceName: "fed"),
            new Spec("t", 2, 100, Crew: 0, Max: 80, RaceName: "klingon")
        );
        world.Transport(u["s"], u["t"]);
        Run(world, 1.02);
        var target = world.State(u["t"])!;
        Assert.AreEqual("fed", target.RaceName);
        Assert.AreEqual(80, target.Profile.MaximumCrew);
        Assert.AreEqual("klingon", target.Profile.RaceName);
    }

    [TestMethod]
    public void ARecapturedUnitTakesTheNewCaptorsRaceName()
    {
        var (world, u) = Build(
            null,
            new Spec("b", 3, 0, Race: Strong, RaceName: "borg"),
            new Spec("f", 1, 200, Race: Strong, RaceName: "fed"),
            new Spec("t", 2, 100, Crew: 0, RaceName: "klingon")
        );
        world.Transport(u["b"], u["t"]);
        Run(world, 1.02);
        Assert.AreEqual("borg", world.State(u["t"])!.RaceName);
        world.Transport(u["f"], u["t"]);
        Run(world, 1.02);
        Assert.AreEqual("fed", world.State(u["t"])!.RaceName);
    }

    [TestMethod]
    public void APartyOntoAnOwnTeamDerelictSetsItsRaceName()
    {
        var (world, u) = Build(
            null,
            new Spec("s", 1, 0, Race: Strong, RaceName: "fed"),
            new Spec("t", 1, 100, Crew: 0, RaceName: "klingon")
        );
        world.Transport(u["s"], u["t"]);
        Run(world, 2 * Tick);
        Assert.AreEqual("fed", world.State(u["t"])!.RaceName);
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
        var (world, u) = Build(
            null,
            new Spec("s", 1, 0),
            new Spec("t", 2, 100, Crew: 50, Shields: 100, ShieldDelay: 4)
        );
        world.State(u["t"])!.Shields = 0;
        world.Transport(u["s"], u["t"]);
        world.Step(Tick);
        Assert.AreEqual(4, world.State(u["t"])!.ShieldDown);
    }

    [TestMethod]
    public void TwoRunsOfOneScriptEndWithTheSameStateDump()
    {
        string Run1()
        {
            var (world, u) = Build(null, new Spec("s", 1, 0, Race: Strong), new Spec("t", 2, 100, Crew: 12));
            world.Transport(u["s"], u["t"]);
            Run(world, 4);

            return new PlayState(new UnitNames(u.Values), new UnitPoses(u.Values).Pose, world.Units).Dump();
        }

        var first = Run1();
        Assert.AreEqual(first, Run1());
        StringAssert.Contains(first, "t team=1", "the script ends in a capture, so the dump is not a trivial one");
    }

    // --- the stop conditions ---

    static bool Stopped(IEnumerable<CombatEvent> events, PlacedObject source) =>
        events.OfType<TransportStopped>().Any(e => e.Source == source);

    [TestMethod]
    public void ATargetOutOfRangeCenterToCenterTakesNoPartyAndTheTransportStopsAtTheFirstCycle()
    {
        // the surfaces (radius 400 each) are within range; the centers, 600 apart, are not
        var (world, u) = Build(null, new Spec("s", 1, 0, Radius: 400), new Spec("t", 1, 600, Crew: 10, Radius: 400));
        Assert.IsTrue(world.Transport(u["s"], u["t"]));
        var events = Run(world, Tick);
        Assert.AreEqual(0, events.OfType<PartySent>().Count());
        Assert.IsTrue(Stopped(events, u["s"]));
    }

    [TestMethod]
    public void ATargetAtExactlyTheTransporterRangeTakesAParty()
    {
        var (world, u) = Build(null, new Spec("s", 1, 0), new Spec("t", 1, 500, Crew: 10));
        world.Transport(u["s"], u["t"]);
        Assert.AreEqual(1, Run(world, Tick).OfType<PartySent>().Count());
    }

    [TestMethod]
    public void AHostileTargetWithShieldsAboveOnePercentOfItsMaximumTakesNoParty()
    {
        int Parties(float shields)
        {
            var (world, u) = Build(null, new Spec("s", 1, 0), new Spec("t", 2, 100, Shields: 100));
            world.State(u["t"])!.Shields = 0; // the order refuses a raised shield; the cycle tests the fraction
            world.Transport(u["s"], u["t"]);
            world.State(u["t"])!.Shields = shields;

            return Run(world, Tick).OfType<PartySent>().Count();
        }

        Assert.AreEqual(1, Parties(0.5f));
        Assert.AreEqual(1, Parties(1f));
        Assert.AreEqual(0, Parties(2f));
    }

    [TestMethod]
    public void ATargetWithoutAShieldMaximumIsAlwaysOpen()
    {
        var (world, u) = Build(null, new Spec("s", 1, 0), new Spec("t", 2, 100, Shields: 0));
        world.Transport(u["s"], u["t"]);
        Assert.AreEqual(1, Run(world, Tick).OfType<PartySent>().Count());
    }

    [TestMethod]
    public void ADestroyedTargetEndsTheTransportAtTheNextCycle()
    {
        var (world, u) = Build(null, new Spec("s", 1, 0), new Spec("t", 1, 100, Crew: 10));
        world.Transport(u["s"], u["t"]);
        Run(world, Tick);
        world.State(u["t"])!.Destroyed = true;
        var events = Run(world, 1.0);
        Assert.IsTrue(Stopped(events, u["s"]));
        Assert.AreEqual(0, events.OfType<PartySent>().Count());
    }

    [TestMethod]
    public void TheTransportStopsWhenTheNextPartyWouldTakeTheSourceToRedStatus()
    {
        // maximum 100, red status 0.25: 40 -> 35 -> 30; the next party would leave 25, so it stops with 30
        var (world, u) = Build(null, new Spec("s", 1, 0, Crew: 40), new Spec("t", 1, 100, Crew: 10));
        world.Transport(u["s"], u["t"]);
        var events = Run(world, 5);
        Assert.AreEqual(2, events.OfType<PartySent>().Count());
        Assert.IsTrue(Stopped(events, u["s"]));
        Assert.AreEqual(30, CrewOf(world, u["s"]));
    }

    [TestMethod]
    public void ASourceJustAboveTheRedStatusLineSendsOnePartyAndStopsAtTheLine()
    {
        // maximum 100, red status 0.25, 30 crew: one party leaves 25, the next would leave 20, so it stops with 25
        var (world, u) = Build(null, new Spec("s", 1, 0, Crew: 30), new Spec("t", 1, 100, Crew: 10));
        world.Transport(u["s"], u["t"]);
        var events = Run(world, 5);
        Assert.AreEqual(1, events.OfType<PartySent>().Count());
        Assert.IsTrue(Stopped(events, u["s"]));
        Assert.AreEqual(25, CrewOf(world, u["s"]));
    }

    [TestMethod]
    public void ASmallSourceStopsAfterOnePartyWithOneOfTheNextPartysCrewLeft()
    {
        // maximum 20, red status 0.2, 10 crew: one party leaves 5; the next would leave 0, which is at red
        var red = Race with
        {
            CrewRedStatus = 0.2f,
        };
        var (world, u) = Build(
            null,
            new Spec("s", 1, 0, Crew: 10, Max: 20, Race: red),
            new Spec("t", 1, 100, Crew: 10)
        );
        world.Transport(u["s"], u["t"]);
        var events = Run(world, 5);
        Assert.AreEqual(1, events.OfType<PartySent>().Count());
        Assert.IsTrue(Stopped(events, u["s"]));
        Assert.AreEqual(5, CrewOf(world, u["s"]));
    }

    [TestMethod]
    public void ADerelictOrDyingSourceEndsItsTransport()
    {
        foreach (var make in new Action<UnitCombatState>[] { s => s.Derelict = true, s => s.Dying = true })
        {
            var (world, u) = Build(null, new Spec("s", 1, 0), new Spec("t", 1, 100, Crew: 10));
            world.Transport(u["s"], u["t"]);
            Run(world, Tick);
            make(world.State(u["s"])!);
            var events = Run(world, 2);
            Assert.IsTrue(Stopped(events, u["s"]));
            Assert.AreEqual(0, events.OfType<PartySent>().Count());
        }
    }

    [TestMethod]
    public void ADestroyedSourceIsReportedStopped()
    {
        var (world, u) = Build(null, new Spec("s", 1, 0), new Spec("t", 1, 100, Crew: 10));
        world.Transport(u["s"], u["t"]);
        var s = world.State(u["s"])!;
        s.Dying = true; // the dying time is 0: the unit leaves in the next tick
        s.DyingRemaining = 0;
        Assert.IsTrue(Stopped(Run(world, Tick), u["s"]));
    }

    [TestMethod]
    public void TheOrderAndTheCancelAreReportedAsStartAndStop()
    {
        var (world, u) = Build(null, new Spec("s", 1, 0), new Spec("t", 1, 100, Crew: 10));
        world.Transport(u["s"], u["t"]);
        var started = world.Step(Tick).OfType<TransportStarted>().Single();
        Assert.AreEqual((u["s"], u["t"]), (started.Source, started.Target));
        world.CancelTransport(u["s"]);
        var events = Run(world, 2);
        Assert.IsTrue(Stopped(events, u["s"]));
        Assert.AreEqual(0, events.OfType<PartySent>().Count());
        Assert.IsFalse(world.State(u["s"])!.Transporting);
    }

    [TestMethod]
    public void ARepeatOrderOnTheCurrentTargetReportsNoStopAndNoSecondStart()
    {
        var (world, u) = Build(null, new Spec("s", 1, 0), new Spec("t", 1, 100, Crew: 10));
        world.Transport(u["s"], u["t"]);
        world.Step(Tick);
        Assert.IsTrue(world.Transport(u["s"], u["t"]));
        var events = world.Step(Tick);
        Assert.AreEqual(0, events.OfType<TransportStopped>().Count());
        Assert.AreEqual(0, events.OfType<TransportStarted>().Count());
        Assert.IsTrue(world.State(u["s"])!.Transporting);
    }

    [TestMethod]
    public void AnOrderFromASourceAtTheTransporterMaximumEndsItsEarlierTransport()
    {
        var (world, u) = Build(
            new TransporterValues(500, 1, 1, 10),
            new Spec("s", 1, 0),
            new Spec("t1", 1, 100, Crew: 10),
            new Spec("t2", 1, 200, Crew: 10)
        );
        world.Transport(u["s"], u["t1"]);
        world.State(u["s"])!.Crew = 10;
        Assert.IsTrue(world.Transport(u["s"], u["t2"]));
        Assert.IsFalse(world.State(u["s"])!.Transporting);
        Assert.IsTrue(Stopped(world.Step(Tick), u["s"]));
    }

    // --- orders ---

    [TestMethod]
    public void AMoveAttackOrStopOrderLeavesTheTransportRunning()
    {
        var (world, u) = Build(null, new Spec("s", 1, 0, Weapon: Weapon()), new Spec("t", 2, 50, Crew: 50));
        var s = world.State(u["s"])!;
        world.Transport(u["s"], u["t"]);
        world.Move(u["s"], 5, 5);
        Assert.IsTrue(s.Transporting);
        Assert.IsTrue(world.Attack(u["s"], u["t"]));
        Assert.IsTrue(s.Transporting);
        world.ClearOrder(u["s"]);
        Assert.IsTrue(s.Transporting);
    }

    [TestMethod]
    public void ATransportOrderLeavesTheAttackAndMoveOrdersRunning()
    {
        var (world, u) = Build(null, new Spec("s", 1, 0, Weapon: Weapon()), new Spec("t", 2, 50, Crew: 50));
        var s = world.State(u["s"])!;
        world.Attack(u["s"], u["t"]);
        world.Transport(u["s"], u["t"]);
        Assert.AreSame(world.State(u["t"]), s.Order);
        world.Move(u["s"], 5, 5);
        world.Transport(u["s"], u["t"]);
        Assert.IsNotNull(s.PlayerMove);
    }

    // --- parties in flight ---

    [TestMethod]
    public void APartyInFlightLandsAfterItsSourceIsDestroyedWithTheTeamAndRaceItWasSentWith()
    {
        var (world, u) = Build(null, new Spec("s", 1, 0, Race: Strong), new Spec("t", 2, 100, Crew: 4));
        world.Transport(u["s"], u["t"]);
        world.Step(Tick);
        var s = world.State(u["s"])!;
        s.Dying = true;
        s.DyingRemaining = 0; // the source is gone before the party lands
        Run(world, 1.0);
        var target = world.State(u["t"])!;
        Assert.AreEqual(1, target.Team);
        Assert.AreSame(Strong, target.Race);
    }

    [TestMethod]
    public void APartyInFlightLandsAfterItsSourceIsCapturedWithTheTeamAndRaceItWasSentWith()
    {
        var (world, u) = Build(null, new Spec("s", 1, 0, Race: Strong), new Spec("t", 2, 100, Crew: 4));
        world.Transport(u["s"], u["t"]);
        world.Step(Tick);
        world.ChangeTeam(u["s"], 3, "", Weak);
        Run(world, 1.0);
        var target = world.State(u["t"])!;
        Assert.AreEqual(1, target.Team);
        Assert.AreSame(Strong, target.Race);
    }

    [TestMethod]
    public void AUnitCapturedWhileTransportingSendsItsNextPartyWithTheNewTeamAndRace()
    {
        var (world, u) = Build(
            null,
            new Spec("s", 1, 0, Race: Strong),
            new Spec("t", 2, 100, Crew: 12, Max: 12),
            new Spec("v", 2, 200, Crew: 0)
        );
        world.Transport(u["s"], u["t"]);
        world.Step(Tick);
        world.Transport(u["t"], u["v"]); // t sends in tick 2 (a hostile derelict lands 50 ticks later)
        world.ChangeTeam(u["t"], 3, "", Weak); // t changes team before its party is sent
        Run(world, 1.1);
        var v = world.State(u["v"])!;
        Assert.AreEqual(3, v.Team);
        Assert.AreSame(Weak, v.Race);
    }

    // --- the boarding block ---

    // s (team 1) boards t (team 2, no shields). mate (team 1) and other (team 3) are armed and stand in range of t.
    static (CombatWorld World, Dictionary<string, PlacedObject> Units) Boarding() =>
        Build(
            null,
            new Spec("s", 1, 0),
            new Spec("t", 2, 100, Crew: 100),
            new Spec("mate", 1, 110, Weapon: Weapon(range: 100)),
            new Spec("other", 3, 90, Weapon: Weapon(range: 100))
        );

    static int ShotsAt(IEnumerable<CombatEvent> events, PlacedObject shooter, PlacedObject target) =>
        events.OfType<ShotFired>().Count(e => e.Shot.Shooter.Unit == shooter && e.Shot.Target.Unit == target);

    [TestMethod]
    public void TheBoardingTeamNeitherAutoTargetsTheBoardedUnitNorKeepsAnOlderAttackOrderOnIt()
    {
        var (world, u) = Boarding();
        Assert.IsTrue(world.Attack(u["mate"], u["t"]));
        world.Transport(u["s"], u["t"]);
        var events = Run(world, 3);
        Assert.AreEqual(0, ShotsAt(events, u["mate"], u["t"]));
        Assert.IsNull(world.State(u["mate"])!.Order);
        Assert.IsTrue(ShotsAt(events, u["other"], u["t"]) > 0, "a team that is not boarding still shoots it");
    }

    [TestMethod]
    public void StoppingTheTransportDoesNotLiftTheBlock()
    {
        var (world, u) = Boarding();
        world.Transport(u["s"], u["t"]);
        world.Step(Tick);
        world.CancelTransport(u["s"]);
        Assert.AreEqual(0, ShotsAt(Run(world, 3), u["mate"], u["t"]));
    }

    [TestMethod]
    public void ANewAttackOrderOnTheUnitLiftsTheBlockForTheTeam()
    {
        var (world, u) = Boarding();
        world.Transport(u["s"], u["t"]);
        world.Step(Tick);
        Assert.IsTrue(world.Attack(u["mate"], u["t"]));
        Assert.IsTrue(ShotsAt(Run(world, 3), u["mate"], u["t"]) > 0);
    }

    [TestMethod]
    public void AChangeOfTheUnitsTeamLiftsTheBlock()
    {
        var (world, u) = Boarding();
        world.State(u["other"])!.Destroyed = true; // mate holds no other pick, so only the block can keep it off t
        world.Transport(u["s"], u["t"]);
        world.Step(Tick);
        world.ChangeTeam(u["t"], 4, "", CombatFixtures.Race);
        Assert.IsTrue(ShotsAt(Run(world, 3), u["mate"], u["t"]) > 0);
    }

    [TestMethod]
    public void BoardingAUnitOfTheOwnTeamBlocksNothing()
    {
        var (world, u) = Build(
            null,
            new Spec("s", 1, 0),
            new Spec("t", 1, 100, Crew: 10),
            new Spec("foe", 2, 110, Weapon: Weapon(range: 100))
        );
        world.Transport(u["s"], u["t"]);
        Assert.IsTrue(ShotsAt(Run(world, 3), u["foe"], u["t"]) > 0);
        Assert.AreEqual(0, world.State(u["t"])!.BoardedBy.Count);
    }
}
