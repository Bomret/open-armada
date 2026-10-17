using System;
using System.Linq;
using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Assets;
using OpenArmada.Formats;
using OpenArmada.Sandbox;

namespace OpenArmada.Tests;

// The units of a map sandbox's scene plan, from maps and installs built in code (INV-0001).
[TestClass]
public class UnitPlanTests
{
    readonly TempInstall temp = new();

    [TestCleanup]
    public void CleanUp() => temp.CleanUp();

    // Each definition is "class" -> odf text; the install also holds the loose files given.
    UnitPlan Plan(
        string[] classes,
        (string Class, string Odf)[] definitions,
        params (string Path, string Content)[] loose
    ) => Plan(_ => true, classes, definitions, loose);

    UnitPlan Plan(
        Func<PlacedObject, bool> selectable,
        string[] classes,
        (string Class, string Odf)[] definitions,
        params (string Path, string Content)[] loose
    )
    {
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

        return UnitPlan.Build(install, plan, selectable);
    }

    const string Physics = "physicsFile = \"p.odf\"\r\n";

    [TestMethod]
    public void OnlyObjectsWhoseDefinitionNamesPhysicsAreUnits()
    {
        var plan = Plan(
            new[] { "ship", "base", "planet" },
            new[]
            {
                ("ship", Physics + "unitName = \"Ship\""),
                ("base", Physics + "unitName = \"Base\"\r\nis_starbase = 1"),
                ("planet", "unitName = \"Planet\""),
            },
            ("addon/p.odf", "maxRoll = 1")
        );
        CollectionAssert.AreEqual(
            new Unit[]
            {
                new Ship { Name = "Ship" },
                new Station { Name = "Base" },
            },
            plan.Units.Select(u => u.Unit).ToArray()
        );
        Assert.AreEqual("base", plan.Units[1].Object.Class);
    }

    [TestMethod]
    public void AUnitWithAPhysicsDefinitionIsMovable()
    {
        var plan = Plan(new[] { "ship" }, new[] { ("ship", Physics) }, ("addon/p.odf", "maxRoll = 1"));
        Assert.IsTrue(plan.Units.Single().Movable);
        Assert.AreEqual(0, plan.Reports.Count);
    }

    [TestMethod]
    public void AMissingPhysicsDefinitionIsReportedOncePerCauseWithACountAndTheUnitStaysSelectable()
    {
        var plan = Plan(new[] { "a", "b", "a" }, new[] { ("a", Physics), ("b", Physics) });
        Assert.AreEqual(3, plan.Units.Count);
        Assert.IsFalse(plan.Units.Any(u => u.Movable));
        var report = plan.Reports.Single();
        CollectionAssert.AreEqual(new[] { "a", "b" }, report.Names.ToArray());
        Assert.AreEqual(3, report.Count);
        StringAssert.Contains(report.Text, "physics definition missing");
    }

    [TestMethod]
    public void AStarbaseValueThatIsNoNumberIsReportedAndTheUnitIsAShip()
    {
        var plan = Plan(
            new[] { "x" },
            new[] { ("x", Physics + "unitName = \"X\"\r\nis_starbase = yes") },
            ("addon/p.odf", "")
        );
        Assert.AreEqual(new Ship { Name = "X" }, plan.Units.Single().Unit);
        StringAssert.Contains(plan.Reports.Single().Cause, "is_starbase");
    }

    [TestMethod]
    public void AUnitNameThatIsNoSingleValueIsReportedByItsOwnCauseAndTheClassNameStandsIn()
    {
        var plan = Plan(new[] { "x", "x" }, new[] { ("x", Physics + "unitName = Borg Cube") }, ("addon/p.odf", ""));
        Assert.AreEqual(new Ship { Name = "x" }, plan.Units[0].Unit);
        var report = plan.Reports.Single();
        StringAssert.Contains(report.Cause, "unitName");
        Assert.AreEqual(2, report.Count);
    }

    [TestMethod]
    public void AUnitNameAndAStarbaseValueThatAreBothBadAreReportedEachOnce()
    {
        var plan = Plan(
            new[] { "x" },
            new[] { ("x", Physics + "unitName = Borg Cube\r\nis_starbase = yes") },
            ("addon/p.odf", "")
        );
        Assert.AreEqual(new Ship { Name = "x" }, plan.Units.Single().Unit);
        Assert.AreEqual(2, plan.Reports.Count);
        Assert.AreEqual(1, plan.Reports.Count(r => r.Cause.Contains("unitName")));
        Assert.AreEqual(1, plan.Reports.Count(r => r.Cause.Contains("is_starbase")));
    }

    [TestMethod]
    public void APhysicsFileThatIsNoSingleValueIsReportedAndTheUnitStaysSelectableButNotMovable()
    {
        var plan = Plan(new[] { "x" }, new[] { ("x", "physicsFile = a b") });
        Assert.IsFalse(plan.Units.Single().Movable);
        StringAssert.Contains(plan.Reports.Single().Cause, "physicsFile");
    }

    [TestMethod]
    public void AnObjectThatCannotBeSelectedIsNoUnitAndNoGap()
    {
        var plan = Plan(o => o.Class != "b", new[] { "a", "b" }, new[] { ("a", Physics), ("b", Physics) });
        Assert.AreEqual("a", plan.Units.Single().Object.Class);
        CollectionAssert.AreEqual(new[] { "a" }, plan.Reports.Single().Names.ToArray());
    }

    [TestMethod]
    public void ADefinitionWithPhysicsButNoUnitNameTakesTheClassName()
    {
        var plan = Plan(new[] { "x" }, new[] { ("x", Physics) }, ("addon/p.odf", ""));
        Assert.AreEqual(new Ship { Name = "x" }, plan.Units.Single().Unit);
    }
}
