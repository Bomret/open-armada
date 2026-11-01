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
        RaceValues? Race = null
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
}
