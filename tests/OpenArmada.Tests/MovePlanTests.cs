using System;
using System.Linq;
using System.Numerics;
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

    MovePlan Plan(string[] classes, (string Class, string Odf)[] definitions, string? physics)
    {
        var loose = physics == null ? Array.Empty<(string, string)>() : new[] { ("addon/p.odf", physics) };
        var install = new GameFileSystem(temp.MakeInstall(definitions.Select(d => ($"addon/{d.Class}.odf", d.Odf)).Concat(loose).ToArray()));
        var map = new BznBuilder { Background = "" };
        foreach (var c in classes) map.Objects.Add(new BznBuilder.Obj(c, 1, new Vector3(1, 2, 3), c));
        var asset = new MapAsset("bzn/t.bzn", Bzn.Read(map.Build()), Array.Empty<MapStart>(), null, Array.Empty<MapPath>());
        var plan = SandboxPlan.Build(asset, SandboxPlan.Definitions(install, asset), _ => true);
        return MovePlan.Build(plan, UnitPlan.Build(install, plan, _ => true).Units, install);
    }

    [TestMethod]
    public void AShipsMovementValuesAreReadFromItsPhysicsDefinition()
    {
        var plan = Plan(new[] { "ship" }, new[] { ("ship", Ship) }, "impulseSpeed = 12.5\r\nyawRotationRate = 0.75\r\nmaxRoll = 30");
        Assert.AreEqual(new PhysicsValues(12.5, 0.75, 30), plan.Physics.Values.Single());
        Assert.AreEqual(0, plan.Reports.Count);
    }

    [TestMethod]
    public void ADefinitionWithoutMaxRollGivesNoRollAndNoReport()
    {
        var plan = Plan(new[] { "ship" }, new[] { ("ship", Ship) }, "impulseSpeed = 12\r\nyawRotationRate = 1");
        Assert.IsNull(plan.Physics.Values.Single().MaxRoll);
        Assert.AreEqual(0, plan.Reports.Count);
    }

    [TestMethod]
    public void StationsGetNoMovementValues()
    {
        var plan = Plan(new[] { "base" }, new[] { ("base", Ship + "is_starbase = 1") }, "impulseSpeed = 12\r\nyawRotationRate = 1");
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
        var plan = Plan(new[] { "a" }, new[] { ("a", Ship) }, "impulseSpeed = 1\r\nyawRotationRate = 1\r\nmaxRoll = lots");
        Assert.IsNull(plan.Physics.Values.Single().MaxRoll);
    }

    [TestMethod]
    public void AMaxRollNotAbove0IsReportedOncePerCauseWithNamesAndACountAndTakenAsNoRoll()
    {
        var plan = Plan(new[] { "a", "b", "a" }, new[] { ("a", Ship), ("b", Ship) }, "impulseSpeed = 1\r\nyawRotationRate = 1\r\nmaxRoll = -30");
        Assert.IsTrue(plan.Physics.Count > 0);
        Assert.IsTrue(plan.Physics.Values.All(p => p.MaxRoll == null));
        var report = plan.Reports.Single();
        CollectionAssert.AreEqual(new[] { "a", "b" }, report.Names.ToArray());
        Assert.AreEqual(3, report.Count);
        StringAssert.Contains(report.Text, "maxRoll");
    }

    [TestMethod]
    public void AShipWithoutAPhysicsDefinitionIsLeftToTheUnitPlansReport()
    {
        var plan = Plan(new[] { "a" }, new[] { ("a", Ship) }, null);
        Assert.AreEqual(0, plan.Physics.Count);
        Assert.AreEqual(0, plan.Reports.Count);
    }
}
