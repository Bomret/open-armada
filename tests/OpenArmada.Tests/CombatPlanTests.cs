using System;
using System.Linq;
using System.Numerics;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Assets;
using OpenArmada.Formats;
using OpenArmada.Sandbox;

namespace OpenArmada.Tests;

// The combat profiles of a map's units, from maps and installs built in code (INV-0001).
[TestClass]
public class CombatPlanTests
{
    readonly TempInstall temp = new();

    [TestCleanup]
    public void CleanUp() => temp.CleanUp();

    const string Physics = "physicsFile = \"p.odf\"\r\n";
    const string Constants = "float SHIELD_DAMAGE_PROTECTION = 0.5;\r\nint SYSTEM_HITPOINTS_BONUS = 2;\r\n";
    const string Race =
        "name = \"federation\"\r\ncrewYellowStatus = 0.4\r\ncrewRedStatus = 0.2\r\nweaponYellow = 1.5\r\n"
        + "weaponRed = 2.5\r\nrepairYellow = 1.1\r\nrepairRed = 1.2\r\nrepairStrength = 1.5\r\n";
    const string Beam =
        "classLabel = \"cannon\"\r\nordName = \"beamo\"\r\nshotDelay = 2\r\nrange = 250\r\nhitChance = 0.7\r\n"
        + "\t\"zmama.odf\" 1.0\r\nstopToHitModifier = 0.25\r\nfireSound = \"b.wav\"\r\ncrewAffectsWeaponDelay = 1\r\n";
    const string BeamOrd =
        "classLabel = \"phaser\"\r\ndamageBase = 30\r\n\t\"zmama.odf\" 2\r\ndamageVariance = 3\r\ndamageThreshold = 20\r\n"
        + "shieldCrewModifier = 0.2\r\nhullCrewModifier = 0.3\r\nlifeSpan = 4\r\nshotSpeed = 300\r\nSprite = \"bp\"\r\n";
    const string Ship =
        Physics
        + "race = \"federation\"\r\nmaxHealth = 900\r\nshieldRate = 5\r\nshieldDelay = 3\r\ncrewCost = 100\r\n"
        + "maximumCrew = 120\r\n";

    // A loose-file marker (an empty file in the install, no races.odf) for a test of an install with no races.odf.
    const string NoRaces = "no-races";

    // Definitions are "class" -> odf text, in addon/. The map holds one object of each class in `classes`.
    (CombatPlan Plan, SelectableUnit[] Units) Plan(
        string[] classes,
        (string Class, string Odf)[] definitions,
        params (string Path, string Content)[] loose
    )
    {
        // the install lists its races in races.odf; tests that do not bring their own list name fed.odf
        if (!loose.Any(l => l.Path is "addon/races.odf" or "addon/races.zfs" or NoRaces))
        {
            loose = loose.Append(("addon/races.odf", "numberOfRaces = 1\r\nrace0 = \"fed.odf\"\r\n")).ToArray();
        }

        var install = new GameFileSystem(
            temp.MakeInstall(definitions.Select(d => ($"addon/{d.Class}.odf", d.Odf)).Concat(loose).ToArray())
        );
        var map = new BznBuilder { Background = "" };
        foreach (var c in classes)
        {
            map.Objects.Add(
                new BznBuilder.Obj
                {
                    Class = c,
                    Team = 1,
                    Position = new Vector3(1, 2, 3),
                    Label = c,
                }
            );
        }

        var asset = new MapAsset
        {
            Name = "bzn/t.bzn",
            Map = Bzn.Read(map.Build()),
            Starts = Array.Empty<MapStart>(),
            Overview = null,
            ScriptedPaths = Array.Empty<MapPath>(),
        };
        var plan = SandboxPlan.Build(asset, SandboxPlan.Definitions(install, asset), _ => true);
        var units = UnitPlan.Build(install, plan, _ => true).Units.ToArray();

        return (CombatPlan.Build(install, plan, units), units);
    }

