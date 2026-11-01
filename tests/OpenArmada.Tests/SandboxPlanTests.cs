using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Assets;
using OpenArmada.Formats;
using OpenArmada.Sandbox;

namespace OpenArmada.Tests;

// The scene plan of a map sandbox, from maps built in code (INV-0001).
[TestClass]
public class SandboxPlanTests
{
    readonly TempInstall temp = new();

    [TestCleanup]
    public void CleanUp() => temp.CleanUp();

    // Plans the map; each definition is "class" -> odf text, each model is a name the install holds.
    SandboxPlan Plan(BznBuilder map, (string Class, string Odf)[] definitions, params string[] models) =>
        Plan(map, definitions, null, models);

    // `edit` changes the parsed map before planning, for values the BZN reader itself rejects.
    SandboxPlan Plan(
        BznBuilder map,
        (string Class, string Odf)[] definitions,
        Func<BznMap, BznMap>? edit,
        params string[] models
    )
    {
        var install = new GameFileSystem(
            temp.MakeInstall(definitions.Select(d => ($"addon/{d.Class}.odf", d.Odf)).ToArray())
        );
        var parsed = Bzn.Read(map.Build());
        var asset = new MapAsset
        {
            Name = "bzn/t.bzn",
            Map = edit?.Invoke(parsed) ?? parsed,
            Starts = Array.Empty<MapStart>(),
            Overview = null,
            ScriptedPaths = Array.Empty<MapPath>(),
        };

        return SandboxPlan.Build(asset, SandboxPlan.Definitions(install, asset), models.Contains);
    }

    static BznBuilder MapOf(params BznBuilder.Obj[] objects)
    {
        var b = new BznBuilder { Background = "" };
        b.Objects.AddRange(objects);

        return b;
    }

    static BznBuilder.Obj Ship(
        string cls = "fbattle",
        int team = 1,
        Vector3? at = null,
        float[]? axes = null,
        string label = "s"
    ) =>
        new()
        {
            Class = cls,
            Team = team,
            Position = at ?? new Vector3(100, 38.5f, 200),
            Label = label,
            Axes = axes,
        };

    static readonly (string, string)[] Fbattle = { ("fbattle", "baseName = fdreadnought") };

    [TestMethod]
    public void AnObjectIsPlacedWithTheDefinitionsBaseNameAtItsMirroredPosition()
    {
        var plan = Plan(MapOf(Ship()), Fbattle, "fdreadnought");
        var o = plan.Objects.Single();
        Assert.AreEqual("fdreadnought", o.Model);
        Assert.AreEqual(new Vector3(100, 38.5f, -200), o.Position);
    }

    [TestMethod]
    public void AClassWithoutScaleSodLoadsItsModelAtItsStoredSize()
    {
        Assert.AreEqual(1f, Plan(MapOf(Ship()), Fbattle, "fdreadnought").Objects.Single().Scale);
    }

    [TestMethod]
    [DataRow("ScaleSOD = 1.3f", 1.3f)]
    [DataRow("scalesod = .8", 0.8f)]
    [DataRow("scaleSOD = 4.0 // a comment", 4f)]
    public void ScaleSodIsReadInAnyCaseWithASuffixOrNoLeadingDigit(string line, float scale)
    {
        var plan = Plan(MapOf(Ship()), new[] { ("fbattle", $"baseName = fdreadnought\n{line}") }, "fdreadnought");
        Assert.AreEqual(scale, plan.Objects.Single().Scale);
        Assert.AreEqual(0, plan.Reports.Count);
    }

    [TestMethod]
    public void ADefinitionsOwnScaleSodBeatsAnIncludedOne()
    {
        var own = Plan(
            MapOf(Ship()),
            new[]
            {
                ("fbattle", "#include \"base.odf\"\nbaseName = fdreadnought\nscaleSOD = 1.4"),
                ("base", "scaleSOD = 2"),
            },
            "fdreadnought"
        );
        var inherited = Plan(
            MapOf(Ship()),
            new[] { ("fbattle", "#include \"base.odf\"\nbaseName = fdreadnought"), ("base", "scaleSOD = 2") },
            "fdreadnought"
        );
        Assert.AreEqual(1.4f, own.Objects.Single().Scale);
        Assert.AreEqual(2f, inherited.Objects.Single().Scale);
    }

    [TestMethod]
    public void AScaleSodThatIsNoNumberIsAGapAndTheStoredSizeIsUsed()
    {
        var plan = Plan(
            MapOf(Ship()),
            new[] { ("fbattle", "baseName = fdreadnought\nscaleSOD = big") },
            "fdreadnought"
        );
        Assert.AreEqual(1f, plan.Objects.Single().Scale);
        Assert.AreEqual(
            "scaleSOD is no number, model drawn at its stored size: fbattle (1 object)",
            plan.Reports.Single().Text
        );
    }

    [TestMethod]
    public void AModelNameMayCarryItsExtension()
    {
        var plan = Plan(MapOf(Ship()), new[] { ("fbattle", "baseName = fdreadnought.sod") }, "fdreadnought");
        Assert.AreEqual("fdreadnought", plan.Objects.Single().Model);
    }

    [TestMethod]
    public void WithoutABaseNameTheModelIsTheOneNamedLikeTheClass()
    {
        var plan = Plan(MapOf(Ship("Msun1")), new[] { ("Msun1", "x = 1") }, "Msun1");
        Assert.AreEqual("Msun1", plan.Objects.Single().Model);
    }

    [TestMethod]
    public void AnObjectWithNoModelAtAllIsLeftOutWithoutAReport()
    {
        var plan = Plan(MapOf(Ship("waypoint")), new[] { ("waypoint", "x = 1") });
        Assert.AreEqual(0, plan.Objects.Count);
        Assert.AreEqual(0, plan.Reports.Count);
    }

