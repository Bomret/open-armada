using System;
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
}
