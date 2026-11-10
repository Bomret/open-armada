using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Sandbox;
using static OpenArmada.Tests.CombatFixtures;

namespace OpenArmada.Tests;

// The crew pool fill of the starbases (3.5 crew per second at the race's rate, times its planet modifier 1.5) over a
// combat world built in code.
[TestClass]
public class CrewPoolFillTests
{
    static readonly RaceValues Borg = Race with { CrewAccumulationRate = 3.5f, PlanetCrewAccumulationModifier = 1.5f };

    static readonly RaceValues Klingon = Race with { CrewAccumulationRate = 2, PlanetCrewAccumulationModifier = 1 };

    sealed record Spec(PlacedObject Object, CombatProfile Profile, bool Starbase);

    static Spec Unit(string label, int team, RaceValues race, bool starbase) =>
        new(Placed(label, team, 0), Profile(race: race), starbase);

    static CombatWorld World(double cap, params Spec[] specs) =>
        new(
            new UnitPoses(specs.Select(s => s.Object)),
            specs.Select(s => new CombatUnit
            {
                Unit = s.Object,
                Profile = s.Profile,
                DefinitionName = s.Object.Label + ".odf",
                Radius = 0,
                Mobile = false,
                Starbase = s.Starbase,
            }),
            1,
            () => 0,
            (_, _, pose) => pose.Position
        )
        {
            CrewPoolCap = cap,
        };

    static double Pool(CombatWorld world, int team) => world.CrewPools.TryGetValue(team, out var pool) ? pool.Crew : 0;

    [TestMethod]
    public void AStarbaseFillsItsTeamsPoolByTheRateTimesTheModifierEachSecond()
    {
        var world = World(99999, Unit("nexus", 1, Borg, true));
        world.Step(2);
        Assert.AreEqual(2 * 3.5 * 1.5, Pool(world, 1), 1e-6);
    }

    [TestMethod]
    public void TheFillNeverGoesAboveTheCap()
    {
        var world = World(10, Unit("nexus", 1, Borg, true));
        world.Step(5);
        Assert.AreEqual(10, Pool(world, 1), 1e-9);
    }

    [TestMethod]
    public void ATeamWithNoStarbaseGetsNoFill()
    {
        var world = World(99999, Unit("ship", 1, Borg, false), Unit("nexus", 2, Klingon, true));
        world.Step(3);
        Assert.AreEqual(0, Pool(world, 1));
        Assert.AreEqual(6, Pool(world, 2), 1e-6);
    }

    [TestMethod]
    public void ADestroyedOrDyingStarbaseStopsFilling()
    {
        var gone = Unit("gone", 1, Borg, true);
        var dying = Unit("dying", 2, Borg, true);
        var world = World(99999, gone, dying);
        world.State(gone.Object)!.Destroyed = true;
        world.State(dying.Object)!.Dying = true;
        world.Step(3);
        Assert.AreEqual(0, Pool(world, 1));
        Assert.AreEqual(0, Pool(world, 2));
    }

    [TestMethod]
    public void ACapturedStarbaseFillsItsNewTeamAtTheRaceItNowHas()
    {
        var station = Unit("station", 2, Klingon, true);
        var world = World(99999, station, Unit("cube", 1, Borg, false));
        world.Step(1);
        world.ChangeTeam(station.Object, 1, "borg", Borg);
        world.Step(1);
        Assert.AreEqual(2, Pool(world, 2), 1e-6);
        Assert.AreEqual(5.25, Pool(world, 1), 1e-6);
    }

    [TestMethod]
    public void TheFillOverTenSecondsIsTheSameAtThirtyAndSixtyTicksPerSecond()
    {
        var totals = new[] { 1 / 30.0, 1 / 60.0 }
            .Select(dt =>
            {
                var world = World(99999, Unit("nexus", 1, Borg, true));
                for (var i = 0; i < (int)System.Math.Round(10 / dt); i++)
                    world.Step(dt);

                return Pool(world, 1);
            })
            .ToArray();
        Assert.AreEqual(52.5, totals[0], 1e-6);
        Assert.AreEqual(52.5, totals[1], 1e-6);
    }
}
