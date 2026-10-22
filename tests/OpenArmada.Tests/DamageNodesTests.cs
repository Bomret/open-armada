using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Formats;
using OpenArmada.Sandbox;
using static OpenArmada.Tests.CombatFixtures;

namespace OpenArmada.Tests;

[TestClass]
public class DamageNodesTests
{
    static Sod.Node Node(string name, string parent) => new() { Name = name, Parent = parent };

    static Sod Model(params Sod.Node[] nodes) => new() { Version = 1.8f, Nodes = nodes };

    // A ship model with a crew chain of six nodes and one node per system, as the stock models name them.
    static Sod Ship(string shieldNode = "shields") =>
        Model(
            Node("root", ""),
            Node("damage", "root"),
            Node("borg", "damage"),
            Node("borgfire", "borg"),
            Node("crew", "damage"),
            Node("crew1", "crew"),
            Node("crew2", "crew1"),
            Node("crew3", "crew2"),
            Node("crew4", "crew3"),
            Node("crew5", "crew4"),
            Node("target", "damage"),
            Node("fire", "target"),
            Node("engines", "damage"),
            Node("life", "damage"),
            Node("sensors", "damage"),
            Node(shieldNode, "damage")
        );

    // A unit of 100 maximum crew in the fixtures' race, whose crewYellowStatus is 0.5.
    static UnitCombatState Unit(float crewPercent = 100, RaceValues? race = null)
    {
        var state = State(
            Profile(
                crew: 100,
                maximumCrew: 100,
                race: race,
                systems:
                [
                    Sys(SystemNames.Engines),
                    Sys(SystemNames.LifeSupport),
                    Sys(SystemNames.Weapons),
                    Sys(SystemNames.ShieldGenerator),
                    Sys(SystemNames.Sensors),
                ]
            )
        );
        state.Crew = crewPercent;

        return state;
    }

    static void Destroy(UnitCombatState unit, string system) => unit.Systems[system].Destroyed = true;

    [TestMethod]
    public void AnUndamagedShipWithFullCrewShowsNoDamageNode() =>
        Assert.AreEqual(0, DamageNodes.Showing(Ship(), Unit()).Count);

    [TestMethod]
    public void TheCrewNodeShowsBelowTheYellowStatusAndEachNextNodeAtEveryFifthOfItsRatio()
    {
        // crew/maximum/yellow: 35% over 0.5 is 0.7, below 1.0 and 0.8, not below 0.6
        CollectionAssert.AreEqual(new[] { "crew", "crew1" }, DamageNodes.Showing(Ship(), Unit(35)).ToArray());
        // 45% over 0.5 is 0.9: only the first node
        CollectionAssert.AreEqual(new[] { "crew" }, DamageNodes.Showing(Ship(), Unit(45)).ToArray());
    }

    [TestMethod]
    public void AtTheYellowStatusAndAboveNoCrewNodeShows() =>
        Assert.AreEqual(0, DamageNodes.Showing(Ship(), Unit(50)).Count);

    [TestMethod]
    public void NoMoreThanFiveCrewNodesShowHoweverLowTheCrew()
    {
        // 5% over 0.5 is 0.1: thresholds 1.0, 0.8, 0.6, 0.4, 0.2 hold, so five nodes show and the sixth never
        CollectionAssert.AreEqual(
            new[] { "crew", "crew1", "crew2", "crew3", "crew4" },
            DamageNodes.Showing(Ship(), Unit(5)).ToArray()
        );
        Assert.AreEqual(5, DamageNodes.Showing(Ship(), Unit(0)).Count);
    }

    [TestMethod]
    public void RecrewingAboveAThresholdHidesThatCrewNodeAgain()
    {
        var unit = Unit(35);
        Assert.AreEqual(2, DamageNodes.Showing(Ship(), unit).Count);

        unit.Crew = 45;

        CollectionAssert.AreEqual(new[] { "crew" }, DamageNodes.Showing(Ship(), unit).ToArray());
    }

    [TestMethod]
    public void ADestroyedWeaponsSystemShowsTheTargetSubtreeUntilItIsRepaired()
    {
        var unit = Unit();
        Destroy(unit, SystemNames.Weapons);
        CollectionAssert.AreEqual(new[] { "target", "fire" }, DamageNodes.Showing(Ship(), unit).ToArray());

        unit.Systems[SystemNames.Weapons].Destroyed = false;

        Assert.AreEqual(0, DamageNodes.Showing(Ship(), unit).Count);
    }

    [TestMethod]
    public void EachSystemShowsItsOwnNode()
    {
        foreach (
            var (system, node) in new[]
            {
                (SystemNames.Engines, "engines"),
                (SystemNames.LifeSupport, "life"),
                (SystemNames.Sensors, "sensors"),
                (SystemNames.ShieldGenerator, "shields"),
            }
        )
        {
            var unit = Unit();
            Destroy(unit, system);

            CollectionAssert.AreEqual(new[] { node }, DamageNodes.Showing(Ship(), unit).ToArray(), system);
        }
    }

    [TestMethod]
    public void ANodeNamedShieldIsNotTheShieldGeneratorNode()
    {
        var unit = Unit();
        Destroy(unit, SystemNames.ShieldGenerator);

        Assert.AreEqual(0, DamageNodes.Showing(Ship("shield"), unit).Count);
    }