    [TestMethod]
    public void TheOrientationIsTheMapsAxesMirroredLikeTheModelBuilder()
    {
        // Right, Up, Front of a quarter turn about Up: right = +Z, front = -X (map frame).
        var plan = Plan(MapOf(Ship(axes: new float[] { 0, 0, 1, 0, 1, 0, -1, 0, 0 })), Fbattle, "fdreadnought");
        var o = plan.Objects.Single();
        Assert.AreEqual(new Vector3(0, 0, -1), o.AxisX);
        Assert.AreEqual(new Vector3(0, 1, 0), o.AxisY);
        Assert.AreEqual(new Vector3(1, 0, 0), o.AxisZ);
    }

    [TestMethod]
    public void ADegenerateOrientationIsReportedAndUsesTheIdentity()
    {
        var plan = Plan(MapOf(Ship(axes: new float[] { 0, 0, 0, 0, 1, 0, 0, 0, 1 })), Fbattle, "fdreadnought");
        var o = plan.Objects.Single();
        Assert.AreEqual(
            (new Vector3(1, 0, 0), new Vector3(0, 1, 0), new Vector3(0, 0, 1)),
            (o.AxisX, o.AxisY, o.AxisZ)
        );
        var report = plan.Reports.Single();
        StringAssert.Contains(report.Cause, "orientation");
        Assert.AreEqual(1, report.Count);
    }

    [TestMethod]
    public void NonFiniteAxesAreReportedAndUseTheIdentity()
    {
        var plan = Plan(
            MapOf(Ship()),
            Fbattle,
            m =>
                m with
                {
                    Objects = m
                        .Objects.Select(o =>
                            o with
                            {
                                Right = new Vector3(float.NaN, 0, 0),
                                Front = new Vector3(0, 0, float.PositiveInfinity),
                            }
                        )
                        .ToList(),
                },
            "fdreadnought"
        );
        var o = plan.Objects.Single();
        Assert.AreEqual(
            (new Vector3(1, 0, 0), new Vector3(0, 1, 0), new Vector3(0, 0, 1)),
            (o.AxisX, o.AxisY, o.AxisZ)
        );
        StringAssert.Contains(plan.Reports.Single().Cause, "orientation");
    }

    [TestMethod]
    public void CollinearAxesAreReported()
    {
        var plan = Plan(MapOf(Ship(axes: new float[] { 1, 0, 0, 2, 0, 0, 0, 0, 1 })), Fbattle, "fdreadnought");
        StringAssert.Contains(plan.Reports.Single().Cause, "orientation");
    }

    [TestMethod]
    public void SmallButIndependentAxesAreAnOrientation()
    {
        var plan = Plan(
            MapOf(Ship(axes: new float[] { 0.05f, 0, 0, 0, 0.05f, 0, 0, 0, 0.05f })),
            Fbattle,
            "fdreadnought"
        );
        Assert.AreEqual(0, plan.Reports.Count);
    }

    [TestMethod]
    public void ATeamBeyondTheMapsRecordsIsReportedAndUsesItsOwnSlot()
    {
        var plan = Plan(
            MapOf(Ship(team: 8)),
            Fbattle,
            m => m with { Teams = m.Teams.Take(4).ToList() },
            "fdreadnought"
        );
        Assert.AreEqual(8, plan.Objects.Single().Slot);
        StringAssert.Contains(plan.Reports.Single().Names[0], "team 8 has no record");
    }

    [TestMethod]
    public void ANegativeRecordIsReportedAndUsesTheTeamsOwnSlot()
    {
        var map = MapOf(Ship(team: 2));
        map.TeamColors = new[] { 0, 0, -3, 0, 0, 0, 0, 0, 0 };
        var plan = Plan(map, Fbattle, "fdreadnought");
        Assert.AreEqual(2, plan.Objects.Single().Slot);
        CollectionAssert.AreEqual(new[] { "-3" }, plan.Reports.Single().Names.ToArray());
    }

    [TestMethod]
    public void ARecordPastTheLastSlotIsReported()
    {
        var map = MapOf(Ship(team: 2));
        map.TeamColors = new[] { 0, 0, TeamColors.SlotNames.Count, 0, 0, 0, 0, 0, 0 };
        var plan = Plan(map, Fbattle, "fdreadnought");
        Assert.AreEqual(2, plan.Objects.Single().Slot);
        StringAssert.Contains(plan.Reports.Single().Cause, "slot");
    }

    [TestMethod]
    public void AMissingDefinitionIsReportedOnceWithItsCountAndTheObjectsAreLeftOut()
    {
        var plan = Plan(MapOf(Ship("ghost", label: "a"), Ship("ghost", label: "b"), Ship()), Fbattle, "fdreadnought");
        Assert.AreEqual(1, plan.Objects.Count);
        var report = plan.Reports.Single();
        CollectionAssert.AreEqual(new[] { "ghost" }, report.Names.ToArray());
        Assert.AreEqual(2, report.Count);
        StringAssert.Contains(report.Text, "ghost");
        StringAssert.Contains(report.Text, "2");
    }

