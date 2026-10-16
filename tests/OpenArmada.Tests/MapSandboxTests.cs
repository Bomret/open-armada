using System;
using System.Numerics;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
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
        b.Objects.Add(new("fbattle", 1, new Vector3(1, 2, 3)));
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
