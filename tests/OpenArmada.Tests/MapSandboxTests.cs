using System;
using System.Linq;
using System.Numerics;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Diagnostics;
using OpenArmada.Sandbox;

namespace OpenArmada.Tests;

// Opening a map sandbox from the command line's values, on installs built in code (INV-0001).
[TestClass]
public class MapSandboxTests
{
    readonly TempInstall temp = new();

    [TestCleanup]
    public void CleanUp() => temp.CleanUp();

    static readonly string[] NoProblems = Array.Empty<string>();

    string Install(string mapFile = "bzn/2duel.bzn", string? content = null)
    {
        var b = new BznBuilder { Background = "" };
        b.Objects.Add(
            new()
            {
                Class = "fbattle",
                Team = 1,
                Position = new Vector3(1, 2, 3),
            }
        );

        return temp.MakeInstall(
            (mapFile, content ?? Encoding.Latin1.GetString(b.Build())),
            ("addon/fbattle.odf", "baseName = fdreadnought"),
            ("SOD/fdreadnought.sod", "")
        );
    }

    [TestMethod]
    [DataRow("2duel")]
    [DataRow("BZN/2DUEL.BZN")]
    public void ASpellingOfTheMapOpensItsPlan(string spelling)
    {
        var sandbox = MapSandbox.Open(Install(), spelling, NoProblems);
        Assert.IsNull(sandbox.Problem);
        Assert.AreEqual(1, sandbox.Plan!.Objects.Count);
    }

    [TestMethod]
    public void AMissingInstallIsAProblemNamingIt()
    {
        var sandbox = MapSandbox.Open("/no/such/install", "2duel", NoProblems);
        StringAssert.Contains(sandbox.Problem, "/no/such/install");
        Assert.IsNull(sandbox.Plan);
    }

    [TestMethod]
    public void NoInstallGivenIsAProblem()
    {
        StringAssert.Contains(MapSandbox.Open(null, "2duel", NoProblems).Problem, "--game");
    }

    [TestMethod]
    public void ACommandLineProblemIsShownInPlaceOfTheScene()
    {
        var sandbox = MapSandbox.Open(Install(), null, new[] { "--map needs a value" });
        StringAssert.Contains(sandbox.Problem, "--map needs a value");
        Assert.IsNull(sandbox.Plan);
    }

    [TestMethod]
    public void ASandboxThatCannotOpenIsOneErrorEntry()
    {
        using var log = new CapturedLog();
        var sandbox = MapSandbox.Open(Install(), "nope", NoProblems);
        var line = Assert.ContainsSingle(log.Lines);
        StringAssert.Contains(line, " ERROR sandbox: ");
        StringAssert.EndsWith(line, sandbox.Problem!);
    }

    [TestMethod]
    public void ACommandLineProblemIsNotLoggedAgainBySandbox()
    {
        using var log = new CapturedLog();
        MapSandbox.Open(Install(), null, new[] { "--map needs a value" });
        var line = Assert.ContainsSingle(log.Lines);
        StringAssert.Contains(line, " ERROR sandbox: ");
        Assert.DoesNotContain("--map needs a value", line);
    }

    [TestMethod]
    public void AnOpenedMapIsOneInfoEntryWithItsNameAndCounts()
    {
        var sandbox = MapSandbox.Open(Install(), "2duel", NoProblems);
        using var log = new CapturedLog();
        sandbox.LogOpened(units: 1);
        var line = Assert.ContainsSingle(log.Lines);
        StringAssert.EndsWith(line, " INFO sandbox: map opened: bzn/2duel.bzn, 1 objects, 1 units");
    }

    [TestMethod]
    public void AModelThatCannotBeReadIsOneErrorEntryWithItsNameAndReasonAndTheLabelLine()
    {
        using var log = new CapturedLog();
        var text = MapSandbox.LogUnreadableModel("fdreadnought", "bad magic");
        var line = Assert.ContainsSingle(log.Lines);
        StringAssert.EndsWith(line, " ERROR sandbox: " + text);
        StringAssert.Contains(text, "fdreadnought");
        StringAssert.Contains(text, "bad magic");
    }

    [TestMethod]
    public void ASavedScreenshotIsOneInfoEntryWithItsAbsolutePath()
    {
        using var log = new CapturedLog();
        ViewerLog.Screenshot(Area.Sandbox, "shot.png", null);
        var line = Assert.ContainsSingle(log.Lines);
        StringAssert.EndsWith(
            line,
            " INFO sandbox: screenshot saved: " + Log.Quote(System.IO.Path.GetFullPath("shot.png"))
        );
    }