    [TestMethod]
    public void ADefinitionThatCannotBeReadIsReportedOnceWithItsCountAndTheObjectsAreLeftOut()
    {
        // a compressed archive entry: the install lists ghost.odf, but reading it throws UnsupportedFormatException
        var archive = System.Text.Encoding.Latin1.GetString(
            ZfsBuilder.Build(0, ZfsBuilder.M("ghost.odf", "baseName = fdreadnought", 2))
        );
        var install = new GameFileSystem(
            temp.MakeInstall(("addon/fbattle.odf", "baseName = fdreadnought"), ("addon/x.zfs", archive))
        );
        var map = MapOf(Ship("ghost", label: "a"), Ship("ghost", label: "b"), Ship());
        var asset = new MapAsset
        {
            Name = "bzn/t.bzn",
            Map = Bzn.Read(map.Build()),
            Starts = Array.Empty<MapStart>(),
            Overview = null,
            ScriptedPaths = Array.Empty<MapPath>(),
        };
        var plan = SandboxPlan.Build(asset, SandboxPlan.Definitions(install, asset), _ => true);
        Assert.AreEqual(1, plan.Objects.Count);
        var report = plan.Reports.Single();
        CollectionAssert.AreEqual(new[] { "ghost" }, report.Names.ToArray());
        Assert.AreEqual(2, report.Count);
        StringAssert.Contains(report.Text, "object definition cannot be read");
    }

    [TestMethod]
    public void ADefinitionThatCannotBeReadIsOneErrorEntryWithItsNameAndReason()
    {
        // a compressed archive entry used by two objects: an asset failed to load, one cause (INV-0005, INV-0002)
        var archive = System.Text.Encoding.Latin1.GetString(
            ZfsBuilder.Build(0, ZfsBuilder.M("ghost.odf", "baseName = fdreadnought", 2))
        );
        var install = new GameFileSystem(temp.MakeInstall(("addon/x.zfs", archive)));
        var map = MapOf(Ship("ghost", label: "a"), Ship("ghost", label: "b"));
        var asset = new MapAsset
        {
            Name = "bzn/t.bzn",
            Map = Bzn.Read(map.Build()),
            Starts = Array.Empty<MapStart>(),
            Overview = null,
            ScriptedPaths = Array.Empty<MapPath>(),
        };
        using var log = new CapturedLog();
        SandboxPlan.Build(asset, SandboxPlan.Definitions(install, asset), _ => true);
        var entries = log.Lines.Where(l => l.Contains("ghost")).ToList();
        StringAssert.Matches(entries.Single(), new Regex(@" ERROR sandbox: object definition ghost\.odf: \S"));
    }

    [TestMethod]
    public void ABaseNameOfAMissingModelIsReportedWithoutAFallback()
    {
        var plan = Plan(MapOf(Ship(), Ship()), Fbattle, "fbattle");
        Assert.AreEqual(0, plan.Objects.Count);
        var report = plan.Reports.Single();
        CollectionAssert.AreEqual(new[] { "fdreadnought" }, report.Names.ToArray());
        Assert.AreEqual(2, report.Count);
    }

    [TestMethod]
    public void AnObjectOfClassLabelAsteroidFieldIsNotPlacedAsAModelObject()
    {
        // The map stores the class name ("mafield"); the definition's classLabel says it is an asteroid field,
        // and the model its baseName names is not in the install.
        var plan = Plan(MapOf(Ship("mafield")), new[] { ("mafield", "classLabel = AsteroidField\nbaseName = sscr_1") });
        Assert.AreEqual(0, plan.Objects.Count);
        Assert.IsFalse(plan.Reports.Any(r => r.Names.Contains("sscr_1")));
    }

    [TestMethod]
    public void TheObstaclesAreTheFieldsByTheirRadiusAndThePlanetsByTheirModelRadius()
    {
        const string field =
            "classLabel = AsteroidField\nminAsteroids = 1\nmaxAsteroids = 2\nfieldRadius = 50\nrotationSpeed = 1\norbitingSpeed = 1";
        var plan = Plan(
            MapOf(
                Ship("mafield", at: new Vector3(1, 2, 3)),
                Ship("mearth", at: new Vector3(10, 20, 30), label: "earth"),
                Ship("fbattle", at: new Vector3(7, 7, 7), label: "ship")
            ),
            new[]
            {
                ("mafield", field),
                ("mearth", "classLabel = planet_obj\nbaseName = mplanet"),
                ("fbattle", "classLabel = ship\nbaseName = fdreadnought"),
            },
            "master",
            "mplanet",
            "fdreadnought"
        );
        var radii = new List<string>();
        var obstacles = plan.Obstacles(o =>
        {
            radii.Add(o.Label);

            return 33;
        });
        CollectionAssert.AreEquivalent(
            new[] { new Obstacle(new Vector3(1, 2, -3), 50, true), new Obstacle(new Vector3(10, 20, -30), 33) },
            obstacles.ToArray()
        );
        CollectionAssert.AreEqual(new[] { "earth" }, radii); // only the planet's model is asked for
    }

    const string HoleKeys =
        "classLabel = blackhole_obj\nbaseName = hole\nGravityRadius = 450\nEventRadius = 70\nGravityForce = 60\nRotation = 0.5\ndistanceBelowGrid = 70";

    [TestMethod]
    public void ABlackHoleIsReadFromItsDefinitionAndItsCenterIsLoweredByDistanceBelowGrid()
    {
        var plan = Plan(
            MapOf(Ship("mblk", at: new Vector3(10, 20, 30), label: "hole1")),
            new[] { ("mblk", HoleKeys) },
            "hole"
        );
        var hole = plan.BlackHoles.Single();
        Assert.AreSame(plan.Objects.Single(), hole.Object);
        Assert.AreEqual(new Vector3(10, -50, -30), hole.Center); // the placed position, Z mirrored, 70 lower
        Assert.AreEqual(450f, hole.GravityRadius);
        Assert.AreEqual(70f, hole.EventRadius);
        Assert.AreEqual(60f, hole.GravityForce);
        Assert.AreEqual(0.5f, hole.Rotation);
        Assert.AreEqual(0, plan.Reports.Count);
    }