    [TestMethod]
    public void NamesMatchWholeAndIgnoreCase()
    {
        var model = Model(
            Node("root", ""),
            Node("Damage", "root"),
            Node("ENGINES", "Damage"),
            Node("engines2", "Damage")
        );
        var unit = Unit();
        Destroy(unit, SystemNames.Engines);

        CollectionAssert.AreEqual(new[] { "ENGINES" }, DamageNodes.Showing(model, unit).ToArray());
    }

    [TestMethod]
    public void AModelWithoutADamageGroupShowsNothing()
    {
        var unit = Unit(0);
        Destroy(unit, SystemNames.Engines);

        Assert.AreEqual(0, DamageNodes.Showing(Model(Node("root", ""), Node("crew", "root")), unit).Count);
        Assert.AreEqual(0, DamageNodes.Showing(null, unit).Count);
    }

    [TestMethod]
    public void AModelWithoutACrewNodeShowsOnlyItsSystemNodes()
    {
        var model = Model(Node("root", ""), Node("damage", "root"), Node("engines", "damage"));
        var unit = Unit(0);
        Destroy(unit, SystemNames.Engines);

        CollectionAssert.AreEqual(new[] { "engines" }, DamageNodes.Showing(model, unit).ToArray());
    }

    [TestMethod]
    public void ARaceWithACrewYellowStatusOfZeroOrLessShowsNoCrewNode()
    {
        foreach (var yellow in new[] { 0f, -0.5f })
        {
            var race = Race with { CrewYellowStatus = yellow };

            Assert.AreEqual(0, DamageNodes.Showing(Ship(), Unit(0, race)).Count, $"{yellow}");
        }
    }

    [TestMethod]
    public void AUnitWhoseMaximumCrewResolvesToZeroShowsNoCrewNode()
    {
        // 0x467803: a maximum crew of 0 (explicit, or from a crewCost of 0 with no maximumCrew) makes the numerator 1,
        // so r is 1 / crewYellowStatus (2 here), never below 1. The crew pool runs on the stand-in; it can be empty
        // and still show no node.
        var crewless = State(Unit().Profile with { CrewCost = 1, MaximumCrew = 1, DefinedMaximumCrew = 0 });
        crewless.Crew = 0;

        Assert.AreEqual(0, DamageNodes.Showing(Ship(), crewless).Count);
    }

    [TestMethod]
    public void ADerelictWithAMaximumCrewStillShowsByCrewOverMaximumCrew()
    {
        var unit = Unit(0);
        unit.Derelict = true;

        Assert.AreEqual(5, DamageNodes.Showing(Ship(), unit).Count);
    }

    [TestMethod]
    public void AChainThatLoopsStopsAtTheFirstRepeat()
    {
        // crew -> crew1 -> a second node named crew, whose first child by name is crew1 again
        var model = Model(
            Node("root", ""),
            Node("damage", "root"),
            Node("crew", "damage"),
            Node("crew1", "crew"),
            Node("crew", "crew1")
        );

        CollectionAssert.AreEqual(new[] { "crew", "crew1", "crew" }, DamageNodes.Showing(model, Unit(0)).ToArray());
    }

    [TestMethod]
    public void ANodeIsTheChildOfTheFirstNodeOfItsParentNameOnlyAsTheBuilderHangsIt()
    {
        // `kid` names "dup", and the first node named dup sits under `target`: the builder hangs kid there, so the
        // second dup, under the destroyed engines, shows without it.
        var model = Model(
            Node("root", ""),
            Node("damage", "root"),
            Node("target", "damage"),
            Node("dup", "target"),
            Node("engines", "damage"),
            Node("dup", "engines"),
            Node("kid", "dup")
        );
        var unit = Unit();
        Destroy(unit, SystemNames.Engines);

        CollectionAssert.AreEqual(new[] { "engines", "dup" }, DamageNodes.Showing(model, unit).ToArray());
    }

    [TestMethod]
    public void TheBorgNodeNeverShowsEvenWithEverySystemDestroyedAndNoCrew()
    {
        var unit = Unit(0);
        foreach (var system in unit.Systems.Keys)
        {
            Destroy(unit, system);
        }

        var showing = DamageNodes.Showing(Ship(), unit).ToArray();

        Assert.IsTrue(showing.Length > 5, "the other nodes show");
        CollectionAssert.DoesNotContain(showing, "borg");
        CollectionAssert.DoesNotContain(showing, "borgfire");
    }

    [TestMethod]
    public void TwoNodesOfOneNameUnderDifferentSystemNodesAreToldApart()
    {
        var targetFire = Node("fire", "target");
        var engineFire = Node("fire", "engines");
        var model = Model(
            Node("root", ""),
            Node("damage", "root"),
            Node("target", "damage"),
            targetFire,
            Node("engines", "damage"),
            engineFire
        );
        var unit = Unit();
        Destroy(unit, SystemNames.Weapons);

        var showing = DamageNodes.ShowingNodes(model, unit);

        Assert.IsTrue(showing.Contains(targetFire));
        Assert.IsFalse(showing.Contains(engineFire));
    }

    [TestMethod]
    public void TheDecisionIsTheSameOnEveryCall() =>
        CollectionAssert.AreEqual(
            DamageNodes.Showing(Ship(), Unit(5)).ToArray(),
            DamageNodes.Showing(Ship(), Unit(5)).ToArray()
        );
}
