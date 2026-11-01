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
        + "weaponRed = 1\r\nrepairYellow = 1\r\nrepairRed = 1\r\nrepairStrength = 1\r\nboardingStrength = 1\r\n";

    // A world of the layout's objects: "s" a ship with all five systems and 100 crew, "nosensors" one without
    // sensors, "nocrew" one whose class has no crew, "rock" an object that is no unit. Returns the world, the
    // placed objects in layout order, and the reports of the unit and combat plans.
    UnitPoses poses = null!; // the poses of the world the last World call built

    (CombatWorld World, PlacedObject[] Objects, string[] Reports) World(params string[] objects)
    {
        var install = new GameFileSystem(
            temp.MakeInstall(
                ("addon/s.odf", Ship + Sensors + "crewCost = 100\r\n"),
                ("addon/nosensors.odf", Ship + "crewCost = 100\r\n"),
                ("addon/nocrew.odf", Ship + Sensors + "crewCost = 0\r\n"),
                ("addon/station.odf", Ship + Sensors + "crewCost = 100\r\nis_starbase = 1\r\n"),
                ("addon/rock.odf", "maxHealth = 1\r\n"),
                ("addon/p.odf", "impulseSpeed = 10\r\nyawRotationRate = 1\r\ntooCloseToTurn = 40\r\n"),
                // a physics definition that cannot be read: a compressed archive entry, which the reader refuses
                (
                    "addon/x.zfs",
                    System.Text.Encoding.Latin1.GetString(
                        ZfsBuilder.Build(0, ZfsBuilder.M("q.odf", "impulseSpeed = 10", 2))
                    )
                ),
                ("addon/fastphys.odf", "impulseSpeed = 20\r\n"),
                ("addon/nophys.odf", Ship.Replace("p.odf", "q.odf") + Sensors + "crewCost = 100\r\n"),
                ("addon/fed.odf", Race),
                ("addon/races.odf", "numberOfRaces = 1\r\nrace0 = \"fed.odf\"\r\n"),
                (
                    "RTS_CFG.h",
                    "#include \"ART_CFG.h\"\r\nfloat SHIELD_DAMAGE_PROTECTION = 1;\r\nint SYSTEM_HITPOINTS_BONUS = 2;\r\nfloat FORMATION_SPACING = 100.0;\r\nfloat MAX_PREVENTION_ROTATION = 0.6;\r\nint OBSTACLE_AVOIDANCE = 0;\r\nfloat COLLISION_LOOK_AHEAD_TIME = 1.4;\r\nfloat COLLISION_SAFETY_MARGIN = 1.2;\r\nfloat TRANSPORTER_RANGE = 500.0;\r\nfloat TRANSPORTER_DELAY = 1.0;\r\nfloat TRANSPORTER_ACTION_DELAY = 1.0;\r\nint TRANSPORTER_MAX = 5;\r\n"
                ),
                (
                    "ART_CFG.h",
                    "int ST3D_CHUNK_MANAGER_MAX_CHUNKS = 3000;\r\nfloat ST3D_CHUNK_DENSITY = 1.0;\r\nfloat ST3D_CHUNK_MAX_LIFETIME = 3.0;\r\n"
                        + "float ST3D_CHUNK_MAX_ANGULAR_VELOCITY = 4.0;\r\nfloat ST3D_CHUNK_MAX_VELOCITY = 200.0;\r\n"
                )
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
        var combat = CombatPlan.Build(install, plan, units.Units, MovePlan.Build(plan, units.Units, install));
        poses = new UnitPoses(plan.Objects);
        var world = new CombatWorld(
            poses,
            units.Units.Select(u => new CombatUnit
            {
                Unit = u.Object,
                Profile = combat.Profiles[u.Object],
                DefinitionName = u.Object.Class + ".odf",
                Radius = 0,
                Mobile = true,
                Motion = u.Unit is Ship ? FlightFixtures.Cruiser(u.Object.Position) : null,
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
    [DataRow("\"destroyedSystems\": [\"Engines\"]")]
    [DataRow("\"crew\": 0")]
    public void AUnitWithoutThrustCoastsFromTheFirstTickAlongItsStartingVelocityInTheFrameOfItsPosition(string keys)
    {
        var (world, objects, reports) = World(Obj("s", keys + ", \"velocity\": [3, 0, 4]"));
        Assert.AreEqual(0, reports.Length, string.Join("\n", reports));

        world.Step(CombatWorld.Tick);

        // the layout's Z is mirrored into Godot's frame, as the position's is
        var v = world.State(objects[0])!.Velocity;
        Assert.AreEqual(3 * 0.992f, v.X, 1e-3); // 5 units/s braked by 2.0 per second per second for one tick
        Assert.AreEqual(-4 * 0.992f, v.Z, 1e-3);
        Assert.IsTrue(poses.Pose(objects[0]).Position.Z < objects[0].Position.Z);
    }

    [TestMethod]
    public void AStartingVelocityReachesThePlacedObjectInGodotsFrame()
    {
        var (_, objects, _) = World(Obj("s", "\"velocity\": [1, 2, 3]"));

        Assert.AreEqual(new System.Numerics.Vector3(1, 2, -3), objects[0].Start!.Velocity);
    }

    [TestMethod]
    [DataRow("s", "\"velocity\": [0, 0, 5]", "with thrust")]
    [DataRow("s", "\"crew\": 0.5, \"velocity\": [0, 0, 5]", "with thrust")]
    [DataRow("nophys", "\"crew\": 0, \"velocity\": [0, 0, 5]", "cannot move")]
    [DataRow("station", "\"velocity\": [0, 0, 5]", "cannot move")]
    [DataRow("station", "\"destroyedSystems\": [\"Engines\"], \"velocity\": [0, 0, 5]", "cannot move")]
    [DataRow("rock", "\"velocity\": [0, 0, 5]", "without combat state")]
    public void AVelocityThatCannotBeUsedIsOneGapNamingTheObjectAndTheRestOpens(string cls, string keys, string cause)
    {
        var (world, objects, reports) = World(Obj(cls, keys), Obj("s", x: 1100));

        Assert.AreEqual(1, reports.Length, string.Join("\n", reports));
        StringAssert.Contains(reports[0], $"{cls} ({cls})");
        StringAssert.Contains(reports[0], cause);
        Assert.IsNotNull(world.State(objects[1]));
        world.Step(1);
        Assert.AreEqual(0, world.State(objects[1])!.Speed);
        if (cls == "s")
            Assert.AreEqual(0, world.State(objects[0])!.Speed); // a ship with thrust stays at rest
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