    [TestMethod]
    public void ABlackHoleDefinitionWithoutKeysTakesTheGamesDefaultsAndEachMissingKeyIsReportedOnce()
    {
        using var log = new CapturedLog();
        var plan = Plan(
            MapOf(Ship("mblk", label: "one"), Ship("mblk", label: "two")),
            new[] { ("mblk", "classLabel = blackhole_obj\nbaseName = hole") },
            "hole"
        );
        Assert.AreEqual(2, plan.BlackHoles.Count);
        foreach (var hole in plan.BlackHoles)
        {
            Assert.AreEqual(50f, hole.GravityRadius);
            Assert.AreEqual(10f, hole.EventRadius);
            Assert.AreEqual(50f, hole.GravityForce);
            Assert.AreEqual(0.02f, hole.Rotation);
            Assert.AreEqual(38.5f, hole.Center.Y); // no lowering
        }

        var report = plan.Reports.Single(r => r.Cause.Contains("missing", StringComparison.Ordinal));
        Assert.AreEqual(2, report.Count);
        CollectionAssert.AreEquivalent(
            new[]
            {
                "mblk: GravityRadius",
                "mblk: EventRadius",
                "mblk: GravityForce",
                "mblk: Rotation",
                "mblk: distanceBelowGrid",
            },
            report.Names.ToArray()
        );
        Assert.AreEqual(1, log.Lines.Count(l => l.Contains("GravityForce", StringComparison.Ordinal)), log.Text);
    }

    [TestMethod]
    public void ABlackHoleDefinitionMissingOnlyGravityForceUsesFiftyAndReportsItOnce()
    {
        var plan = Plan(MapOf(Ship("mblk")), new[] { ("mblk", HoleKeys.Replace("GravityForce = 60\n", "")) }, "hole");
        Assert.AreEqual(50f, plan.BlackHoles.Single().GravityForce);
        Assert.AreEqual(450f, plan.BlackHoles.Single().GravityRadius);
        CollectionAssert.AreEqual(new[] { "mblk: GravityForce" }, plan.Reports.Single().Names.ToArray());
    }

    [TestMethod]
    public void ABlackHoleKeyThatIsNoNumberOrANegativeRadiusIsReportedAndTheDefaultIsUsed()
    {
        var plan = Plan(
            MapOf(Ship("mblk")),
            new[]
            {
                (
                    "mblk",
                    "classLabel = blackhole_obj\nbaseName = hole\nGravityRadius = big\nEventRadius = -3\nGravityForce = 1 2\nRotation = 0.1\ndistanceBelowGrid = 0"
                ),
            },
            "hole"
        );
        var hole = plan.BlackHoles.Single();
        Assert.AreEqual(50f, hole.GravityRadius);
        Assert.AreEqual(10f, hole.EventRadius);
        Assert.AreEqual(50f, hole.GravityForce);
        Assert.AreEqual(0.1f, hole.Rotation);
        var report = plan.Reports.Single();
        CollectionAssert.AreEquivalent(
            new[] { "mblk: GravityRadius", "mblk: EventRadius", "mblk: GravityForce" },
            report.Names.ToArray()
        );
    }

    [TestMethod]
    public void ABlackHoleWhoseModelIsMissingIsNotPlacedAndHasNoPull()
    {
        var plan = Plan(MapOf(Ship("mblk")), new[] { ("mblk", HoleKeys) });
        Assert.AreEqual(0, plan.BlackHoles.Count);
    }

    [TestMethod]
    public void APlanetWithoutAModelRadiusPushesNothingAndAPlanetWithoutAModelIsNotPlaced()
    {
        var plan = Plan(
            MapOf(Ship("mearth", label: "bare"), Ship("mearth", label: "nomodel")),
            new[] { ("mearth", "classLabel = planet_obj\nbaseName = mplanet") },
            "mplanet"
        );
        Assert.AreEqual(0, plan.Obstacles(_ => 0).Count);
        Assert.AreEqual(0, plan.Obstacles(_ => float.NaN).Count);
        var missing = Plan(MapOf(Ship("mearth")), new[] { ("mearth", "classLabel = planet_obj\nbaseName = mplanet") });
        Assert.AreEqual(0, missing.Obstacles(_ => 30).Count);
    }

    [TestMethod]
    public void AnAsteroidFieldIsPlannedWithItsRocksAndItsGapsAreReported()
    {
        const string odf =
            "classLabel = AsteroidField\nminAsteroids = 1\nmaxAsteroids = 2\nfieldRadius = 50\nrotationSpeed = 1\norbitingSpeed = 1";
        var plan = Plan(MapOf(Ship("mafield")), new[] { ("mafield", odf) }, "master", "master1");
        Assert.AreEqual(1, plan.Fields.Count);
        Assert.AreEqual(1, plan.Reports.Count); // six of the eight models are missing
        Assert.AreEqual(6, plan.Reports.Single().Names.Count);
    }

    [TestMethod]
    public void AGapOfOneCauseOnManyObjectsIsOneWarnEntryWithTheLabelLine()
    {
        using var log = new CapturedLog();
        var plan = Plan(
            MapOf(Ship(), Ship(), Ship("fother")),
            Fbattle.Append(("fother", "baseName = fdreadnought")).ToArray()
        );
        var line = Assert.ContainsSingle(log.Lines);
        StringAssert.EndsWith(line, " WARN sandbox: " + plan.Reports.Single().Text);
    }

    [TestMethod]
    public void AnAsteroidFieldsGapIsLoggedOnceBySandboxPlanAndNotAgain()
    {
        const string odf =
            "classLabel = AsteroidField\nminAsteroids = 1\nmaxAsteroids = 2\nfieldRadius = 50\nrotationSpeed = 1\norbitingSpeed = 1";
        using var log = new CapturedLog();
        var plan = Plan(MapOf(Ship("mafield")), new[] { ("mafield", odf) }, "master", "master1");
        var line = Assert.ContainsSingle(log.Lines);
        StringAssert.EndsWith(line, plan.Reports.Single().Text);
    }