    // One ship "s" with the given extra keys, and the definitions every ship of these tests shares.
    (CombatPlan Plan, SelectableUnit[] Units) OneShip(string extra, params (string Class, string Odf)[] more) =>
        Plan(
            new[] { "s" },
            new[] { ("s", Ship + extra), ("fed", Race), ("beam", Beam), ("beamo", BeamOrd) }.Concat(more).ToArray(),
            ("RTS_CFG.h", Constants)
        );

    static CombatProfile ProfileOf((CombatPlan Plan, SelectableUnit[] Units) r) => r.Plan.Profiles[r.Units[0].Object];

    [TestMethod]
    public void AWeaponSlotIsPairedWithTheHardpointsOfTheSameNumber()
    {
        var r = OneShip("weapon1 = \"beam\"\r\nweaponHardpoints1 = \"hp1\" \"hp2\"\r\n");
        var w = ProfileOf(r).Weapons.Single();
        CollectionAssert.AreEqual(new[] { "hp1", "hp2" }, w.Hardpoints.ToArray());
        Assert.AreEqual(2f, w.ShotDelay);
        Assert.AreEqual(250f, w.Range);
        Assert.AreEqual(0.25f, w.StopToHitModifier);
        Assert.IsTrue(w.CrewAffectsWeaponDelay);
        Assert.AreEqual("b.wav", w.FireSound);
        Assert.AreEqual("phaser", w.Ordnance.ClassLabel);
        Assert.AreEqual(2f, w.Ordnance.DamageBase.For("zmama.odf"));
        Assert.AreEqual(1f, w.HitChance.For("zmama.odf"));
        Assert.AreEqual("bp", w.Ordnance.Sprite);
    }

    [TestMethod]
    public void SlotNumbersWithGapsKeepTheirOwnHardpoints()
    {
        var r = OneShip(
            "weapon1 = \"beam\"\r\nweaponHardpoints1 = \"a\"\r\nweapon3 = \"beam\"\r\nweaponHardpoints3 = \"c\"\r\n"
                + "weaponHardpoints2 = \"b\"\r\n"
        );
        var w = ProfileOf(r).Weapons;
        CollectionAssert.AreEqual(new[] { 1, 3 }, w.Select(x => x.Slot).ToArray());
        Assert.AreEqual("c", w[1].Hardpoints.Single());
    }

    [TestMethod]
    public void AMissingWeaponDefinitionIsReportedOnceAndTheOtherWeaponsStay()
    {
        var r = OneShip(
            "weapon1 = \"nope\"\r\nweaponHardpoints1 = \"a\"\r\nweapon2 = \"beam\"\r\nweaponHardpoints2 = \"b\"\r\n"
        );
        Assert.AreEqual(1, ProfileOf(r).Weapons.Count);
        var gap = r.Plan.Reports.Single(g => g.Cause.Contains("weapon definition"));
        CollectionAssert.AreEqual(new[] { "nope" }, gap.Names.ToArray());
        Assert.AreEqual(1, gap.Count);
    }

    [TestMethod]
    public void AMissingOrdnanceDefinitionIsReportedAndTheWeaponIsLeftOut()
    {
        var r = OneShip(
            "weapon1 = \"bad\"\r\nweaponHardpoints1 = \"a\"\r\nweapon2 = \"beam\"\r\nweaponHardpoints2 = \"b\"\r\n",
            ("bad", Beam.Replace("beamo", "gone"))
        );
        Assert.AreEqual(2, ProfileOf(r).Weapons.Single().Slot);
        var gap = r.Plan.Reports.Single(g => g.Cause.Contains("ordnance definition"));
        CollectionAssert.AreEqual(new[] { "gone" }, gap.Names.ToArray());
        Assert.AreEqual(1, gap.Count);
    }

    [TestMethod]
    public void OneMissingWeaponNamedByTwoUnitClassesIsOneCauseWithBothClasses()
    {
        var weapon = "weapon1 = \"nope\"\r\nweaponHardpoints1 = \"a\"\r\n";
        var r = Plan(
            new[] { "s", "t" },
            new[] { ("s", Ship + weapon), ("t", Ship + weapon), ("fed", Race) },
            ("RTS_CFG.h", Constants)
        );
        var gap = r.Plan.Reports.Single(g => g.Cause.Contains("weapon definition"));
        CollectionAssert.AreEqual(new[] { "nope" }, gap.Names.ToArray());
        Assert.AreEqual(2, gap.Count);
    }

