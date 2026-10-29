using System;
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

    // `physicsFile` is the install file that holds p.odf (a loose file, or an archive given as Latin-1 text).
    // `movable` takes every ship as Movable, so MovePlan reads a physics definition the unit plan found absent.
    MovePlan Plan(
        string[] classes,
        (string Class, string Odf)[] definitions,
        string? physics,
        string physicsFile = "addon/p.odf",
        bool movable = false
    )
    {
        var loose = physics == null ? Array.Empty<(string, string)>() : new[] { (physicsFile, physics) };
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

    [TestMethod]
    public void AShipsMovementValuesAreReadFromItsPhysicsDefinition()
    {
        var plan = Plan(
            new[] { "ship" },
            new[] { ("ship", Ship) },
            "impulseSpeed = 12.5\r\nyawRotationRate = 0.75\r\nmaxRoll = 30\r\ntooCloseToTurn = 40"
        );
        Assert.AreEqual(
            new PhysicsValues
            {
                ImpulseSpeed = 12.5,
                YawRotationRate = 0.75,
                MaxRoll = 30,
                TooCloseToTurn = 40,
            },
            plan.Physics.Values.Single()
        );
        Assert.AreEqual(0, plan.Reports.Count);
    }

    [TestMethod]
    [DataRow("\r\nborgPhysics = 1", true)]
    [DataRow("\r\nborgPhysics = 0", false)]
    [DataRow("", false)]
    public void ABorgPhysicsFlagIsReadFromThePhysicsDefinitionAndNeedsNoReport(string key, bool borg)
    {
        var plan = Plan(
            new[] { "ship" },
            new[] { ("ship", Ship) },
            "impulseSpeed = 12\r\nyawRotationRate = 1\r\ntooCloseToTurn = 40" + key
        );
        Assert.AreEqual(borg, plan.Physics.Values.Single().BorgPhysics);
        Assert.AreEqual(0, plan.Reports.Count);
    }

    [TestMethod]
    public void ABorgPhysicsValueThatIsNoNumberIsReportedAndLeftOff()
    {
        var plan = Plan(
            new[] { "ship" },
            new[] { ("ship", Ship) },
            "impulseSpeed = 12\r\nyawRotationRate = 1\r\ntooCloseToTurn = 40\r\nborgPhysics = yes"
        );
        Assert.IsFalse(plan.Physics.Values.Single().BorgPhysics);
        StringAssert.Contains(plan.Reports.Single().Text, "borgPhysics");
    }

    [TestMethod]
    public void ADefinitionWithoutMaxRollGivesNoRollAndNoReport()
    {
        var plan = Plan(
            new[] { "ship" },
            new[] { ("ship", Ship) },
            "impulseSpeed = 12\r\nyawRotationRate = 1\r\ntooCloseToTurn = 40"
        );
        Assert.IsNull(plan.Physics.Values.Single().MaxRoll);
        Assert.AreEqual(0, plan.Reports.Count);
    }

    [TestMethod]
    public void StationsGetNoMovementValues()
    {
        var plan = Plan(
            new[] { "base" },
            new[] { ("base", Ship + "is_starbase = 1") },
            "impulseSpeed = 12\r\nyawRotationRate = 1"
        );
        Assert.AreEqual(0, plan.Physics.Count);
    }

    [TestMethod]
    public void AMissingMovementValueIsReportedOncePerCauseWithNamesAndACount()
    {
        var plan = Plan(new[] { "a", "b", "a" }, new[] { ("a", Ship), ("b", Ship) }, "yawRotationRate = 1");
        Assert.AreEqual(0, plan.Physics.Count);
        var report = plan.Reports.Single();
        CollectionAssert.AreEqual(new[] { "a", "b" }, report.Names.ToArray());
        Assert.AreEqual(3, report.Count);
        StringAssert.Contains(report.Text, "impulseSpeed");
    }

    [TestMethod]
    public void AGroupedMovementGapIsOneWarnEntryWithTheLabelLine()
    {
        using var log = new CapturedLog();
        var plan = Plan(new[] { "a", "b", "a" }, new[] { ("a", Ship), ("b", Ship) }, "yawRotationRate = 1");
        var line = Assert.ContainsSingle(log.Lines);
        StringAssert.EndsWith(line, " WARN sandbox: " + plan.Reports.Single().Text);
    }

    [TestMethod]
    public void EachMissingValueIsItsOwnCause()
    {
        var plan = Plan(new[] { "a" }, new[] { ("a", Ship) }, "");
        Assert.AreEqual(2, plan.Reports.Count);
    }

    [TestMethod]
    public void AValueThatIsNoPositiveNumberIsReported()
    {
        var plan = Plan(new[] { "a" }, new[] { ("a", Ship) }, "impulseSpeed = fast\r\nyawRotationRate = 0");
        Assert.AreEqual(0, plan.Physics.Count);
        Assert.AreEqual(2, plan.Reports.Count);
    }

    [TestMethod]
    public void AMaxRollThatIsNoNumberIsTakenAsNoRoll()
    {
        var plan = Plan(
            new[] { "a" },
            new[] { ("a", Ship) },
            "impulseSpeed = 1\r\nyawRotationRate = 1\r\nmaxRoll = lots"
        );
        Assert.IsNull(plan.Physics.Values.Single().MaxRoll);
    }

    [TestMethod]
    public void ANegativeMaxRollIsReportedOncePerCauseWithNamesAndACountAndTakenAsNoRoll()
    {
        var plan = Plan(
            new[] { "a", "b", "a" },
            new[] { ("a", Ship), ("b", Ship) },
            "impulseSpeed = 1\r\nyawRotationRate = 1\r\nmaxRoll = -30\r\ntooCloseToTurn = 40"
        );
        Assert.IsTrue(plan.Physics.Count > 0);
        Assert.IsTrue(plan.Physics.Values.All(p => p.MaxRoll == null));
        var report = plan.Reports.Single();
        CollectionAssert.AreEqual(new[] { "a", "b" }, report.Names.ToArray());
        Assert.AreEqual(3, report.Count);
        StringAssert.Contains(report.Text, "maxRoll");
    }

    [TestMethod]
    public void EachShipsTooCloseToTurnComesFromItsOwnPhysicsDefinition()
    {
        var install = new GameFileSystem(
            temp.MakeInstall([
                ("addon/a.odf", "physicsFile = \"pa.odf\"\r\n"),
                ("addon/b.odf", "physicsFile = \"pb.odf\"\r\n"),
                ("addon/pa.odf", "impulseSpeed = 1\r\nyawRotationRate = 1\r\ntooCloseToTurn = 40;"), // the stock destphys.odf closes the value with a semicolon
                ("addon/pb.odf", "impulseSpeed = 1\r\nyawRotationRate = 1\r\ntooCloseToTurn = 50"),
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
    [DataRow("\r\ntooCloseToTurn = 0")]
    [DataRow("\r\ntooCloseToTurn = -5")]
    [DataRow("\r\ntooCloseToTurn = near")]
    public void ATooCloseToTurnThatIsMissingOrNoPositiveNumberTakesTheDefaultAndIsReportedOncePerClass(string key)
    {
        var plan = Plan(
            new[] { "a", "b", "a" },
            new[] { ("a", Ship), ("b", Ship) },
            "impulseSpeed = 1\r\nyawRotationRate = 1" + key
        );
        Assert.IsTrue(plan.Physics.Values.All(p => p.TooCloseToTurn == 25));
        var report = plan.Reports.Single();
        CollectionAssert.AreEqual(new[] { "a", "b" }, report.Names.ToArray());
        Assert.AreEqual(3, report.Count);
        StringAssert.Contains(report.Text, "tooCloseToTurn");
    }

    [TestMethod]
    public void AMaxRollOf0IsNoBankWithoutAReport()
    {
        var plan = Plan(
            new[] { "a" },
            new[] { ("a", Ship) },
            "impulseSpeed = 1\r\nyawRotationRate = 1\r\nmaxRoll = 0\r\ntooCloseToTurn = 40"
        );
        Assert.AreEqual(0.0, plan.Physics.Values.Single().MaxRoll);
        Assert.AreEqual(0, plan.Reports.Count);
    }

    [TestMethod]
    public void APhysicsDefinitionThatCannotBeReadIsReportedOncePerCauseWithNamesAndACount()
    {
        // a compressed archive entry: the install lists p.odf, but reading it throws UnsupportedFormatException
        var archive = System.Text.Encoding.Latin1.GetString(
            ZfsBuilder.Build(0, ZfsBuilder.M("p.odf", "impulseSpeed = 1\r\nyawRotationRate = 1", 2))
        );
        var plan = Plan(new[] { "a", "b", "a" }, new[] { ("a", Ship), ("b", Ship) }, archive, "addon/x.zfs");
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
            ZfsBuilder.Build(0, ZfsBuilder.M("p.odf", "impulseSpeed = 1\r\nyawRotationRate = 1", 2))
        );
        using var log = new CapturedLog();
        Plan(new[] { "a", "b" }, new[] { ("a", Ship), ("b", Ship) }, archive, "addon/x.zfs");
        var entries = log.Lines.Where(l => l.Contains("physics definition")).ToList();
        StringAssert.Matches(entries.Single(), new Regex(@" ERROR sandbox: physics definition p\.odf: \S"));
    }

    [TestMethod]
    public void AMissingPhysicsDefinitionIsOneWarnEntryAndNoError()
    {
        // absent data, not an asset that failed to load (INV-0005): the grouped gap's WARN, no ERROR
        using var log = new CapturedLog();
        Plan(new[] { "a", "b" }, new[] { ("a", Ship), ("b", Ship) }, null, movable: true);
        // the unit plan's own "units cannot move" line is its report, not this plan's
        var line = Assert.ContainsSingle(log.Lines.Where(l => !l.Contains("units cannot move")));
        StringAssert.Contains(line, " WARN sandbox: physics definition missing, ships cannot move");
    }

    [TestMethod]
    public void AShipWithoutAPhysicsDefinitionIsLeftToTheUnitPlansReport()
    {
        var plan = Plan(new[] { "a" }, new[] { ("a", Ship) }, null);
        Assert.AreEqual(0, plan.Physics.Count);
        Assert.AreEqual(0, plan.Reports.Count);
    }

    // The map holds two objects of the same class, team, position, and label.
    [TestMethod]
    public void TwoIdenticalShipsGetAPhysicsEntryEach()
    {
        var plan = Plan(
            new[] { "ship", "ship" },
            new[] { ("ship", Ship) },
            "impulseSpeed = 12\r\nyawRotationRate = 1\r\ntooCloseToTurn = 40"
        );
        Assert.AreEqual(2, plan.Physics.Count);
        var keys = plan.Physics.Keys.ToArray();
        Assert.AreNotSame(keys[0], keys[1]);
    }
}