    [TestMethod]
    public void APlanWithoutGapsLogsNothing()
    {
        using var log = new CapturedLog();
        Plan(MapOf(Ship()), Fbattle, "fdreadnought");
        Assert.AreEqual(0, log.Lines.Count);
    }

    [TestMethod]
    public void ATeamUsesTheSlotItsMapRecordGives()
    {
        var map = MapOf(Ship(team: 2));
        map.TeamColors = new[] { 0, 0, 9, 0, 0, 0, 0, 0, 0 };
        Assert.AreEqual(9, Plan(map, Fbattle, "fdreadnought").Objects.Single().Slot);
    }

    [TestMethod]
    public void ARecordOfZeroUsesTheSlotOfTheTeamsOwnNumber()
    {
        var map = MapOf(Ship(team: 3), Ship(team: 0));
        map.TeamColors = new int[9];
        CollectionAssert.AreEqual(
            new[] { 3, 0 },
            Plan(map, Fbattle, "fdreadnought").Objects.Select(o => o.Slot).ToArray()
        );
    }

    [TestMethod]
    public void ASlotOutsideTheGamesSlotsIsReportedAndUsesTheStandIn()
    {
        var map = MapOf(Ship(team: 2), Ship(team: 2));
        map.TeamColors = new[] { 0, 0, 30, 0, 0, 0, 0, 0, 0 };
        var plan = Plan(map, Fbattle, "fdreadnought");
        CollectionAssert.AreEqual(new[] { 2, 2 }, plan.Objects.Select(o => o.Slot).ToArray());
        var report = plan.Reports.Single();
        CollectionAssert.AreEqual(new[] { "30" }, report.Names.ToArray());
        Assert.AreEqual(2, report.Count);
        StringAssert.Contains(report.Cause, "slot");
    }

    [TestMethod]
    public void SlotOfATeamIsTheSlotItsMapRecordGivesAndReportsNothingMore()
    {
        var map = MapOf(Ship(team: 2));
        map.TeamColors = new[] { 0, 0, 30, 0, 9, 0, 0, 0, 0 };
        var plan = Plan(map, Fbattle, "fdreadnought");
        var reported = plan.Reports.Count;

        Assert.AreEqual(9, plan.SlotOf(4));
        Assert.AreEqual(2, plan.SlotOf(2)); // the unusable record 30: the team's own slot
        Assert.AreEqual(8, plan.SlotOf(8)); // a record of 0: the team's own slot
        Assert.AreEqual(0, plan.SlotOf(40)); // a team beyond the records and the slots
        Assert.AreEqual(reported, plan.Reports.Count);
    }

    [TestMethod]
    public void ObjectsOfOneModelOnDifferentTeamsGetDifferentSlots()
    {
        var plan = Plan(MapOf(Ship(team: 1), Ship(team: 2)), Fbattle, "fdreadnought");
        CollectionAssert.AreEqual(new[] { 1, 2 }, plan.Objects.Select(o => o.Slot).ToArray());
    }

    [TestMethod]
    public void TheAmbientLightAndTheBackgroundModelAreTheMaps()
    {
        var map = MapOf();
        map.Ambient = new[] { 0.1f, 0.2f, 0.3f };
        map.Background = "mbgnebula.sod";
        var plan = Plan(map, Array.Empty<(string, string)>(), "mbgnebula");
        Assert.AreEqual(new Vector3(0.1f, 0.2f, 0.3f), plan.Ambient);
        Assert.AreEqual("mbgnebula", plan.Background);
        Assert.AreEqual(6400f, plan.Bounds.Width);
    }

    [TestMethod]
    public void AMissingBackgroundModelIsReportedAndTheMapStillPlans()
    {
        var map = MapOf(Ship());
        map.Background = "mbgstars.sod";
        var plan = Plan(map, Fbattle, "fdreadnought");
        Assert.IsNull(plan.Background);
        CollectionAssert.AreEqual(new[] { "mbgstars" }, plan.Reports.Single().Names.ToArray());
        Assert.AreEqual(1, plan.Objects.Count);
    }

    // A map can stack two objects of the same values; each is its own value, so every collection keeps both.
    [TestMethod]
    public void TwoPlacedObjectsWithIdenticalFieldsAreTwoValues()
    {
        PlacedObject Twin() =>
            new()
            {
                Label = "s",
                Class = "fbattle",
                Model = "fdreadnought",
                Team = 1,
                Slot = 0,
                Position = new Vector3(1, 2, 3),
                AxisX = Vector3.UnitX,
                AxisY = Vector3.UnitY,
                AxisZ = Vector3.UnitZ,
            };
        var (one, two) = (Twin(), Twin());
        Assert.AreNotEqual(one, two);
        Assert.AreEqual(2, new HashSet<PlacedObject> { one, two }.Count);
        Assert.AreEqual(2, new Dictionary<PlacedObject, int> { [one] = 1, [two] = 2 }.Count);
    }

    [TestMethod]
    public void AMapOfTwoIdenticalObjectsPlansBoth()
    {
        var plan = Plan(MapOf(Ship(), Ship()), Fbattle, "fdreadnought");
        Assert.AreEqual(2, plan.Objects.Count);
        Assert.AreNotSame(plan.Objects[0], plan.Objects[1]);
        Assert.AreEqual(2, plan.Objects.Distinct().Count());
    }

    const string FieldKeys =
        "classLabel = asteroidfield\nminAsteroids = 1\nmaxAsteroids = 2\nfieldRadius = 100\nrotationSpeed = 1\norbitingSpeed = 1";

    static readonly string[] Masters =
    [
        "master",
        "master1",
        "master2",
        "master3",
        "master4",
        "master5",
        "master6",
        "master7",
    ];

