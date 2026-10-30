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

// The movement values of a map's ships, read from physics definitions built in code (INV-0001).
[TestClass]
public class MovePlanTests
{
    readonly TempInstall temp = new();

    [TestCleanup]
    public void CleanUp() => temp.CleanUp();

    const string Ship = "physicsFile = \"p.odf\"\r\n";

    // Every key a physics definition gives for the flight, at values of the notes' stock fastphys.odf with an impulse speed
    // of 12.5, tooCloseToTurn 40, and pathLeadDistance 150: a definition that gives them all draws no report.
    static readonly (string Key, string Value)[] AllKeys =
    [
        ("impulseSpeed", "12.5"),
        ("velocCoeffStill", "0.3"),
        ("velocWhenCoeffHalf", "0.7"),
        ("pitchRotationSpeed", "0.0667"),
        ("maxPitch", "45"),
        ("rollRotationSpeed", "0.5"),
        ("maxRoll", "40"),
        ("turnRadius", "7.5"),
        ("frontBackInertia", "7.5"),
        ("maxReverseSpeed", "4"),
        ("upDownInertia", "5"),
        ("maxVerticalSpeed", "0.667"),
        ("leftRightInertia", "5"),
        ("braccelControlStiffness", "1.5"),
        ("sy_rollVelocCoeff", "0"),
        ("sy_rollAngleCoeff", "0"),
        ("sy_yawVelocCoeff", "0.15"),
        ("sy_yawAngleCoeff", "1"),
        ("sp_pitchVelocCoeff", "0"),
        ("sp_pitchAngleCoeff", "-1.197"),
        ("sp_velocAngleCoeff", "0.06"),
        ("tooCloseToTurn", "40"),
        ("pathLeadDistance", "150"),
    ];

    // The definition text of AllKeys with the keys of `without` left out and those of `with` set (or added).
    static string Odf(string[]? without = null, params (string Key, string Value)[] with)
    {
        var values = AllKeys.ToDictionary(k => k.Key, k => k.Value);
        foreach (var key in without ?? [])
        {
            values.Remove(key);
        }

        foreach (var (key, value) in with)
        {
            values[key] = value;
        }

        return string.Join("\r\n", values.Select(p => $"{p.Key} = {p.Value}"));
    }

    // The keys of an Odf with the given changes, as the engine takes them.
    static PhysicsKeys Keys(params (string Key, double Value)[] with) =>
        PhysicsKeys.Game.With(
            AllKeys
                .Where(k => k.Key is not ("tooCloseToTurn" or "pathLeadDistance"))
                // the engine reads each number as a float
                .Select(k => (k.Key, (double)float.Parse(k.Value, System.Globalization.CultureInfo.InvariantCulture)))
                .Concat(with)
                .ToArray()
        );