    [TestMethod]
    public void AWeaponSlotNumberTooLargeForAnIntIsReportedAndTheMapStillOpens()
    {
        var r = OneShip(
            "weapon99999999999 = \"beam\"\r\nweaponHardpoints99999999999 = \"a\"\r\n"
                + "weapon1 = \"beam\"\r\nweaponHardpoints1 = \"b\"\r\n"
        );
        Assert.AreEqual(1, ProfileOf(r).Weapons.Single().Slot);
        Assert.IsTrue(r.Plan.Reports.Any(g => g.Names.Any(n => n.Contains("s.weapon99999999999"))));
    }

    [TestMethod]
    public void AWeaponOfAnotherClassIsLeftOutWithoutAGap()
    {
        var r = OneShip(
            "weapon1 = \"sp\"\r\nweaponHardpoints1 = \"a\"\r\n",
            ("sp", "classLabel = \"special\"\r\nordName = \"beamo\"\r\n")
        );
        Assert.AreEqual(0, ProfileOf(r).Weapons.Count);
        Assert.AreEqual(0, r.Plan.Reports.Count);
    }

    [TestMethod]
    public void AWeaponWithoutHardpointsIsReportedAndLeftOut()
    {
        var r = OneShip("weapon1 = \"beam\"\r\nweapon2 = \"beam\"\r\nweaponHardpoints2 = \"b\"\r\n");
        Assert.AreEqual(2, ProfileOf(r).Weapons.Single().Slot);
        Assert.IsTrue(r.Plan.Reports.Any(g => g.Cause == "weapon without hardpoints, weapon left out"));
    }

    // A world of the plan's one ship and a hostile unit 50 units away, at red alert.
    static (CombatWorld World, PlacedObject Ship, PlacedObject Foe) WithFoe((CombatPlan Plan, SelectableUnit[] Units) r)
    {
        var ship = r.Units[0].Object;
        var foe = CombatFixtures.Placed("foe", 2, 50);
        var world = new CombatWorld(
            new UnitPoses(new[] { ship, foe }),
            new[]
            {
                new CombatUnit
                {
                    Unit = ship,
                    Profile = ProfileOf(r),
                    DefinitionName = "s.odf",
                    Radius = 0,
                    Mobile = true,
                },
                new CombatUnit
                {
                    Unit = foe,
                    Profile = CombatFixtures.Profile(),
                    DefinitionName = "foe.odf",
                    Radius = 0,
                    Mobile = true,
                },
            },
            1,
            () => 0
        );

        return (world, ship, foe);
    }

    [TestMethod]
    public void AUnitWhoseOnlyWeaponHasNoHardpointsRefusesAnAttackOrder()
    {
        var (world, ship, foe) = WithFoe(OneShip("weapon1 = \"beam\"\r\n"));
        Assert.IsFalse(world.Attack(ship, foe));
    }

    [TestMethod]
    public void AUnitWhoseOnlyWeaponHasNoHardpointsTakesNoAutoOrderAtRed()
    {
        var (world, ship, _) = WithFoe(OneShip("weapon1 = \"beam\"\r\n"));
        world.Step(0.1);
        Assert.IsNull(world.State(ship)!.Order);
    }

    [TestMethod]
    public void AMalformedOverrideIsReportedWithClassAndKeyAndTheDefaultStands()
    {
        var r = OneShip(
            "weapon1 = \"beam2\"\r\nweaponHardpoints1 = \"a\"\r\n",
            ("beam2", Beam.Replace("1.0\r\n", "lots\r\n"))
        );
        Assert.AreEqual(0.7f, ProfileOf(r).Weapons.Single().HitChance.For("zmama.odf"));
        var gap = r.Plan.Reports.Single();
        Assert.IsTrue(gap.Names.Any(n => n.Contains("beam2") && n.Contains("hitChance")));
    }

