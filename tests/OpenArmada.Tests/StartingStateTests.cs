using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Assets;
using OpenArmada.Sandbox;

namespace OpenArmada.Tests;

// A layout object's starting state, applied to its unit in the combat world of a plan built from the layout;
// installs and layouts are built in code (INV-0001).
[TestClass]
public class StartingStateTests
{
    readonly TempInstall temp = new();

    [TestCleanup]
    public void CleanUp() => temp.CleanUp();

    const string Systems =
        "enginesHitPoints = 10\r\nenginesHitPercent = 10\r\nenginesRepairTime = 10\r\n"
        + "lifeSupportHitPoints = 10\r\nlifeSupportHitPercent = 10\r\nlifeSupportRepairTime = 10\r\n"
        + "weaponsHitPoints = 10\r\nweaponsHitPercent = 10\r\nweaponsRepairTime = 10\r\n"
        + "shieldGeneratorHitPoints = 10\r\nshieldGeneratorHitPercent = 10\r\nshieldGeneratorRepairTime = 10\r\n";
    const string Sensors = "sensorsHitPoints = 10\r\nsensorsHitPercent = 10\r\nsensorsRepairTime = 10\r\n";
    const string Ship =
        "physicsFile = \"p.odf\"\r\nrace = \"federation\"\r\nmaxHealth = 900\r\nshieldRate = 5\r\nshieldDelay = 3\r\n"
        + "maximumCrew = 120\r\nhullHitPercent = 50\r\n"
        + Systems;
    const string Race =
        "name = \"federation\"\r\ncrewYellowStatus = 0.4\r\ncrewRedStatus = 0.2\r\nweaponYellow = 1\r\n"
        + "weaponRed = 1\r\nrepairYellow = 1\r\nrepairRed = 1\r\nrepairStrength = 1\r\n";

    // A world of the layout's objects: "s" a ship with all five systems and 100 crew, "nosensors" one without
    // sensors, "nocrew" one whose class has no crew, "rock" an object that is no unit. Returns the world, the
    // placed objects in layout order, and the reports of the unit and combat plans.
    (CombatWorld World, PlacedObject[] Objects, string[] Reports) World(params string[] objects)
    {
        var install = new GameFileSystem(
            temp.MakeInstall(
                ("addon/s.odf", Ship + Sensors + "crewCost = 100\r\n"),
                ("addon/nosensors.odf", Ship + "crewCost = 100\r\n"),
                ("addon/nocrew.odf", Ship + Sensors + "crewCost = 0\r\n"),
                ("addon/rock.odf", "maxHealth = 1\r\n"),
                ("addon/p.odf", ""),
                ("addon/fed.odf", Race),
                ("addon/races.odf", "numberOfRaces = 1\r\nrace0 = \"fed.odf\"\r\n"),
                ("RTS_CFG.h", "float SHIELD_DAMAGE_PROTECTION = 1;\r\nint SYSTEM_HITPOINTS_BONUS = 2;\r\n")
            )
        );
        var read = Layout.Read(
            "l.jsonc",
            """{ "bounds": { "minX": 0, "maxX": 5000, "minZ": 0, "maxZ": 5000 }, "objects": [ """
                + string.Join(", ", objects)
                + " ] }"
        );
        Assert.IsNull(read.Problem);
        var plan = SandboxPlan.Build(read.Asset!, SandboxPlan.Definitions(install, read.Asset!), _ => true);
        var units = UnitPlan.Build(install, plan, _ => true);
        var combat = CombatPlan.Build(install, plan, units.Units);
        var world = new CombatWorld(
            new UnitPoses(plan.Objects),
            units.Units.Select(u => new CombatUnit
            {
                Unit = u.Object,
                Profile = combat.Profiles[u.Object],
                DefinitionName = u.Object.Class + ".odf",
                Radius = 0,
                Mobile = true,
            }),
            combat.ShieldDamageProtection,
            () => 0.5,
            (_, _, pose) => pose.Position,
            combat.SystemHitpointsBonus
        );

        return (world, plan.Objects.ToArray(), units.Reports.Concat(combat.Reports).Select(r => r.Text).ToArray());
    }

    static string Obj(string cls, string keys = "", float x = 1000) =>
        $$"""{ "class": "{{cls}}", "team": 1, "position": [{{x}}, 0, 1000]{{(keys.Length > 0 ? ", " + keys : "")}} }""";

    [TestMethod]
    public void AUnitStartsWithItsCrewShieldsAndDestroyedSystems()
    {
        var (world, objects, _) = World(
            Obj("s", "\"crew\": 0.5, \"shields\": 0.25, \"destroyedSystems\": [\"Engines\"]")
        );
        var s = world.State(objects[0])!;

        Assert.AreEqual(50, s.Crew);
        Assert.AreEqual(225, s.Shields);
        Assert.AreEqual(0, s.ShieldDown);
        Assert.AreEqual(0, s.Systems["Engines"].HitPoints);
        Assert.IsTrue(s.Systems["Engines"].Destroyed);
        Assert.AreEqual(0, s.Systems["Engines"].HitPercent);
        // the engines' 10 percent went evenly to the four working systems
        Assert.AreEqual(12.5, s.Systems["Sensors"].HitPercent, 1e-9);
        Assert.IsTrue(s.Immobile);
        Assert.IsFalse(s.Derelict);

        var dump = new PlayState(new UnitNames(objects), u => new UnitPoses(objects).Pose(u), world.Units).Dump();
        StringAssert.Contains(dump, "shields=225.00 crew=50.00 Engines=0.00/12.00/destroyed");
        StringAssert.Contains(dump, "immobile=true");
    }

