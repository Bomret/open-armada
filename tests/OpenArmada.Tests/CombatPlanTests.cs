using System;
using System.Linq;
using System.Numerics;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Assets;
using OpenArmada.Diagnostics;
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
    const string Constants =
        "#include \"ART_CFG.h\"\r\nfloat SHIELD_DAMAGE_PROTECTION = 0.5;\r\nint SYSTEM_HITPOINTS_BONUS = 2;\r\nfloat FORMATION_SPACING = 100.0;\r\nfloat MAX_PREVENTION_ROTATION = 0.6;\r\nint cfgMaxCrew = 5000;\r\n"
        + AvoidanceConstants
        + TransporterConstants;
    const string ConstantsWithoutPrevention =
        "#include \"ART_CFG.h\"\r\nfloat SHIELD_DAMAGE_PROTECTION = 0.5;\r\nint SYSTEM_HITPOINTS_BONUS = 2;\r\nfloat FORMATION_SPACING = 100.0;\r\n"
        + AvoidanceConstants
        + TransporterConstants;

    // values that differ from the stock ones, so a test sees them read
    const string TransporterConstants =
        "float TRANSPORTER_RANGE = 800.0;\r\nfloat TRANSPORTER_DELAY = 2.0;\r\nfloat TRANSPORTER_ACTION_DELAY = 3.0;\r\nint TRANSPORTER_MAX = 7;\r\n";

    // the stock install's avoidance keys (RTS_CFG.h lines 272, 276, 280)
    const string AvoidanceConstants =
        "int OBSTACLE_AVOIDANCE = 0;\r\nfloat COLLISION_LOOK_AHEAD_TIME = 1.4;\r\nfloat COLLISION_SAFETY_MARGIN = 1.2;\r\n";
    const string ArtConstants =
        "int ST3D_CHUNK_MANAGER_MAX_CHUNKS = 1500;\r\nfloat ST3D_CHUNK_DENSITY = 0.8;\r\nfloat ST3D_CHUNK_MAX_LIFETIME = 2.75;\r\n"
        + "float ST3D_CHUNK_MAX_ANGULAR_VELOCITY = 5.0;\r\nfloat ST3D_CHUNK_MAX_VELOCITY = 70.0;\r\n";
    const string Race =
        "name = \"federation\"\r\ncrewYellowStatus = 0.4\r\ncrewRedStatus = 0.2\r\nweaponYellow = 1.5\r\n"
        + "weaponRed = 2.5\r\nrepairYellow = 1.1\r\nrepairRed = 1.2\r\nrepairStrength = 1.5\r\nboardingStrength = 1.5\r\n"
        + "crewAccumulationRate = 3.5\r\nplanetCrewAccumulationModifier = 1.5\r\n";
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

    // A loose-file marker for a test of an install whose RTS_CFG.h names an ART_CFG.h the install lacks.
    const string NoArt = "no-art";

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

        // the install's RTS_CFG.h includes an ART_CFG.h; tests that do not bring their own get a full one
        if (loose.Any(l => l.Path == "RTS_CFG.h") && !loose.Any(l => l.Path is "ART_CFG.h" or NoArt))
        {
            loose = loose.Append(("ART_CFG.h", ArtConstants)).ToArray();
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

        return (CombatPlan.Build(install, plan, units, MovePlan.Build(plan, units, install)), units);
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
    public void TheTransporterValuesAndTheBoardingStrengthAreReadFromTheInstall()
    {
        var r = OneShip("");
        Assert.AreEqual(new TransporterValues(800, 2, 3, 7), r.Plan.Transporter);
        Assert.AreEqual(1.5f, ProfileOf(r).Race.BoardingStrength);
        Assert.AreEqual(0, r.Plan.Reports.Count);
    }

    [TestMethod]
    public void TheCrewAccumulationRateAndItsPlanetModifierAreReadFromTheRace()
    {
        var race = ProfileOf(OneShip("")).Race;
        Assert.AreEqual(3.5f, race.CrewAccumulationRate);
        Assert.AreEqual(1.5f, race.PlanetCrewAccumulationModifier);
    }

    [TestMethod]
    public void TheCrewPoolCapIsCfgMaxCrewAndTheStockValueWithoutIt()
    {
        Assert.AreEqual(5000, OneShip("").Plan.CrewPoolCap);
        var r = Plan(new[] { "s" }, new[] { ("s", Ship), ("fed", Race) }, ("RTS_CFG.h", ConstantsWithoutPrevention));
        Assert.AreEqual(CombatWorld.DefaultCrewPoolCap, r.Plan.CrewPoolCap);
    }

    [TestMethod]
    public void MissingTransporterValuesAreReportedOnceEachWithTheirStandIns()
    {
        var r = Plan(
            new[] { "s", "s" },
            new[] { ("s", Ship), ("fed", Race) },
            ("RTS_CFG.h", "#include \"ART_CFG.h\"\r\nfloat TRANSPORTER_RANGE = 800.0;\r\n" + AvoidanceConstants)
        );
        Assert.AreEqual(new TransporterValues(800, 1, 1, 5), r.Plan.Transporter);
        foreach (var name in new[] { "TRANSPORTER_DELAY", "TRANSPORTER_ACTION_DELAY", "TRANSPORTER_MAX" })
        {
            var gap = r.Plan.Reports.Single(g => g.Cause.Contains(name));
            StringAssert.Contains(gap.Cause, "stand-in");
        }
    }

    [TestMethod]
    public void AMissingBoardingStrengthIsOneAndReportedOnce()
    {
        var r = Plan(
            new[] { "s", "s" },
            new[] { ("s", Ship), ("fed", Race.Replace("boardingStrength = 1.5\r\n", "")) },
            ("RTS_CFG.h", Constants)
        );
        Assert.AreEqual(1f, r.Plan.Profiles[r.Units[0].Object].Race.BoardingStrength);
        var gap = r.Plan.Reports.Single(g => g.Cause.Contains("boardingStrength"));
        StringAssert.Contains(gap.Cause, "stand-in 1 used");
    }

    [TestMethod]
    public void ABoardingStrengthThatIsNotAboveZeroTakesTheStandInAndIsReported()
    {
        var r = OneShip("", ("fed", Race.Replace("boardingStrength = 1.5", "boardingStrength = 0")));
        Assert.AreEqual(1f, ProfileOf(r).Race.BoardingStrength);
        Assert.IsTrue(r.Plan.Reports.Any(g => g.Cause.Contains("boardingStrength")));
    }

    [TestMethod]
    public void TheStandInRaceCarriesABoardingStrength()
    {
        var r = Plan(
            new[] { "s" },
            new[] { ("s", Ship) },
            ("RTS_CFG.h", Constants),
            ("addon/races.odf", "numberOfRaces = 0\r\n")
        );
        Assert.AreEqual(1f, ProfileOf(r).Race.BoardingStrength);
    }

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
    public void TheSpecialAndTargetLocationFlagsOfAWeaponAreRead()
    {
        var r = OneShip(
            "weapon1 = \"beam\"\r\nweaponHardpoints1 = \"hp1\"\r\nweapon2 = \"flagged\"\r\nweaponHardpoints2 = \"hp2\"\r\n",
            ("flagged", Beam + "special = 1\r\ntargetLocation = 1\r\n")
        );
        var weapons = ProfileOf(r).Weapons;
        Assert.IsFalse(weapons.Single(w => w.Slot == 1).Special);
        Assert.IsFalse(weapons.Single(w => w.Slot == 1).TargetLocation);
        Assert.IsTrue(weapons.Single(w => w.Slot == 2).Special);
        Assert.IsTrue(weapons.Single(w => w.Slot == 2).TargetLocation);
    }

    const string HoldWeapon =
        "classLabel = \"toggleweapon\"\r\nordName = \"holdo\"\r\nbuttonSlot = 4\r\nrange = 300\r\nspecial = 1\r\n";
    const string HoldOrd =
        "classLabel = \"holding_bm\"\r\nspecialEnergyCost = 50\r\nlifeSpan = 30\r\ntargetRange = 310\r\n"
        + "boardingDelay = 1.5\r\nboardingInterval = 2\r\nboardingSize = 20\r\n";

    [TestMethod]
    public void ASpecialToggleWeaponWithTheHoldingBeamIsReadWithItsOrdnance()
    {
        var r = OneShip("weapon1 = \"hold\"\r\n", ("hold", HoldWeapon), ("holdo", HoldOrd));
        var hold = ProfileOf(r).SpecialWeapons.Single();
        Assert.AreEqual(
            (4, 300f, 50f, 310f, 30f),
            (hold.Slot, hold.Range, hold.EnergyCost, hold.TargetRange, hold.LifeSpan)
        );
        Assert.AreEqual((20f, 2f, 1.5f), (hold.BoardingSize, hold.BoardingInterval, hold.BoardingDelay));
        Assert.AreEqual(0, ProfileOf(r).Weapons.Count);
        Assert.AreEqual(0, r.Plan.Reports.Count);
    }

    [TestMethod]
    public void ASpecialWeaponIsReadWithItsNameHardpointsFireSoundBeamModelAndScales()
    {
        var r = OneShip(
            "weapon1 = \"hold\"\r\nweaponHardpoints1 = \"hp03\" \"hp05\"\r\nweapon2 = \"assim\"\r\nweaponHardpoints2 = \"hp07\"\r\n",
            ("hold", HoldWeapon + "fireSound = \"swbhb.wav\"\r\n"),
            ("holdo", HoldOrd + "shotGeometry = \"wholdbm.sod\"\r\nradiusScale = 2.5\r\nlengthScale = 5\r\n"),
            (
                "assim",
                HoldWeapon.Replace("holdo", "assimo").Replace("buttonSlot = 4", "buttonSlot = 3")
                    + "fireSound = \"swbauto.wav\"\r\n"
            ),
            (
                "assimo",
                "classLabel = \"borgbore\"\r\nspecialEnergyCost = 8\r\nspecialValue = 5\r\nattackRange = 400\r\nshotGeometry = \"wborgbor.sod\"\r\nradiusScale = 3\r\nlengthScale = 4\r\n"
            )
        );
        var specials = ProfileOf(r).SpecialWeapons;
        var hold = specials.Single(w => w.Kind == SpecialWeaponKind.HoldingBeam);
        var assimilator = specials.Single(w => w.Kind == SpecialWeaponKind.AutoAssimilator);
        Assert.AreEqual(
            ("hold", "swbhb.wav", "wholdbm.sod", 2.5f, 5f),
            (hold.Name, hold.FireSound, hold.Geometry, hold.RadiusScale, hold.LengthScale)
        );
        CollectionAssert.AreEqual(new[] { "hp03", "hp05" }, hold.Hardpoints.ToArray());
        Assert.AreEqual(
            ("assim", "swbauto.wav", "wborgbor.sod", 3f, 4f),
            (
                assimilator.Name,
                assimilator.FireSound,
                assimilator.Geometry,
                assimilator.RadiusScale,
                assimilator.LengthScale
            )
        );
        CollectionAssert.AreEqual(new[] { "hp07" }, assimilator.Hardpoints.ToArray());
        Assert.AreEqual(0, r.Plan.Reports.Count);
    }

    [TestMethod]
    public void AnAutoAssimilatorIsReadWithItsRateRangeAndCostAndNoDuration()
    {
        var r = OneShip(
            "weapon1 = \"assim\"\r\n",
            ("assim", HoldWeapon.Replace("holdo", "assimo").Replace("buttonSlot = 4", "buttonSlot = 3")),
            (
                "assimo",
                "classLabel = \"borgbore\"\r\nspecialEnergyCost = 8\r\nspecialValue = 5\r\nattackRange = 400\r\n"
                    + "duration = 9\r\n"
            )
        );
        var assimilator = ProfileOf(r).SpecialWeapons.Single();
        Assert.AreEqual(
            (SpecialWeaponKind.AutoAssimilator, 3, 400f, 8f, 5f, 0f),
            (
                assimilator.Kind,
                assimilator.Slot,
                assimilator.Range,
                assimilator.EnergyCost,
                assimilator.CrewRate,
                assimilator.LifeSpan
            )
        );
        Assert.AreEqual(0, ProfileOf(r).Weapons.Count);
        Assert.AreEqual(0, r.Plan.Reports.Count);
    }

    [TestMethod]
    public void AToggleWeaponThatIsNotSpecialOrNotTheHoldingBeamIsLeftOutWithoutAGap()
    {
        var r = OneShip(
            "weapon1 = \"plain\"\r\nweapon2 = \"other\"\r\n",
            ("plain", HoldWeapon.Replace("special = 1", "special = 0")),
            ("other", HoldWeapon.Replace("holdo", "othero")),
            ("holdo", HoldOrd),
            ("othero", HoldOrd.Replace("holding_bm", "tractor_bm"))
        );
        Assert.AreEqual(0, ProfileOf(r).SpecialWeapons.Count);
        Assert.AreEqual(0, r.Plan.Reports.Count);
    }

    [TestMethod]
    public void OrdnanceWithoutShieldKeysUsesTheDefaultGeometryAndDuration()
    {
        var o = ProfileOf(OneShip("weapon1 = \"beam\"\r\nweaponHardpoints1 = \"hp1\"\r\n")).Weapons.Single().Ordnance;

        Assert.AreEqual("xshldx01", o.ShieldGeometryName);
        Assert.AreEqual(0.8f, o.ShieldDuration);
        Assert.IsFalse(o.IgnoreShield);
        Assert.AreEqual(1f, o.Size);
    }

    [TestMethod]
    public void OrdnanceShieldKeysAreRead()
    {
        var r = OneShip(
            "weapon1 = \"beam2\"\r\nweaponHardpoints1 = \"hp1\"\r\n",
            ("beam2", Beam.Replace("beamo", "ord2")),
            (
                "ord2",
                BeamOrd + "ignoreShield = 1\r\nshieldGeometryName = \"xother\"\r\nshieldDuration = 2.5\r\nsize = 3\r\n"
            )
        );

        var o = ProfileOf(r).Weapons.Single().Ordnance;

        Assert.IsTrue(o.IgnoreShield);
        Assert.AreEqual("xother", o.ShieldGeometryName);
        Assert.AreEqual(2.5f, o.ShieldDuration);
        Assert.AreEqual(3f, o.Size);
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
    public void EachGapCauseIsOneWarnEntryEqualToItsLabelLine()
    {
        using var log = new CapturedLog();
        var weapon = "weapon1 = \"nope\"\r\nweaponHardpoints1 = \"a\"\r\n";
        var r = Plan(
            new[] { "s", "t" },
            new[] { ("s", Ship + weapon), ("t", Ship + weapon), ("fed", Race) },
            ("RTS_CFG.h", Constants)
        );
        Assert.IsTrue(r.Plan.Reports.Count > 0);
        // The fixture also builds a move plan, whose own grouped report is one WARN sandbox entry.
        var combat = log.Lines.Where(l => l.Contains(" WARN combat: ")).ToList();
        Assert.AreEqual(r.Plan.Reports.Count, combat.Count, log.Text);
        for (var i = 0; i < combat.Count; i++)
        {
            StringAssert.Contains(combat[i], r.Plan.Reports[i].Text);
        }

        Assert.AreEqual(log.Lines.Count, Log.WarnCount);
    }

    // An archive whose one member is stored compressed, so reading that member throws.
    static string Unreadable(string member, string content) =>
        Encoding.Latin1.GetString(ZfsBuilder.Build(0, ZfsBuilder.M(member, content, 2)));

    const string ArmedShip = "weapon1 = \"beam\"\r\nweaponHardpoints1 = \"a\"\r\n";

    [TestMethod]
    public void AnAssetThatCannotBeReadIsOneErrorEntryWithItsNameAndReasonAndNoWarnEntryForIt()
    {
        var weapon = ("addon/w.zfs", Unreadable("beam.odf", Beam));
        var ordnance = ("addon/o.zfs", Unreadable("beamo.odf", BeamOrd));
        var raceFile = ("addon/x.zfs", Unreadable("fed.odf", Race));
        var racesList = ("addon/races.zfs", Unreadable("races.odf", "numberOfRaces = 1\r\nrace0 = \"fed.odf\"\r\n"));
        var constants = ("addon/c.zfs", Unreadable("RTS_CFG.h", Constants));
        var ship = ("s", Ship + ArmedShip);
        var cases = new (string Name, string[] Classes, (string, string)[] Definitions, (string, string)[] Loose)[]
        {
            ("weapon definition beam", new[] { "s", "s" }, new[] { ship }, new[] { ("RTS_CFG.h", Constants), weapon }),
            (
                "ordnance definition beamo",
                new[] { "s", "s" },
                new[] { ship, ("beam", Beam) },
                new[] { ("RTS_CFG.h", Constants), ordnance }
            ),
            (
                "race definition fed.odf",
                new[] { "s", "s" },
                new[] { ship, ("beam", Beam), ("beamo", BeamOrd) },
                new[] { ("RTS_CFG.h", Constants), raceFile }
            ),
            (
                "races.odf",
                new[] { "s", "s" },
                new[] { ship, ("beam", Beam), ("beamo", BeamOrd) },
                new[] { ("RTS_CFG.h", Constants), racesList }
            ),
            (
                "RTS_CFG.h",
                new[] { "s" },
                new[] { ship, ("beam", Beam), ("beamo", BeamOrd), ("fed", Race) },
                new[] { constants }
            ),
        };
        foreach (var c in cases)
        {
            using var log = new CapturedLog();
            var r = Plan(c.Classes, c.Definitions, c.Loose);
            var errors = log.Lines.Where(l => l.Contains(" ERROR combat: " + c.Name + ": ")).ToList();
            Assert.AreEqual(1, errors.Count, c.Name + "\n" + log.Text);
            Assert.IsFalse(log.Lines.Any(l => l.Contains(" WARN ") && l.Contains("cannot be read")), c.Name);
            Assert.IsTrue(r.Plan.Reports.Any(g => g.Cause.Contains("cannot be read")), c.Name);
        }
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
            () => 0,
            (_, _, pose) => pose.Position
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
    public void AUnitWhoseOnlyWeaponHasNoHardpointsPicksNoTargetAtRed()
    {
        var (world, ship, _) = WithFoe(OneShip("weapon1 = \"beam\"\r\n"));
        world.Step(0.1);
        Assert.IsNull(world.State(ship)!.Target);
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
    public void ProfileReadsIsHeroFromTheDefinition()
    {
        Assert.IsTrue(ProfileOf(OneShip("isHero = 1\r\n")).IsHero);
        Assert.IsFalse(ProfileOf(OneShip("")).IsHero);
    }

    [TestMethod]
    public void ProfileReadsTheTransportVoiceLineAndHasNoneWithoutTheKey()
    {
        Assert.AreEqual("picard.wav", ProfileOf(OneShip("eventTransport = \"picard.wav\"\r\n")).TransportVoice);
        Assert.AreEqual("", ProfileOf(OneShip("")).TransportVoice);
    }

    [TestMethod]
    public void ProfileReadsTheSpecialEnergyMaximumAndRate()
    {
        var p = ProfileOf(OneShip("maxSpecialEnergy = 1000\r\nspecialEnergyRate = 10\r\n"));
        Assert.AreEqual(1000f, p.MaxSpecialEnergy);
        Assert.AreEqual(10f, p.SpecialEnergyRate);
    }

    [TestMethod]
    public void ADefinitionWithNoSpecialEnergyHasNoneAndNoGap()
    {
        var r = OneShip("");
        var p = ProfileOf(r);
        Assert.AreEqual(0f, p.MaxSpecialEnergy);
        Assert.AreEqual(0f, p.SpecialEnergyRate);
        Assert.IsFalse(r.Plan.Reports.Any(g => g.Cause.Contains("pecialEnergy")));
    }

    [TestMethod]
    public void ProfileHoldsShieldsCrewAndRaceValuesFromTheInstall()
    {
        var r = OneShip(
            "canAttack = 0\r\ndefaultGreenAlert = 1\r\nfireball = \"fb\"\r\nlifeSupportLoss = 5\r\n"
                + "maxSpecialEnergy = 1000\r\nspecialEnergyRate = 10\r\n"
        );
        var p = ProfileOf(r);
        Assert.AreEqual(1000f, p.MaxSpecialEnergy);
        Assert.AreEqual(10f, p.SpecialEnergyRate);
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
        Assert.AreEqual(100f, r.Plan.FormationSpacing);
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
    public void ASystemReadsItsSparkTimeAndTargetHardpoints()
    {
        var r = OneShip(
            "enginesHitPoints = 20\r\nenginesSparkTime = 0.25\r\nenginesTargetHardpoints = \"hp01\" \"hp02\"\r\n"
        );
        var s = ProfileOf(r).Systems.Single();
        Assert.AreEqual(0.25f, s.SparkTime);
        CollectionAssert.AreEqual(new[] { "hp01", "hp02" }, s.TargetHardpoints.ToArray());
    }

    [TestMethod]
    public void ASystemWithoutSparkTimeSparksEveryHalfSecondAndWithoutTargetHardpointsAtNone()
    {
        var s = ProfileOf(OneShip("enginesHitPoints = 20\r\n")).Systems.Single();
        Assert.AreEqual(0.5f, s.SparkTime);
        Assert.AreEqual(0, s.TargetHardpoints.Count);
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
        Assert.AreEqual(11, r.Plan.Reports.Count(g => g.Cause.Contains("game constant"))); // the four TRANSPORTER_ values included
        Assert.AreEqual(40f, r.Plan.FormationSpacing);
        Assert.AreEqual(TransporterValues.Stock, r.Plan.Transporter);
    }

    [TestMethod]
    public void AFormationSpacingThatIsNoPositiveNumberTakesTheGamesDefaultAndIsReportedOnce()
    {
        foreach (var value in new[] { "0", "-5", "\"wide\"", "1e40" })
        {
            var r = Plan(
                new[] { "s", "s" },
                new[] { ("s", Ship), ("fed", Race) },
                (
                    "RTS_CFG.h",
                    $"float SHIELD_DAMAGE_PROTECTION = 0.5;\r\nint SYSTEM_HITPOINTS_BONUS = 2;\r\nfloat FORMATION_SPACING = {value};\r\n"
                )
            );
            Assert.AreEqual(40f, r.Plan.FormationSpacing, value);
            Assert.AreEqual(1, r.Plan.Reports.Count(g => g.Cause.Contains("FORMATION_SPACING")), value);
        }
    }

    [TestMethod]
    public void AMissingGameConstantsFileTakesTheGamesFormationSpacing()
    {
        var r = Plan(new[] { "s" }, new[] { ("s", Ship), ("fed", Race) });
        Assert.AreEqual(40f, r.Plan.FormationSpacing);
    }

    [TestMethod]
    public void MaxPreventionRotationComesFromTheInstall()
    {
        var r = Plan(new[] { "s" }, new[] { ("s", Ship), ("fed", Race) }, ("RTS_CFG.h", Constants));
        Assert.AreEqual(0.6f, r.Plan.MaxPreventionRotation);
        Assert.AreEqual(0, r.Plan.Reports.Count(g => g.Cause.Contains("MAX_PREVENTION_ROTATION")));
    }

    [TestMethod]
    public void AMissingMaxPreventionRotationTakesTheGamesDefaultAndIsReportedOnce()
    {
        var r = Plan(
            new[] { "s", "s" },
            new[] { ("s", Ship), ("fed", Race) },
            ("RTS_CFG.h", ConstantsWithoutPrevention)
        );
        Assert.AreEqual(0.4f, r.Plan.MaxPreventionRotation);
        Assert.AreEqual(1, r.Plan.Reports.Count(g => g.Cause.Contains("game constant MAX_PREVENTION_ROTATION")));
    }

    [TestMethod]
    public void TheAvoidanceKeysComeFromTheInstall()
    {
        var r = Plan(new[] { "s" }, new[] { ("s", Ship), ("fed", Race) }, ("RTS_CFG.h", Constants));
        Assert.AreEqual(new AvoidanceKeys(false, 1.4f, 1.2f), r.Plan.Avoidance);
        Assert.AreEqual(0, r.Plan.Reports.Count(g => g.Cause.Contains("OBSTACLE_AVOIDANCE")));
    }

    [TestMethod]
    public void AMissingAvoidanceKeyTakesTheGamesDefaultAndIsReportedOnce()
    {
        var r = Plan(
            new[] { "s", "s" },
            new[] { ("s", Ship), ("fed", Race) },
            ("RTS_CFG.h", "#include \"ART_CFG.h\"\r\nfloat COLLISION_LOOK_AHEAD_TIME = 2.0;\r\n")
        );
        // the game's statics: on, 4.0, 1.5 (0x5fbc30, 0x5fbc34, 0x5fbc38); the key the file sets is the file's
        Assert.AreEqual(new AvoidanceKeys(true, 2f, 1.5f), r.Plan.Avoidance);
        Assert.AreEqual(1, r.Plan.Reports.Count(g => g.Cause.Contains("game constant OBSTACLE_AVOIDANCE")));
        Assert.AreEqual(1, r.Plan.Reports.Count(g => g.Cause.Contains("game constant COLLISION_SAFETY_MARGIN")));
        Assert.AreEqual(0, r.Plan.Reports.Count(g => g.Cause.Contains("game constant COLLISION_LOOK_AHEAD_TIME")));
    }

    [TestMethod]
    public void AMissingGameConstantsFileIsReportedOnce()
    {
        var r = Plan(new[] { "s" }, new[] { ("s", Ship), ("fed", Race) });
        Assert.AreEqual(1, r.Plan.Reports.Count(g => g.Cause.Contains("RTS_CFG.h")));
    }

    [TestMethod]
    public void AMissingMaximumCrewDefaultsToTheCrewCost()
    {
        // 0x4601d7: the class's maximumCrew defaults to its crewCost
        var odf = Ship.Replace("maximumCrew = 120\r\n", "");
        var r = Plan(new[] { "s" }, new[] { ("s", odf), ("fed", Race) }, ("RTS_CFG.h", Constants));
        var p = ProfileOf(r);
        Assert.AreEqual(100f, p.DefinedMaximumCrew);
        Assert.IsFalse(r.Plan.Reports.Any(g => g.Cause.Contains("maximumCrew")));
    }

    [TestMethod]
    public void AMissingMaximumCrewAndACrewCostOfZeroDefinesNoMaximumCrew()
    {
        var odf = Ship.Replace("maximumCrew = 120\r\n", "").Replace("crewCost = 100", "crewCost = 0");
        var p = ProfileOf(Plan(new[] { "s" }, new[] { ("s", odf), ("fed", Race) }, ("RTS_CFG.h", Constants)));
        Assert.AreEqual(0f, p.DefinedMaximumCrew);
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
        Assert.AreEqual(0f, p.DefinedMaximumCrew);
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

    // The debris limits: ART_CFG.h reached through RTS_CFG.h's #include (the game's compiled defaults where it lacks one).
    DebrisLimits DebrisOf(params (string Path, string Content)[] loose) =>
        Plan(new[] { "s" }, new[] { ("s", Ship), ("fed", Race) }, loose).Plan.Debris;

    [TestMethod]
    public void TheDebrisLimitsComeFromTheArtConfigurationTheGameConstantsInclude()
    {
        var limits = DebrisOf(("RTS_CFG.h", Constants), ("ART_CFG.h", ArtConstants));
        Assert.AreEqual(1500, limits.MaxChunks);
        Assert.AreEqual(0.8f, limits.Density);
        Assert.AreEqual(2.75f, limits.MaxLifetime);
        Assert.AreEqual(5f, limits.MaxAngularVelocity);
        Assert.AreEqual(70f, limits.MaxVelocity);
    }

    [TestMethod]
    public void AMissingArtConfigurationTakesTheCompiledDefaultsAndIsReportedOnce()
    {
        using var log = new CapturedLog();
        var r = Plan(new[] { "s", "s" }, new[] { ("s", Ship), ("fed", Race) }, ("RTS_CFG.h", Constants), (NoArt, ""));
        Assert.AreEqual(DebrisLimits.Default, r.Plan.Debris);
        Assert.AreEqual(1, r.Plan.Reports.Count(g => g.Cause.StartsWith("ART_CFG.h missing")));
        Assert.AreEqual(1, log.Lines.Count(l => l.Contains(" WARN combat: ") && l.Contains("ART_CFG.h")), log.Text);
    }

    [TestMethod]
    public void GameConstantsThatIncludeNoArtConfigurationTakeTheCompiledDefaultsAndReportIt()
    {
        var r = Plan(
            new[] { "s" },
            new[] { ("s", Ship), ("fed", Race) },
            ("RTS_CFG.h", "float SHIELD_DAMAGE_PROTECTION = 0.5;\r\n")
        );
        Assert.AreEqual(DebrisLimits.Default, r.Plan.Debris);
        Assert.AreEqual(1, r.Plan.Reports.Count(g => g.Cause.StartsWith("ART_CFG.h missing")));
    }

    [TestMethod]
    public void AnUnreadableArtConfigurationIsOneErrorAndTakesTheCompiledDefaults()
    {
        using var log = new CapturedLog();
        var r = Plan(
            new[] { "s" },
            new[] { ("s", Ship), ("fed", Race) },
            ("RTS_CFG.h", Constants),
            ("addon/a.zfs", Unreadable("ART_CFG.h", ArtConstants))
        );
        Assert.AreEqual(DebrisLimits.Default, r.Plan.Debris);
        Assert.AreEqual(1, log.Lines.Count(l => l.Contains(" ERROR combat: ART_CFG.h: ")), log.Text);
        Assert.IsTrue(r.Plan.Reports.Any(g => g.Cause.StartsWith("ART_CFG.h cannot be read")));
    }

    [TestMethod]
    public void AMissingDebrisKeyAloneTakesItsDefaultAndTheRestAreRead()
    {
        var r = Plan(
            new[] { "s" },
            new[] { ("s", Ship), ("fed", Race) },
            ("RTS_CFG.h", Constants),
            ("ART_CFG.h", "float ST3D_CHUNK_DENSITY = 0.5;\r\n")
        );
        Assert.AreEqual(0.5f, r.Plan.Debris.Density);
        Assert.AreEqual(3f, r.Plan.Debris.MaxLifetime);
        Assert.AreEqual(3000, r.Plan.Debris.MaxChunks);
        Assert.AreEqual(1, r.Plan.Reports.Count(g => g.Cause.Contains("ST3D_CHUNK_MAX_LIFETIME")));
        Assert.AreEqual(0, r.Plan.Reports.Count(g => g.Cause.Contains("ST3D_CHUNK_DENSITY")));
    }

    // The test's ART_CFG.h with one limit's value replaced.
    static string ArtWith(string key, string value) =>
        string.Concat(
            ArtConstants
                .Split("\r\n", StringSplitOptions.RemoveEmptyEntries)
                .Select(l => (l.Contains($" {key} = ") ? $"{l[..l.IndexOf('=')]}= {value};" : l) + "\r\n")
        );

    // A literal the header can hold that is infinite as the float a limit is read into: 1e39 and its 40-digit integer
    // (a NaN or an infinity is no literal of the header, so it reads as no number).
    [TestMethod]
    [DataRow("ST3D_CHUNK_MANAGER_MAX_CHUNKS", "1000000000000000000000000000000000000000")]
    [DataRow("ST3D_CHUNK_DENSITY", "1e39")]
    [DataRow("ST3D_CHUNK_DENSITY", "-1e39")]
    [DataRow("ST3D_CHUNK_MAX_LIFETIME", "1e39")]
    [DataRow("ST3D_CHUNK_MAX_ANGULAR_VELOCITY", "-1e39")]
    [DataRow("ST3D_CHUNK_MAX_VELOCITY", "1e39")]
    public void ADebrisLimitThatIsNoFiniteNumberTakesItsDefaultAndIsReportedOnce(string key, string value)
    {
        var r = Plan(
            new[] { "s" },
            new[] { ("s", Ship), ("fed", Race) },
            ("RTS_CFG.h", Constants),
            ("ART_CFG.h", ArtWith(key, value))
        );
        var expected = DebrisLimits.Default with
        {
            MaxChunks = 1500,
            Density = 0.8f,
            MaxLifetime = 2.75f,
            MaxAngularVelocity = 5f,
            MaxVelocity = 70f,
        };
        expected = key switch
        {
            "ST3D_CHUNK_MANAGER_MAX_CHUNKS" => expected with { MaxChunks = DebrisLimits.Default.MaxChunks },
            "ST3D_CHUNK_DENSITY" => expected with { Density = DebrisLimits.Default.Density },
            "ST3D_CHUNK_MAX_LIFETIME" => expected with { MaxLifetime = DebrisLimits.Default.MaxLifetime },
            "ST3D_CHUNK_MAX_ANGULAR_VELOCITY" => expected with
            {
                MaxAngularVelocity = DebrisLimits.Default.MaxAngularVelocity,
            },
            _ => expected with { MaxVelocity = DebrisLimits.Default.MaxVelocity },
        };
        Assert.AreEqual(expected, r.Plan.Debris);
        Assert.AreEqual(1, r.Plan.Reports.Count(g => g.Cause.Contains(key)));
    }

    // 3000000000 is above int's range, which the cap is held in.
    [TestMethod]
    [DataRow("10001")]
    [DataRow("3000000000")]
    public void ADebrisCapAboveTheMostIsReportedOnceAndTakesTheMost(string value)
    {
        var r = Plan(
            new[] { "s" },
            new[] { ("s", Ship), ("fed", Race) },
            ("RTS_CFG.h", Constants),
            ("ART_CFG.h", ArtWith("ST3D_CHUNK_MANAGER_MAX_CHUNKS", value))
        );
        Assert.AreEqual(DebrisLimits.MostChunks, r.Plan.Debris.MaxChunks);
        Assert.AreEqual(1, r.Plan.Reports.Count(g => g.Cause.Contains("ST3D_CHUNK_MANAGER_MAX_CHUNKS")));
    }

    [TestMethod]
    public void ADebrisDensityAboveTheMostIsReportedOnceAndTakesTheMost()
    {
        var r = Plan(
            new[] { "s" },
            new[] { ("s", Ship), ("fed", Race) },
            ("RTS_CFG.h", Constants),
            ("ART_CFG.h", ArtWith("ST3D_CHUNK_DENSITY", "16777216.0"))
        );
        Assert.AreEqual(DebrisLimits.MostDensity, r.Plan.Debris.Density);
        Assert.AreEqual(1, r.Plan.Reports.Count(g => g.Cause.Contains("ST3D_CHUNK_DENSITY")));
    }
}