    [TestMethod]
    public void ProfileHoldsShieldsCrewAndRaceValuesFromTheInstall()
    {
        var r = OneShip("canAttack = 0\r\ndefaultGreenAlert = 1\r\nfireball = \"fb\"\r\nlifeSupportLoss = 5\r\n");
        var p = ProfileOf(r);
        Assert.AreEqual(900f, p.MaxHealth);
        Assert.AreEqual(5f, p.ShieldRate);
        Assert.AreEqual(3f, p.ShieldDelay);
        Assert.AreEqual(100f, p.CrewCost);
        Assert.AreEqual(120f, p.MaximumCrew);
        Assert.IsFalse(p.CanAttack);
        Assert.IsTrue(p.DefaultGreenAlert);
        Assert.AreEqual("fb", p.Fireball);
        Assert.AreEqual(5f, p.LifeSupportLoss);
        Assert.AreEqual(0.4f, p.Race.CrewYellowStatus);
        Assert.AreEqual(2.5f, p.Race.WeaponRed);
        Assert.AreEqual(1.5f, p.Race.RepairStrength);
        Assert.AreEqual(0.5f, r.Plan.ShieldDamageProtection);
        Assert.AreEqual(2f, r.Plan.SystemHitpointsBonus);
        Assert.AreEqual(0, r.Plan.Reports.Count);
    }

    [TestMethod]
    public void ASystemWithNoHitPointsIsAbsent()
    {
        var r = OneShip(
            "enginesHitPoints = 20\r\nenginesHitPercent = 5\r\nenginesCrewLoss = 3\r\nenginesRepairTime = 10\r\n"
                + "sensorsHitPoints = 0\r\nsensorsHitPercent = 5\r\n"
        );
        var s = ProfileOf(r).Systems.Single();
        Assert.AreEqual("Engines", s.Name);
        Assert.AreEqual(20f, s.HitPoints);
        Assert.AreEqual(10f, s.RepairTime);
    }

    [TestMethod]
    public void AMissingRaceDefinitionIsReportedAndTakesStandInValues()
    {
        var (plan, units) = Plan(
            new[] { "s" },
            new[] { ("s", Ship), ("beam", Beam), ("beamo", BeamOrd) },
            ("RTS_CFG.h", Constants)
        );
        var race = plan.Profiles[units[0].Object].Race;
        Assert.AreEqual(1f, race.WeaponRed);
        var gap = plan.Reports.Single(g => g.Cause.Contains("race definition missing"));
        CollectionAssert.AreEqual(new[] { "federation" }, gap.Names.ToArray());
    }

    [TestMethod]
    public void ARaceDefinitionNamedOnlyByRacesOdfIsFoundByItsName()
    {
        // races.odf and fedx.odf are archive members: no scan of addon/*.odf reaches them
        var (plan, units) = Plan(
            new[] { "s" },
            new[] { ("s", Ship), ("beam", Beam), ("beamo", BeamOrd) },
            ("RTS_CFG.h", Constants),
            (
                "addon/races.zfs",
                Encoding.Latin1.GetString(
                    ZfsBuilder.Build(
                        0x9466279b,
                        ZfsBuilder.M("races.odf", "numberOfRaces = 1\r\nrace0 = \"fedx.odf\"\r\n"),
                        ZfsBuilder.M("fedx.odf", Race)
                    )
                )
            )
        );
        Assert.AreEqual(2.5f, plan.Profiles[units[0].Object].Race.WeaponRed);
        Assert.AreEqual(0, plan.Reports.Count);
    }

    [TestMethod]
    public void ARaceFileThatCannotBeReadIsReportedAsUnreadableNotMissing()
    {
        // fed.odf is an archive member stored compressed: races.odf lists it, but reading it throws
        var archive = Encoding.Latin1.GetString(ZfsBuilder.Build(0, ZfsBuilder.M("fed.odf", Race, 2)));
        var (plan, _) = Plan(
            new[] { "s" },
            new[] { ("s", Ship), ("beam", Beam), ("beamo", BeamOrd) },
            ("RTS_CFG.h", Constants),
            ("addon/x.zfs", archive)
        );
        Assert.AreEqual(0, plan.Reports.Count(g => g.Cause.Contains("race definition missing")));
        var gap = plan.Reports.Single(g => g.Cause.Contains("race definition cannot be read"));
        CollectionAssert.AreEqual(new[] { "federation" }, gap.Names.ToArray());
    }