    [TestMethod]
    public void AScreenshotThatFailedToSaveIsOneErrorEntryWithPathAndReason()
    {
        using var log = new CapturedLog();
        ViewerLog.Screenshot(Area.Sandbox, "shot.png", "ERR_FILE_CANT_WRITE");
        var line = Assert.ContainsSingle(log.Lines);
        StringAssert.Contains(line, " ERROR sandbox: ");
        StringAssert.Contains(line, Log.Quote(System.IO.Path.GetFullPath("shot.png")));
        StringAssert.Contains(line, "ERR_FILE_CANT_WRITE");
    }

    [TestMethod]
    public void AnUnknownMapIsAProblemNamingIt()
    {
        StringAssert.Contains(MapSandbox.Open(Install(), "nope", NoProblems).Problem, "'nope'");
    }

    [TestMethod]
    public void ACorruptMapIsAProblemNamingItAndItsReason()
    {
        var sandbox = MapSandbox.Open(Install(content: "not a map\r\n"), "2duel", NoProblems);
        StringAssert.Contains(sandbox.Problem, "2duel");
        StringAssert.Contains(sandbox.Problem, "not a BZN map");
        Assert.IsNull(sandbox.Plan);
    }

    string LayoutFile(string objects, string top = "")
    {
        var path = System.IO.Path.Combine(temp.MakeDir(), "duel.jsonc");
        System.IO.File.WriteAllText(
            path,
            $$"""{ "bounds": { "minX": 0, "maxX": 100, "minZ": 0, "maxZ": 100 }, {{top}} "objects": [ {{objects}} ] }"""
        );

        return path;
    }

    const string Battleship = """{ "class": "fbattle", "team": 1, "position": [1, 2, 3] }""";

    [TestMethod]
    public void ALayoutOpensThePlanOfExactlyItsObjects()
    {
        var path = LayoutFile(Battleship + ", " + Battleship);
        var sandbox = MapSandbox.OpenLayout(Install(), path, NoProblems);
        Assert.IsNull(sandbox.Problem);
        Assert.AreEqual(path, sandbox.Name);
        Assert.AreEqual(2, sandbox.Plan!.Objects.Count);
        Assert.IsTrue(sandbox.Plan.Objects.All(o => o.Class == "fbattle" && o.Team == 1));
        Assert.AreEqual(new Vector3(1, 2, -3), sandbox.Plan.Objects[0].Position);
        Assert.AreEqual(0, sandbox.Plan.Reports.Count);
    }

    [TestMethod]
    public void ALayoutProblemIsReportedWithNoInstallGiven()
    {
        var path = LayoutFile("""{ "class": "fbattle", "team": 9, "position": [0, 0, 0] }""");
        var sandbox = MapSandbox.OpenLayout(null, path, NoProblems);
        Assert.AreEqual($"layout '{path}': objects[0].team: team 9 is outside 0..8", sandbox.Problem);
        Assert.IsNull(sandbox.Plan);
    }

    [TestMethod]
    public void AMissingLayoutIsOneErrorEntryAndNoScene()
    {
        using var log = new CapturedLog();
        var sandbox = MapSandbox.OpenLayout(Install(), "/no/such/layout.jsonc", NoProblems);
        StringAssert.StartsWith(sandbox.Problem, "layout '/no/such/layout.jsonc': cannot be read: ");
        Assert.IsNull(sandbox.Plan);
        var line = Assert.ContainsSingle(log.Lines);
        StringAssert.EndsWith(line, " ERROR sandbox: " + sandbox.Problem);
    }

    [TestMethod]
    public void ALayoutCommandLineProblemIsShownInPlaceOfTheScene()
    {
        var sandbox = MapSandbox.OpenLayout(Install(), null, new[] { "--layout needs a value" });
        StringAssert.Contains(sandbox.Problem, "--layout needs a value");
        Assert.IsNull(sandbox.Plan);
    }

    [TestMethod]
    public void AClassTheInstallLacksIsAGapAndTheRestOfTheLayoutOpens()
    {
        var path = LayoutFile(Battleship + """, { "class": "nosuch", "team": 2, "position": [0, 0, 0] }""");
        var plan = MapSandbox.OpenLayout(Install(), path, NoProblems).Plan!;
        Assert.AreEqual(1, plan.Objects.Count);
        StringAssert.Contains(Assert.ContainsSingle(plan.Reports).Text, "nosuch");
    }