    [TestMethod]
    public void AStartDestroyedSystemRepairsItselfToItsMaximum()
    {
        var (world, objects, _) = World(Obj("s", "\"destroyedSystems\": [\"Sensors\"]"));

        Assert.IsTrue(world.State(objects[0])!.Systems["Sensors"].Destroyed);

        world.Step(30);

        var sensors = world.State(objects[0])!.Systems["Sensors"];
        Assert.IsFalse(sensors.Destroyed);
        Assert.AreEqual(sensors.Max, sensors.HitPoints);
    }

    [TestMethod]
    [DataRow("\"crew\": 0.37")]
    [DataRow("\"crew\": 0.37, \"destroyedSystems\": [\"LifeSupport\", \"Weapons\"]")]
    public void TheStartingCrewIsTheFractionOfTheCrewCostExactly(string keys)
    {
        var (world, objects, _) = World(Obj("s", keys));

        Assert.AreEqual(0.37 * 100, world.State(objects[0])!.Crew);
    }

    [TestMethod]
    [DataRow("\"crew\": 0.005")]
    [DataRow("\"crew\": 0")]
    public void FewerThanOneCrewMemberStartsTheUnitDerelictWithShieldsDown(string keys)
    {
        var (world, objects, reports) = World(Obj("s", keys));
        var s = world.State(objects[0])!;

        Assert.IsTrue(s.Derelict);
        Assert.AreEqual(0, s.Shields);
        Assert.AreEqual(0, s.Crew);
        Assert.AreEqual(0, reports.Length, string.Join("\n", reports));
    }

    [TestMethod]
    public void ExactlyOneCrewMemberDoesNotStartTheUnitDerelict()
    {
        var (world, objects, _) = World(Obj("s", "\"crew\": 0.01"));

        Assert.IsFalse(world.State(objects[0])!.Derelict);
        Assert.AreEqual(900, world.State(objects[0])!.Shields);
    }

    [TestMethod]
    [DataRow("s", "\"crew\": 0, \"shields\": 0.5")]
    [DataRow("nocrew", "\"shields\": 0.5")]
    public void ShieldsAboveZeroOnAUnitThatStartsDerelictAreOneWarningNamingTheObject(string cls, string keys)
    {
        var (world, objects, reports) = World(Obj(cls, keys));

        Assert.AreEqual(0, world.State(objects[0])!.Shields);
        Assert.AreEqual(1, reports.Length, string.Join("\n", reports));
        StringAssert.Contains(reports[0], $"{cls} ({cls})");
    }

    [TestMethod]
    public void ShieldsOfZeroRegainWithAWorkingGenerator()
    {
        var (world, objects, _) = World(Obj("s", "\"shields\": 0"));

        world.Step(1);

        Assert.AreEqual(5, world.State(objects[0])!.Shields, 1e-6);
    }

    [TestMethod]
    public void AStartDestroyedShieldGeneratorLeavesTheStartingShieldsUnchanged()
    {
        var (world, objects, _) = World(Obj("s", "\"shields\": 0.5, \"destroyedSystems\": [\"ShieldGenerator\"]"));

        world.Step(1);

        Assert.AreEqual(450, world.State(objects[0])!.Shields);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("\"crew\": 1, \"shields\": 1, \"destroyedSystems\": []")]
    public void AFullOrAbsentStartingStateGivesAFullStrengthUnit(string keys)
    {
        var (world, objects, reports) = World(Obj("s", keys));
        var s = world.State(objects[0])!;

        Assert.AreEqual(100, s.Crew);
        Assert.AreEqual(900, s.Shields);
        Assert.IsFalse(s.Derelict);
        Assert.IsTrue(s.Systems.Values.All(x => !x.Destroyed && x.HitPoints == x.Max && x.HitPercent == 10));
        Assert.AreEqual(0, reports.Length, string.Join("\n", reports));
    }

    [TestMethod]
    public void ASystemTheClassLacksIsOneGapNamingTheObjectAndTheRestOpens()
    {
        var (world, objects, reports) = World(
            Obj("nosensors", "\"destroyedSystems\": [\"Sensors\", \"Engines\"]"),
            Obj("s", x: 1100)
        );

        Assert.AreEqual(1, reports.Length, string.Join("\n", reports));
        StringAssert.Contains(reports[0], "nosensors (nosensors)");
        Assert.IsTrue(world.State(objects[0])!.Systems["Engines"].Destroyed);
        Assert.IsNotNull(world.State(objects[1]));
    }

    [TestMethod]
    public void KeysOnAnObjectWithoutCombatStateAreOneGapNamingTheObjectAndTheRestOpens()
    {
        var (world, objects, reports) = World(Obj("rock", "\"crew\": 0.5"), Obj("s", x: 1100));

        Assert.AreEqual(1, reports.Length, string.Join("\n", reports));
        StringAssert.Contains(reports[0], "rock (rock)");
        Assert.IsNull(world.State(objects[0]));
        Assert.IsNotNull(world.State(objects[1]));
    }
}