    // the class-specific tokens of an asteroid field object (loader 0x44f8e0): have_belt, and for a belt the closest offset, three floats,
    // and the path label with its length
    static byte[][] BeltRecord(string label) =>
        [
            BznBuilder.Bool(true),
            BznBuilder.Vec(Vector3.Zero),
            BznBuilder.Float(1),
            BznBuilder.Float(1),
            BznBuilder.Float(0.04f),
            BznBuilder.Int(label.Length),
            BznBuilder.Str(label, label.Length),
        ];

    static BznBuilder.Obj FieldObject(string label, byte[][]? prefix, Vector3? at = null) =>
        Ship("mafield", 0, at ?? new Vector3(500, 20, 700), label: label) with
        {
            Prefix = prefix,
        };

    [TestMethod]
    public void AnAsteroidFieldWithoutABeltIsASiteAtItsMirroredPosition()
    {
        var plan = Plan(MapOf(FieldObject("f", [BznBuilder.Bool(false)])), new[] { ("mafield", FieldKeys) }, Masters);
        var site = plan.AsteroidSites.Single();
        Assert.AreEqual(("f", "mafield", new Vector3(500, 20, -700)), (site.Label, site.Class, site.Center));
        Assert.IsNull(site.Belt);
        Assert.AreEqual(0, plan.AsteroidSites.Count(s => s.Belt != null));
        Assert.AreEqual(0, plan.Reports.Count);
    }

    [TestMethod]
    public void AFieldWithABeltOfATypeThreePathIsABeltAlongThePathsMirroredPoints()
    {
        var map = MapOf(FieldObject("f", BeltRecord("path_1")));
        map.Paths.Add(("other", new[] { new Vector3(9, 9, 9) }));
        map.Paths.Add(("path_1", new[] { new Vector3(1, 20, 2), new Vector3(30, 20, 40) }));
        map.PathTypes["path_1"] = 3;
        var plan = Plan(map, new[] { ("mafield", FieldKeys) }, Masters);
        var belt = plan.AsteroidSites.Single(s => s.Belt != null);
        CollectionAssert.AreEqual(new[] { new Vector3(1, 20, -2), new Vector3(30, 20, -40) }, belt.Belt!.ToArray());
        Assert.AreEqual(new Vector3(500, 20, -700), belt.Center);
        Assert.AreEqual(0, plan.Reports.Count);
    }

    [TestMethod]
    public void ABeltWhosePathIsNotOfTypeThreeIsAPlainFieldAsTheGameClearsIt()
    {
        var map = MapOf(FieldObject("f", BeltRecord("path_1")));
        map.Paths.Add(("path_1", new[] { new Vector3(1, 20, 2) }));
        map.PathTypes["path_1"] = 2;
        var plan = Plan(map, new[] { ("mafield", FieldKeys) }, Masters);
        Assert.IsNull(plan.AsteroidSites.Single().Belt);
        Assert.AreEqual(0, plan.Reports.Count);
    }

    [TestMethod]
    public void ABeltWhosePathIsMissingIsAPlainFieldAndAGapNamesThePath()
    {
        var plan = Plan(
            MapOf(FieldObject("a", BeltRecord("gone")), FieldObject("b", BeltRecord("gone"))),
            new[] { ("mafield", FieldKeys) },
            Masters
        );
        Assert.AreEqual(2, plan.AsteroidSites.Count);
        Assert.IsTrue(plan.AsteroidSites.All(s => s.Belt == null));
        var report = plan.Reports.Single();
        CollectionAssert.AreEqual(new[] { "gone" }, report.Names.ToArray());
        Assert.AreEqual(2, report.Count);
    }

    [TestMethod]
    public void ABeltRecordThatCannotBeReadIsAGapAndTheFieldStaysAPlainSite()
    {
        var truncated = new[] { BznBuilder.Bool(true), BznBuilder.Int(5) };
        var plan = Plan(MapOf(FieldObject("f", truncated)), new[] { ("mafield", FieldKeys) }, Masters);
        Assert.IsNull(plan.AsteroidSites.Single().Belt);
        CollectionAssert.AreEqual(new[] { "mafield" }, plan.Reports.Single().Names.ToArray());
        StringAssert.Contains(plan.Reports.Single().Cause, "belt");
    }

    [TestMethod]
    public void AFieldWithNoClassRecordOfItsOwnIsAPlainSite()
    {
        // a layout's field has no tokens; the plan reads both through the same code (INV-0009)
        var plan = Plan(MapOf(FieldObject("f", null)), new[] { ("mafield", FieldKeys) }, Masters);
        Assert.IsNull(plan.AsteroidSites.Single().Belt);
        Assert.AreEqual(0, plan.Reports.Count);
    }

    [TestMethod]
    public void AFieldWhoseRocksCannotBePlannedIsStillASite()
    {
        var plan = Plan(MapOf(FieldObject("f", null)), new[] { ("mafield", "classLabel = asteroidfield") }, Masters);
        Assert.AreEqual(0, plan.Fields.Count);
        Assert.AreEqual(1, plan.AsteroidSites.Count);
    }

    const string NebulaKeys =
        "classLabel = nebula_obj\neffectRadius = 100\ncraft_speed_coefficient = 0.6\ncraft_speed_limit = 3\npathing_cost = 400";