    [TestMethod]
    public void AMissingRacesOdfIsReportedOnce()
    {
        var (plan, _) = Plan(
            new[] { "s", "s" },
            new[] { ("s", Ship), ("fed", Race), ("beam", Beam), ("beamo", BeamOrd) },
            ("RTS_CFG.h", Constants),
            (NoRaces, "")
        );
        var gap = plan.Reports.Single(g => g.Cause.Contains("races.odf"));
        Assert.AreEqual(2, gap.Count);
        Assert.AreEqual(0, plan.Reports.Count(g => g.Cause.Contains("race definition missing")));
    }

    [TestMethod]
    public void AMissingGameConstantIsReportedAndTakesAStandIn()
    {
        var r = Plan(new[] { "s" }, new[] { ("s", Ship), ("fed", Race) }, ("RTS_CFG.h", "int OTHER = 1;\r\n"));
        Assert.AreEqual(1f, r.Plan.ShieldDamageProtection);
        Assert.AreEqual(0f, r.Plan.SystemHitpointsBonus);
        Assert.AreEqual(2, r.Plan.Reports.Count(g => g.Cause.Contains("game constant")));
    }

    [TestMethod]
    public void AMissingGameConstantsFileIsReportedOnce()
    {
        var r = Plan(new[] { "s" }, new[] { ("s", Ship), ("fed", Race) });
        Assert.AreEqual(1, r.Plan.Reports.Count(g => g.Cause.Contains("RTS_CFG.h")));
    }

    [TestMethod]
    public void AMaximumCrewOfZeroTakesAPositiveStandIn()
    {
        var r = Plan(
            new[] { "s" },
            new[] { ("s", Ship + "maximumCrew = 0\r\n"), ("fed", Race) },
            ("RTS_CFG.h", Constants)
        );
        var p = ProfileOf(r);
        Assert.AreEqual(100f, p.MaximumCrew);
        Assert.IsTrue(r.Plan.Reports.Any(g => g.Cause.Contains("maximumCrew")));
    }

    [DataTestMethod]
    [DataRow("maxHealth")]
    [DataRow("shieldRate")]
    [DataRow("crewCost")]
    public void AMissingShieldOrCrewValueIsReportedAndIsANumber(string key)
    {
        var odf = string.Join(
            "\r\n",
            Ship.Split("\r\n").Where(l => !l.StartsWith(key + " ", StringComparison.Ordinal))
        );
        var r = Plan(new[] { "s" }, new[] { ("s", odf), ("fed", Race) }, ("RTS_CFG.h", Constants));
        var p = ProfileOf(r);
        Assert.AreEqual(key == "maxHealth" ? 0f : 900f, p.MaxHealth);
        Assert.AreEqual(key == "shieldRate" ? 0f : 5f, p.ShieldRate);
        Assert.AreEqual(key == "crewCost" ? 1f : 100f, p.CrewCost);
        Assert.IsTrue(r.Plan.Reports.Any(g => g.Cause.StartsWith(key + " missing")));
    }

    [TestMethod]
    public void AGapOfOneClassCountsEachUnitOfIt()
    {
        var odf = Ship.Replace("crewCost = 100\r\n", "");
        var r = Plan(new[] { "s", "s" }, new[] { ("s", odf), ("fed", Race) }, ("RTS_CFG.h", Constants));
        Assert.AreEqual(2, r.Plan.Reports.Single(g => g.Cause.StartsWith("crewCost missing")).Count);
    }

    // The map holds two objects of the same class, team, position, and label.
    [TestMethod]
    public void TwoIdenticalUnitsGetAProfileEach()
    {
        var r = Plan(
            new[] { "s", "s" },
            new[] { ("s", Ship), ("fed", Race), ("beam", Beam), ("beamo", BeamOrd) },
            ("RTS_CFG.h", Constants)
        );
        Assert.AreEqual(2, r.Units.Length);
        Assert.AreNotSame(r.Units[0].Object, r.Units[1].Object);
        Assert.AreEqual(2, r.Plan.Profiles.Count);
        Assert.IsTrue(r.Units.All(u => r.Plan.Profiles.ContainsKey(u.Object)));
    }
}