    [TestMethod]
    public void AnObjectOutsideTheBoundsReachesThePlanAsOnAMap()
    {
        var path = LayoutFile("""{ "class": "fbattle", "team": 1, "position": [500, 0, -50] }""");
        var plan = MapSandbox.OpenLayout(Install(), path, NoProblems).Plan!;
        Assert.AreEqual(new Vector3(500, 0, 50), Assert.ContainsSingle(plan.Objects).Position);
    }

    [TestMethod]
    public void AnEmptyObjectListOpensAnEmptyScene()
    {
        var sandbox = MapSandbox.OpenLayout(Install(), LayoutFile(""), NoProblems);
        Assert.IsNull(sandbox.Problem);
        Assert.AreEqual(0, sandbox.Plan!.Objects.Count);
    }

    [TestMethod]
    public void AnObjectThatGivesAxesIsPlacedWithThatFacingAndOneThatGivesNoneFacesAsIdentity()
    {
        var path = LayoutFile(
            Battleship
                + """, { "class": "fbattle", "team": 1, "position": [0, 0, 0], "right": [0, 0, -1], "up": [0, 1, 0], "front": [1, 0, 0] }"""
        );
        var objects = MapSandbox.OpenLayout(Install(), path, NoProblems).Plan!.Objects;
        Assert.AreEqual(
            (Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ),
            (objects[0].AxisX, objects[0].AxisY, objects[0].AxisZ)
        );
        Assert.AreEqual(new Vector3(0, 0, 1), objects[1].AxisX);
        Assert.AreEqual(new Vector3(-1, 0, 0), objects[1].AxisZ);
    }

    [TestMethod]
    public void APartialFacingIsAProblemAndNoSceneOpens()
    {
        var path = LayoutFile("""{ "class": "fbattle", "team": 1, "position": [0, 0, 0], "right": [1, 0, 0] }""");
        var sandbox = MapSandbox.OpenLayout(Install(), path, NoProblems);
        StringAssert.Contains(sandbox.Problem, "objects[0]: gives right but not up, front");
        Assert.IsNull(sandbox.Plan);
    }

    [TestMethod]
    public void ALeftOutLabelIsTheClassNameAndAGivenOneIsKept()
    {
        var path = LayoutFile(
            Battleship + """, { "class": "fbattle", "team": 1, "position": [0, 0, 0], "label": "flagship" }"""
        );
        var objects = MapSandbox.OpenLayout(Install(), path, NoProblems).Plan!.Objects;
        Assert.AreEqual("fbattle", objects[0].Label);
        Assert.AreEqual("flagship", objects[1].Label);
    }

    [TestMethod]
    [DataRow("fdreadnought")]
    [DataRow("fdreadnought.sod")]
    [DataRow("FDREADNOUGHT.SOD")]
    public void ABackgroundWithOrWithoutSodOpensTheSameModel(string background)
    {
        var path = LayoutFile(Battleship, $$""" "background": "{{background}}", """);
        Assert.AreEqual(
            "fdreadnought",
            MapSandbox.OpenLayout(Install(), path, NoProblems).Plan!.Background?.ToLowerInvariant()
        );
    }

    [TestMethod]
    public void ALeftOutBackgroundGivesNoBackground()
    {
        Assert.IsNull(MapSandbox.OpenLayout(Install(), LayoutFile(Battleship), NoProblems).Plan!.Background);
    }

    [TestMethod]
    public void ALeftOutTeamColorGivesTheTeamsOwnSlotAndAGivenSlotColorsThatTeam()
    {
        var two = """{ "class": "fbattle", "team": 2, "position": [0, 0, 0] }""";
        var plan = MapSandbox
            .OpenLayout(Install(), LayoutFile(Battleship + ", " + two, """ "teamColors": { "2": 5 }, """), NoProblems)
            .Plan!;
        Assert.AreEqual(1, plan.Objects[0].Slot);
        Assert.AreEqual(5, plan.Objects[1].Slot);
    }

    [TestMethod]
    public void AGivenAmbientColorLightsTheSceneAndALeftOutOneIsTheStandIn()
    {
        var given = MapSandbox.OpenLayout(
            Install(),
            LayoutFile(Battleship, """ "ambient": [0.1, 0.2, 0.3], """),
            NoProblems
        );
        Assert.AreEqual(new Vector3(0.1f, 0.2f, 0.3f), given.Plan!.Ambient);
        var left = MapSandbox.OpenLayout(Install(), LayoutFile(Battleship), NoProblems);
        Assert.AreEqual(new Vector3(0.35f, 0.35f, 0.4f), left.Plan!.Ambient);
    }
}