    // `physicsFile` is the install file that holds p.odf (a loose file, or an archive given as Latin-1 text);
    // `fastphys` is the text of fastphys.odf (null: the install has none). `movable` takes every ship as Movable, so
    // MovePlan reads a physics definition the unit plan found absent.
    MovePlan Plan(
        string[] classes,
        (string Class, string Odf)[] definitions,
        string? physics,
        string physicsFile = "addon/p.odf",
        bool movable = false,
        string? fastphys = null,
        bool noFast = false
    )
    {
        var loose = new List<(string, string)>();
        if (physics != null)
        {
            loose.Add((physicsFile, physics));
        }

        if (!noFast)
        {
            loose.Add(("addon/fastphys.odf", fastphys ?? Odf(with: ("impulseSpeed", "20"))));
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

        var units = UnitPlan.Build(install, plan, _ => true).Units.Select(u => movable ? u with { Movable = true } : u);

        return MovePlan.Build(plan, units, install);
    }

    MovePlan One(string physics, string? fastphys = null, bool noFast = false) =>
        Plan(["ship"], [("ship", Ship)], physics, fastphys: fastphys, noFast: noFast);

    [TestMethod]
    public void AShipsMovementValuesAreReadFromItsPhysicsDefinition()
    {
        var plan = One(Odf());
        var values = plan.Physics.Values.Single();
        Assert.AreEqual(12.5, values.ImpulseSpeed);
        Assert.AreEqual(40, values.TooCloseToTurn);
        Assert.AreEqual(150, values.PathLeadDistance);
        Assert.AreEqual(FlightCoefficients.Derive(Keys()), values.Coefficients);
        Assert.AreEqual(0, plan.Reports.Count);
    }

    [TestMethod]
    public void AShipTakesTheCoefficientsOfFastphysForItsFirstMoveAndKeepsItsOwnSpeedAndDistances()
    {
        var plan = One(
            Odf(),
            fastphys: Odf(with: [("impulseSpeed", "20"), ("turnRadius", "9"), ("tooCloseToTurn", "5")])
        );
        var values = plan.Physics.Values.Single();
        Assert.AreEqual(FlightCoefficients.Derive(Keys(("impulseSpeed", 20), ("turnRadius", 9))), values.Fast);
        Assert.AreEqual(12.5, values.ImpulseSpeed);
        Assert.AreEqual(40, values.TooCloseToTurn);
        Assert.AreEqual(0, plan.Reports.Count);
    }

    [TestMethod]
    public void AMissingFastphysIsReportedOnceAndTheShipsKeepTheirOwnCoefficients()
    {
        var plan = Plan(["a", "b", "a"], [("a", Ship), ("b", Ship)], Odf(), noFast: true);
        Assert.IsTrue(plan.Physics.Values.All(p => p.Fast == null));
        var report = plan.Reports.Single();
        CollectionAssert.AreEqual(new[] { "fastphys.odf" }, report.Names.ToArray());
        StringAssert.Contains(report.Text, "fastphys.odf missing");
    }

    [TestMethod]
    public void AFastphysKeyThatIsMissingTakesTheGamesDefaultAndIsReportedOnce()
    {
        var plan = Plan(
            ["a", "b"],
            [("a", Ship), ("b", Ship)],
            Odf(),
            fastphys: Odf(["turnRadius"], ("impulseSpeed", "20"))
        );
        var report = plan.Reports.Single();
        StringAssert.Contains(report.Text, "fastphys.odf lacks turnRadius");
        StringAssert.Contains(report.Text, "25");
        Assert.AreEqual(25, plan.Physics.Values.First().Fast!.TurnRadius);
    }

    [TestMethod]
    public void AFastphysThatCannotBeReadIsOneErrorEntryAndNoWarn()
    {
        var archive = System.Text.Encoding.Latin1.GetString(
            ZfsBuilder.Build(0, ZfsBuilder.M("fastphys.odf", "impulseSpeed = 20", 2))
        );
        using var log = new CapturedLog();
        var install = new GameFileSystem(
            temp.MakeInstall(("addon/ship.odf", Ship), ("addon/p.odf", Odf()), ("addon/x.zfs", archive))
        );
        var map = new BznBuilder { Background = "" };
        map.Objects.Add(
            new BznBuilder.Obj
            {
                Class = "ship",
                Team = 1,
                Position = Vector3.Zero,
                Label = "ship",
            }
        );
        var asset = new MapAsset
        {
            Name = "bzn/t.bzn",
            Map = Bzn.Read(map.Build()),
            Starts = Array.Empty<MapStart>(),
            Overview = null,
            ScriptedPaths = Array.Empty<MapPath>(),
        };
        var plan = SandboxPlan.Build(asset, SandboxPlan.Definitions(install, asset), _ => true);
        var moves = MovePlan.Build(plan, UnitPlan.Build(install, plan, _ => true).Units, install);
        Assert.IsNull(moves.Physics.Values.Single().Fast);
        var entries = log.Lines.Where(l => l.Contains("fastphys.odf")).ToList();
        StringAssert.Matches(entries.Single(), new Regex(@" ERROR sandbox: physics definition fastphys\.odf: \S"));
    }

    // The Borg keys of the stock bbatphys.odf: the yaw spring's and tooSlowToTurn.
    const string BorgKeys =
        "\r\ntooSlowToTurn = 5\r\nrotationAcceleration = 2.5\r\nmaxRotationSpeed = 0.75\r\nrotationStiffness = 1.5";

    [TestMethod]
    [DataRow("\r\nborgPhysics = 1" + BorgKeys, true)]
    [DataRow("\r\nborgPhysics = 0", false)]
    [DataRow("", false)]
    public void ABorgPhysicsFlagIsReadFromThePhysicsDefinitionAndNeedsNoReport(string key, bool borg)
    {
        var plan = One(Odf() + key);
        Assert.AreEqual(borg, plan.Physics.Values.Single().BorgPhysics);
        Assert.AreEqual(0, plan.Reports.Count);
    }

    [TestMethod]
    public void ABorgShipReadsItsYawSpringKeysAndTooSlowToTurn()
    {
        var values = One(Odf() + "\r\nborgPhysics = 1" + BorgKeys).Physics.Values.Single();
        Assert.AreEqual(5, values.TooSlowToTurn);
        Assert.AreEqual(2.5, values.RotationAcceleration);
        Assert.AreEqual(0.75, values.MaxRotationSpeed);
        Assert.AreEqual(1.5, values.RotationStiffness);
    }

    [TestMethod]
    public void ABorgShipWithoutTheKeysTakesTheGamesDefaultsAndEachGapIsReported()
    {
        var plan = One(Odf() + "\r\nborgPhysics = 1\r\nrotationStiffness = 0");
        var values = plan.Physics.Values.Single();
        Assert.AreEqual(PhysicsValues.DefaultTooSlowToTurn, values.TooSlowToTurn);
        Assert.AreEqual(PhysicsValues.DefaultRotationAcceleration, values.RotationAcceleration);
        Assert.AreEqual(PhysicsValues.DefaultMaxRotationSpeed, values.MaxRotationSpeed);
        Assert.AreEqual(PhysicsValues.DefaultRotationStiffness, values.RotationStiffness);
        var text = string.Join("\n", plan.Reports.Select(r => r.Text));
        foreach (var key in new[] { "tooSlowToTurn", "rotationAcceleration", "maxRotationSpeed", "rotationStiffness" })
        {
            StringAssert.Contains(text, key);
        }
    }

    [TestMethod]
    public void ABorgPhysicsValueThatIsNoNumberIsReportedAndLeftOff()
    {
        var plan = One(Odf() + "\r\nborgPhysics = yes");
        Assert.IsFalse(plan.Physics.Values.Single().BorgPhysics);
        StringAssert.Contains(plan.Reports.Single().Text, "borgPhysics");
    }

    [TestMethod]
    public void ADefinitionWithoutYawRotationRateMovesToo()
    {
        var plan = One(Odf());
        Assert.AreEqual(1, plan.Physics.Count);
        Assert.IsFalse(plan.Reports.Any(r => r.Text.Contains("yawRotationRate")));
    }

    [TestMethod]
    public void StationsGetNoMovementValues()
    {
        var plan = Plan(["base"], [("base", Ship + "is_starbase = 1")], Odf());
        Assert.AreEqual(0, plan.Physics.Count);
    }

    [TestMethod]
    public void AMissingKeyTakesTheGamesDefaultAndIsReportedOncePerCauseWithNamesAndACount()
    {
        var plan = Plan(["a", "b", "a"], [("a", Ship), ("b", Ship)], Odf(["velocWhenCoeffHalf"]));
        Assert.AreEqual(3, plan.Physics.Count);
        var report = plan.Reports.Single();
        CollectionAssert.AreEqual(new[] { "a", "b" }, report.Names.ToArray());
        Assert.AreEqual(3, report.Count);
        StringAssert.Contains(report.Text, "velocWhenCoeffHalf");
        StringAssert.Contains(report.Text, "7.5");
        Assert.AreEqual(
            FlightCoefficients.Derive(Keys(("velocWhenCoeffHalf", 7.5))),
            plan.Physics.Values.First().Coefficients
        );
    }

    [TestMethod]
    public void AGroupedMissingKeyIsOneWarnEntryWithTheLabelLine()
    {
        using var log = new CapturedLog();
        var plan = Plan(["a", "b", "a"], [("a", Ship), ("b", Ship)], Odf(["maxPitch"]));
        var line = Assert.ContainsSingle(log.Lines);
        StringAssert.EndsWith(line, " WARN sandbox: " + plan.Reports.Single().Text);
    }

    [TestMethod]
    public void AShipWithoutAnImpulseSpeedTakesItsForwardSpeedFromMaxForwardSpeedAndReportsTheKey()
    {
        var plan = One(Odf(["impulseSpeed"], ("maxForwardSpeed", "15")));
        var values = plan.Physics.Values.Single();
        Assert.AreEqual(15, values.ImpulseSpeed);
        Assert.AreEqual(
            FlightCoefficients.Derive(Keys(("impulseSpeed", -1), ("maxForwardSpeed", 15))),
            values.Coefficients
        );
        StringAssert.Contains(plan.Reports.Single(r => r.Text.Contains("impulseSpeed")).Text, "impulseSpeed");
    }

    [TestMethod]
    public void MaxForwardSpeedIsReadOnlyWithoutAnImpulseSpeed()
    {
        var plan = One(Odf());
        Assert.AreEqual(0, plan.Reports.Count, "no report for maxForwardSpeed, which the game does not read here");
    }

    [TestMethod]
    public void EachMissingKeyIsItsOwnCause()
    {
        var plan = One("");
        Assert.AreEqual(AllKeys.Length + 1, plan.Reports.Count); // maxForwardSpeed is read too, with no impulseSpeed
    }

    [TestMethod]
    [DataRow("frontBackInertia", "0")]
    [DataRow("frontBackInertia", "-2")]
    [DataRow("upDownInertia", "0")]
    [DataRow("leftRightInertia", "0")]
    [DataRow("maxRoll", "0")]
    [DataRow("maxRoll", "-30")]
    [DataRow("turnRadius", "0")]
    [DataRow("velocWhenCoeffHalf", "0")]
    [DataRow("velocCoeffStill", "1")]
    [DataRow("maxPitch", "0")]
    [DataRow("rollRotationSpeed", "0")]
    [DataRow("rollRotationSpeed", "-1")]
    [DataRow("pitchRotationSpeed", "fast")]
    public void ADegenerateValueIsReportedAndTheGamesDefaultStandsInSoTheCoefficientsAreFinite(string key, string value)
    {
        var plan = One(Odf(with: (key, value)));
        var report = plan.Reports.Single();
        StringAssert.Contains(report.Text, key);
        StringAssert.Contains(report.Text, "stand-in");
        Assert.IsTrue(plan.Physics.Values.Single().Coefficients.IsUsable);
        Assert.IsTrue(plan.Physics.Values.Single().Fast!.IsUsable);
    }

    [TestMethod]
    public void ADegenerateValueIsReportedOncePerCauseWithNamesAndACount()
    {
        var plan = Plan(["a", "b", "a"], [("a", Ship), ("b", Ship)], Odf(with: ("maxRoll", "-30")));
        var report = plan.Reports.Single();
        CollectionAssert.AreEqual(new[] { "a", "b" }, report.Names.ToArray());
        Assert.AreEqual(3, report.Count);
        StringAssert.Contains(report.Text, "maxRoll");
    }

    [TestMethod]
    public void AMissingRollRotationSpeedIsReportedAndTakesAStandInThatDampsTheRoll()
    {
        // the loader's own default of -1 gives the roll negative damping: the roll would grow without bound
        var plan = One(Odf(["rollRotationSpeed"]));
        var report = plan.Reports.Single();
        StringAssert.Contains(report.Text, "rollRotationSpeed");
        StringAssert.Contains(report.Text, "stand-in");
        Assert.IsTrue(plan.Physics.Values.Single().Coefficients.RollRate > 0);
    }

    [TestMethod]
    public void AHeldShipWithoutARollRotationSpeedLevelsOutFromARoll()
    {
        // a hold keeps the ship's own coefficients (no swap to fastphys), so the missing key's value decides the roll's damping
        var ship = new FlightModel(
            One(Odf(["rollRotationSpeed"])).Physics.Values.Single(),
            FlightFixtures.Pose(Vector3.Zero) with
            {
                Roll = 0.2f,
            }
        );
        for (var i = 0; i < 6000; i++)
        {
            ship.Brake(CombatBand.Stock);
            ship.Step(0.02, false, true);
        }

        Assert.IsTrue(Math.Abs(ship.Pose.Roll) < 0.01f, "the roll settles");
        Assert.IsTrue(ship.Speed < 1, "and the ship is at rest");
    }

    [TestMethod]
    public void ADefinitionWhoseGainDenominatorReachesZeroUnderTheVelocityLimitIsReportedAndTakesTheDefaults()
    {
        // velocCoeffStill 0.95 gives the control gain a negative slope: 20 - 2.06 v is zero near v = 10
        var plan = One(Odf(with: ("velocCoeffStill", "0.95")));
        var report = plan.Reports.Single();
        StringAssert.Contains(report.Text, "no usable flight coefficients");
        Near(1 / (1 - 0.1), plan.Physics.Values.Single().Coefficients.GainBase, 1e-4);
    }

    static void Near(double expected, double actual, double tolerance) => Assert.AreEqual(expected, actual, tolerance);

    [TestMethod]
    public void AShipWithADegenerateValueFliesWithAFinitePose()
    {
        var plan = One(Odf(with: [("frontBackInertia", "0"), ("maxRoll", "0")]));
        var ship = new FlightModel(plan.Physics.Values.Single(), FlightFixtures.Pose(Vector3.Zero));
        ship.StartMove([Vector3.Zero, new Vector3(0, 38.5f, 300)]);
        for (var i = 0; i < 500; i++)
        {
            ship.Follow(CombatBand.Stock);
            ship.Step(0.02, false, true);
        }

        var p = ship.Pose;
        Assert.IsTrue(float.IsFinite(p.Position.X + p.Position.Y + p.Position.Z + p.Pitch + p.Yaw + p.Roll));
    }

    [TestMethod]
    public void EachShipsTooCloseToTurnComesFromItsOwnPhysicsDefinition()
    {
        var install = new GameFileSystem(
            temp.MakeInstall([
                ("addon/a.odf", "physicsFile = \"pa.odf\"\r\n"),
                ("addon/b.odf", "physicsFile = \"pb.odf\"\r\n"),
                ("addon/pa.odf", Odf(with: ("tooCloseToTurn", "40;"))), // the stock destphys.odf closes the value with a semicolon
                ("addon/pb.odf", Odf(with: ("tooCloseToTurn", "50"))),
                ("addon/fastphys.odf", Odf()),
            ])
        );
        var map = new BznBuilder { Background = "" };
        foreach (var c in new[] { "a", "b" })
        {
            map.Objects.Add(
                new BznBuilder.Obj
                {
                    Class = c,
                    Team = 1,
                    Position = Vector3.Zero,
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
        var moves = MovePlan.Build(plan, UnitPlan.Build(install, plan, _ => true).Units, install);
        CollectionAssert.AreEqual(new[] { 40.0, 50.0 }, moves.Physics.Values.Select(p => p.TooCloseToTurn).ToArray());
        Assert.AreEqual(0, moves.Reports.Count);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("0")]
    [DataRow("-5")]
    [DataRow("near")]
    public void ATooCloseToTurnThatIsMissingOrNoPositiveNumberTakesTheDefaultAndIsReportedOncePerClass(string value)
    {
        var plan = Plan(
            ["a", "b", "a"],
            [("a", Ship), ("b", Ship)],
            value == "" ? Odf(["tooCloseToTurn"]) : Odf(with: ("tooCloseToTurn", value))
        );
        Assert.IsTrue(plan.Physics.Values.All(p => p.TooCloseToTurn == 25));
        var report = plan.Reports.Single();
        CollectionAssert.AreEqual(new[] { "a", "b" }, report.Names.ToArray());
        Assert.AreEqual(3, report.Count);
        StringAssert.Contains(report.Text, "tooCloseToTurn");
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("0")]
    [DataRow("near")]
    public void APathLeadDistanceThatIsMissingOrNoPositiveNumberTakesTheRootOf30AndIsReportedOnce(string value)
    {
        var plan = One(value == "" ? Odf(["pathLeadDistance"]) : Odf(with: ("pathLeadDistance", value)));
        Assert.AreEqual(Math.Sqrt(30), plan.Physics.Values.Single().PathLeadDistance);
        StringAssert.Contains(plan.Reports.Single().Text, "pathLeadDistance");
    }

    [TestMethod]
    public void APhysicsDefinitionThatCannotBeReadIsReportedOncePerCauseWithNamesAndACount()
    {
        // a compressed archive entry: the install lists p.odf, but reading it throws UnsupportedFormatException
        var archive = System.Text.Encoding.Latin1.GetString(
            ZfsBuilder.Build(0, ZfsBuilder.M("p.odf", "impulseSpeed = 1", 2))
        );
        var plan = Plan(["a", "b", "a"], [("a", Ship), ("b", Ship)], archive, "addon/x.zfs");
        Assert.AreEqual(0, plan.Physics.Count);
        var report = plan.Reports.Single();
        CollectionAssert.AreEqual(new[] { "a", "b" }, report.Names.ToArray());
        Assert.AreEqual(3, report.Count);
        StringAssert.Contains(report.Text, "physics definition cannot be read");
    }

    [TestMethod]
    public void APhysicsDefinitionThatCannotBeReadIsOneErrorEntryWithItsNameAndReason()
    {
        // a compressed archive entry, used by two ships: an asset failed to load, one cause (INV-0005, INV-0002)
        var archive = System.Text.Encoding.Latin1.GetString(
            ZfsBuilder.Build(0, ZfsBuilder.M("p.odf", "impulseSpeed = 1", 2))
        );
        using var log = new CapturedLog();
        Plan(["a", "b"], [("a", Ship), ("b", Ship)], archive, "addon/x.zfs");
        var entries = log.Lines.Where(l => l.Contains("physics definition")).ToList();
        StringAssert.Matches(entries.Single(), new Regex(@" ERROR sandbox: physics definition p\.odf: \S"));
    }

    [TestMethod]
    public void AMissingPhysicsDefinitionIsOneWarnEntryAndNoError()
    {
        // absent data, not an asset that failed to load (INV-0005): the grouped gap's WARN, no ERROR
        using var log = new CapturedLog();
        Plan(["a", "b"], [("a", Ship), ("b", Ship)], null, movable: true);
        // the unit plan's own "units cannot move" line is its report, not this plan's
        var line = Assert.ContainsSingle(log.Lines.Where(l => !l.Contains("units cannot move")));
        StringAssert.Contains(line, " WARN sandbox: physics definition missing, ships cannot move");
    }

    [TestMethod]
    public void AShipWithoutAPhysicsDefinitionIsLeftToTheUnitPlansReport()
    {
        var plan = Plan(["a"], [("a", Ship)], null);
        Assert.AreEqual(0, plan.Physics.Count);
        Assert.AreEqual(0, plan.Reports.Count);
    }

    // The map holds two objects of the same class, team, position, and label.
    [TestMethod]
    public void TwoIdenticalShipsGetAPhysicsEntryEach()
    {
        var plan = Plan(["ship", "ship"], [("ship", Ship)], Odf());
        Assert.AreEqual(2, plan.Physics.Count);
        var keys = plan.Physics.Keys.ToArray();
        Assert.AreNotSame(keys[0], keys[1]);
    }
}