    [TestMethod]
    public void ANebulaIsPlannedWithItsKindRadiusAndPathCostAtItsMirroredPosition()
    {
        var plan = Plan(
            MapOf(Ship("mnebula3", 0, new Vector3(1000, 0, 2000), label: "neb")),
            new[] { ("mnebula3", NebulaKeys) }
        );
        var nebula = plan.Nebulae.Single();
        Assert.AreEqual("neb", nebula.Label);
        Assert.AreEqual("mnebula3", nebula.Class);
        Assert.AreEqual(3, nebula.Kind);
        Assert.AreEqual(new Vector3(1000, 0, -2000), nebula.Center);
        Assert.AreEqual(100f, nebula.EffectRadius);
        Assert.AreEqual(400f, nebula.PathingCost);
        Assert.AreEqual(0, plan.Reports.Count);
        Assert.AreEqual(0, plan.Objects.Count); // its model is not in the install, and no gap says so: nothing new is drawn
    }

    [TestMethod]
    public void ANebulaPlansTheKindsValuesAndTheGridGivesThemToAShipInIt()
    {
        var plan = Plan(MapOf(Ship("mnebula3", 0, new Vector3(1000, 0, 2000))), new[] { ("mnebula3", NebulaKeys) });
        var kind = plan.NebulaKinds.Single();
        Assert.AreEqual((3, 0.6f, 3f), (kind.Kind, kind.Coefficient, kind.Limit));
        Assert.AreEqual(kind, plan.NebulaGrid.At(new Vector3(1010, 30, -2010)).Single());
        Assert.AreEqual(0, plan.NebulaGrid.At(new Vector3(1500, 30, -2010)).Count);
    }

    [TestMethod]
    public void ANebulaOfAnyOtherClassNameIsKindFiveAndTakesTheValuesOfMnebulaFive()
    {
        var plan = Plan(
            MapOf(Ship("mxneb", 0, new Vector3(1000, 0, 2000))),
            new[]
            {
                ("mxneb", "classLabel = nebula_obj\neffectRadius = 100\ncraft_speed_coefficient = 0.1"),
                ("mnebula5", "classLabel = nebula_obj\neffectRadius = 100\ncraft_speed_coefficient = 0.8"),
            }
        );
        Assert.AreEqual(5, plan.Nebulae.Single().Kind);
        Assert.AreEqual(0.8f, plan.NebulaKinds.Single().Coefficient); // the class of the kind, though the map places none
    }

    [TestMethod]
    public void ANebulaClassOfAnotherCaseThanTheKindNameIsKindFiveAsTheGameComparesTheNameExactly()
    {
        var plan = Plan(
            MapOf(Ship("MNebula3", 0, new Vector3(1000, 0, 2000))),
            new[]
            {
                ("MNebula3", "classLabel = nebula_obj\neffectRadius = 100"),
                ("mnebula5", "classLabel = nebula_obj\neffectRadius = 100\ncraft_speed_coefficient = 0.8"),
            }
        );
        Assert.AreEqual(5, plan.Nebulae.Single().Kind);
        Assert.AreEqual(5, plan.NebulaKinds.Single().Kind);
    }

    const string RtsCfg = "int CANNOT_PATH_INTO_COST = 300;";

    // Plans a map whose install holds the definitions and, where given, the game constants of RTS_CFG.h.
    SandboxPlan PlanWithConstants(BznBuilder map, (string Class, string Odf)[] definitions, string? constants)
    {
        var files = definitions.Select(d => ($"addon/{d.Class}.odf", d.Odf)).ToList();
        if (constants != null)
        {
            files.Add(("RTS_CFG.h", constants));
        }

        var install = new GameFileSystem(temp.MakeInstall(files.ToArray()));
        var asset = new MapAsset
        {
            Name = "bzn/t.bzn",
            Map = Bzn.Read(map.Build()),
            Starts = Array.Empty<MapStart>(),
            Overview = null,
            ScriptedPaths = Array.Empty<MapPath>(),
        };

        return SandboxPlan.Build(asset, SandboxPlan.Definitions(install, asset), _ => true);
    }

    [TestMethod]
    public void AnOmittedSpeedKeyTakesTheGamesDefaultsWithoutAGap()
    {
        var plan = PlanWithConstants(
            MapOf(Ship("mnebula1", 0)),
            new[] { ("mnebula1", "classLabel = nebula_obj\neffectRadius = 100\npathing_cost = 5") },
            RtsCfg
        );
        Assert.AreEqual((0.5f, -1f), (plan.NebulaKinds.Single().Coefficient, plan.NebulaKinds.Single().Limit));
        Assert.AreEqual(0, plan.Reports.Count);
    }

    [TestMethod]
    public void AnOmittedPathingCostIsOneBelowCannotPathIntoCostOfTheInstallsConstants()
    {
        var plan = PlanWithConstants(
            MapOf(Ship("mnebula1", 0)),
            new[] { ("mnebula1", "classLabel = nebula_obj\neffectRadius = 100") },
            RtsCfg
        );
        Assert.AreEqual(299f, plan.Nebulae.Single().PathingCost);
        Assert.AreEqual(0, plan.Reports.Count);
    }

    [TestMethod]
    public void AnOmittedPathingCostWithoutTheConstantIsThe199OfTheGamesStaticValueAndOneGap()
    {
        var plan = PlanWithConstants(
            MapOf(Ship("mnebula1", 0, label: "a"), Ship("mnebula1", 0, label: "b")),
            new[] { ("mnebula1", "classLabel = nebula_obj\neffectRadius = 100") },
            null
        );
        Assert.AreEqual(2, plan.Nebulae.Count);
        Assert.IsTrue(plan.Nebulae.All(n => n.PathingCost == 199f));
        var report = plan.Reports.Single(r => r.Cause.Contains("CANNOT_PATH_INTO_COST", StringComparison.Ordinal));
        Assert.AreEqual(1, plan.Reports.Count);
        Assert.AreEqual(1, report.Names.Count);
    }

