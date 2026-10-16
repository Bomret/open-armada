using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
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
    SandboxPlan Plan(BznBuilder map, (string Class, string Odf)[] definitions, params string[] models)
    {
        var install = new GameFileSystem(temp.MakeInstall(definitions.Select(d => ($"addon/{d.Class}.odf", d.Odf)).ToArray()));
        var asset = new MapAsset("bzn/t.bzn", Bzn.Read(map.Build()), Array.Empty<MapStart>(), null, Array.Empty<MapPath>());
        return SandboxPlan.Build(asset, SandboxPlan.Definitions(install, asset), models.Contains);
    }

    static BznBuilder MapOf(params BznBuilder.Obj[] objects)
    {
        var b = new BznBuilder { Background = "" };
        b.Objects.AddRange(objects);
        return b;
    }

    static BznBuilder.Obj Ship(string cls = "fbattle", int team = 1, Vector3? at = null, float[]? axes = null, string label = "s") =>
        new(cls, team, at ?? new Vector3(100, 38.5f, 200), label, Axes: axes);

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
        Assert.AreEqual((new Vector3(1, 0, 0), new Vector3(0, 1, 0), new Vector3(0, 0, 1)), (o.AxisX, o.AxisY, o.AxisZ));
        var report = plan.Reports.Single();
        StringAssert.Contains(report.Cause, "orientation");
        Assert.AreEqual(1, report.Count);
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
    public void ABaseNameOfAMissingModelIsReportedWithoutAFallback()
    {
        var plan = Plan(MapOf(Ship(), Ship()), Fbattle, "fbattle");
        Assert.AreEqual(0, plan.Objects.Count);
        var report = plan.Reports.Single();
        CollectionAssert.AreEqual(new[] { "fdreadnought" }, report.Names.ToArray());
        Assert.AreEqual(2, report.Count);
    }

    [TestMethod]
    public void AnObjectOfClassLabelAsteroidFieldIsSkippedWithoutAReport()
    {
        // The map stores the class name ("mafield"); the definition's classLabel says it is an asteroid field,
        // and the model its baseName names is not in the install.
        var plan = Plan(MapOf(Ship("mafield")), new[] { ("mafield", "classLabel = AsteroidField\nbaseName = sscr_1") });
        Assert.AreEqual(0, plan.Objects.Count);
        Assert.AreEqual(0, plan.Reports.Count);
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
        CollectionAssert.AreEqual(new[] { 3, 0 }, Plan(map, Fbattle, "fdreadnought").Objects.Select(o => o.Slot).ToArray());
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
}