    [TestMethod]
    [DataRow("int OTHER_CONSTANT = 300;")] // the header lacks the constant
    [DataRow("int CANNOT_PATH_INTO_COST = lots;")] // the constant is no number
    public void AnOmittedPathingCostWithAHeaderThatGivesNoNumberIsTheStaticValueAndOneGap(string constants)
    {
        var plan = PlanWithConstants(
            MapOf(Ship("mnebula1", 0)),
            new[] { ("mnebula1", "classLabel = nebula_obj\neffectRadius = 100") },
            constants
        );
        Assert.AreEqual(199f, plan.Nebulae.Single().PathingCost);
        Assert.AreEqual(
            1,
            plan.Reports.Count(r => r.Cause.Contains("CANNOT_PATH_INTO_COST", StringComparison.Ordinal))
        );
    }

    [TestMethod]
    public void ANebulaWithAPathingCostNeedsNoConstantAndReportsNoGap()
    {
        var plan = PlanWithConstants(
            MapOf(Ship("mnebula1", 0)),
            new[] { ("mnebula1", "classLabel = nebula_obj\neffectRadius = 100\npathing_cost = 5") },
            null
        );
        Assert.AreEqual(5f, plan.Nebulae.Single().PathingCost);
        Assert.AreEqual(0, plan.Reports.Count);
    }

    [TestMethod]
    public void OnlyTheKindClassesOfThePlacedNebulaeAreResolved()
    {
        // mnebula1 is in the install and cannot be read, and no placed nebula is of kind 1: no ERROR, no gap
        var archive = System.Text.Encoding.Latin1.GetString(
            ZfsBuilder.Build(0, ZfsBuilder.M("mnebula1.odf", "classLabel = nebula_obj", 2))
        );
        var install = new GameFileSystem(
            temp.MakeInstall(("addon/mnebula3.odf", NebulaKeys), ("RTS_CFG.h", RtsCfg), ("addon/x.zfs", archive))
        );
        var asset = new MapAsset
        {
            Name = "bzn/t.bzn",
            Map = Bzn.Read(MapOf(Ship("mnebula3", 0)).Build()),
            Starts = Array.Empty<MapStart>(),
            Overview = null,
            ScriptedPaths = Array.Empty<MapPath>(),
        };
        using var log = new CapturedLog();
        var plan = SandboxPlan.Build(asset, SandboxPlan.Definitions(install, asset), _ => true);
        Assert.AreEqual(3, plan.NebulaKinds.Single().Kind);
        Assert.AreEqual(0, plan.Reports.Count);
        Assert.AreEqual(0, log.Lines.Count(l => l.Contains("mnebula1", StringComparison.Ordinal)), log.Text);
    }

    [TestMethod]
    public void ANebulaWithoutAUsableEffectRadiusIsAGapNamingItAndTheMapOpens()
    {
        var plan = Plan(
            MapOf(Ship("mnebula1", 0, label: "a"), Ship("mnebula2", 0, label: "b"), Ship()),
            new[]
            {
                ("mnebula1", "classLabel = nebula_obj"),
                ("mnebula2", "classLabel = nebula_obj\neffectRadius = wide"),
                ("fbattle", "baseName = fdreadnought"),
            },
            "fdreadnought"
        );
        Assert.AreEqual(0, plan.Nebulae.Count);
        Assert.AreEqual(1, plan.Objects.Count);
        var report = plan.Reports.Single(r => r.Cause.Contains("effectRadius", StringComparison.Ordinal));
        CollectionAssert.AreEquivalent(new[] { "mnebula1", "mnebula2" }, report.Names.ToArray());
        Assert.AreEqual(2, report.Count);
    }

    [TestMethod]
    public void ASpeedKeyThatIsNoNumberIsAGapAndTheGamesDefaultIsUsed()
    {
        var plan = Plan(
            MapOf(Ship("mnebula3", 0)),
            new[]
            {
                (
                    "mnebula3",
                    "classLabel = nebula_obj\neffectRadius = 100\npathing_cost = 5\ncraft_speed_coefficient = slow"
                ),
            }
        );
        Assert.AreEqual(0.5f, plan.NebulaKinds.Single().Coefficient);
        CollectionAssert.AreEqual(new[] { "mnebula3: craft_speed_coefficient" }, plan.Reports.Single().Names.ToArray());
    }

    [TestMethod]
    public void ANebulaKindClassThatCannotBeReadIsAGapNamingItAndTheGamesDefaultsAreUsed()
    {
        var archive = System.Text.Encoding.Latin1.GetString(
            ZfsBuilder.Build(0, ZfsBuilder.M("mnebula5.odf", "classLabel = nebula_obj", 2))
        );
        var install = new GameFileSystem(
            temp.MakeInstall(
                ("addon/mxneb.odf", "classLabel = nebula_obj\neffectRadius = 100"),
                ("addon/x.zfs", archive)
            )
        );
        var asset = new MapAsset
        {
            Name = "bzn/t.bzn",
            Map = Bzn.Read(MapOf(Ship("mxneb", 0)).Build()),
            Starts = Array.Empty<MapStart>(),
            Overview = null,
            ScriptedPaths = Array.Empty<MapPath>(),
        };
        using var log = new CapturedLog();
        var plan = SandboxPlan.Build(asset, SandboxPlan.Definitions(install, asset), _ => true);
        Assert.AreEqual(1, plan.Nebulae.Count);
        Assert.AreEqual(0.5f, plan.NebulaKinds.Single().Coefficient);
        var report = plan.Reports.Single(r => r.Cause.Contains("nebula kind", StringComparison.Ordinal));
        CollectionAssert.AreEqual(new[] { "mnebula5" }, report.Names.ToArray());
        Assert.AreEqual(1, log.Lines.Count(l => l.Contains("mnebula5", StringComparison.Ordinal)), log.Text); // one ERROR, no WARN
    }
}
