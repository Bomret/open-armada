using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Sandbox;

namespace OpenArmada.Tests;

[TestClass]
public class LaunchArgsTests
{
    [TestMethod]
    public void AFlagsValueIsTheArgumentAfterIt()
    {
        var args = new LaunchArgs(new[] { "--game", "x", "--map", "2duel" });
        Assert.AreEqual("2duel", args.Value("--map"));
        Assert.AreEqual(0, args.Problems.Count);
    }

    [TestMethod]
    public void AnAbsentFlagHasNoValueAndNoProblem()
    {
        var args = new LaunchArgs(new[] { "--game", "x" });
        Assert.IsNull(args.Value("--map"));
        Assert.IsFalse(args.Has("--map"));
        Assert.AreEqual(0, args.Problems.Count);
    }

    [TestMethod]
    public void AFlagFollowedByAnotherFlagIsAMissingValue()
    {
        var args = new LaunchArgs(new[] { "--map", "--game", "x" });
        Assert.IsNull(args.Value("--map"));
        Assert.IsTrue(args.Has("--map"));
        StringAssert.Contains(args.Problems[0], "--map needs a value");
        Assert.AreEqual("x", args.Value("--game"));
    }

    [TestMethod]
    public void ATrailingFlagIsAMissingValue()
    {
        var args = new LaunchArgs(new[] { "--game", "x", "--screenshot" });
        Assert.IsNull(args.Value("--screenshot"));
        StringAssert.Contains(args.Problems[0], "--screenshot needs a value");
    }

    [TestMethod]
    public void AMapWithATrailingScreenshotFlagHasItsValueAndTheScreenshotProblem()
    {
        var args = new LaunchArgs(new[] { "--map", "x", "--screenshot" });
        Assert.AreEqual("x", args.Value("--map"));
        Assert.IsNull(args.Value("--screenshot"));
        Assert.AreEqual(1, args.Problems.Count);
        StringAssert.Contains(args.Problems[0], "--screenshot needs a value");
    }

    [TestMethod]
    public void TwoFlagsThatExcludeEachOtherAreOneProblemNamingBoth()
    {
        var args = new LaunchArgs(new[] { "--layout", "a.jsonc", "--map", "2duel" });
        args.Exclusive("--layout", "--map");
        args.Exclusive("--layout", "--map");
        Assert.AreEqual("--layout and --map cannot be given together", Assert.ContainsSingle(args.Problems));
    }

    // --check wins over --maps; --maps wins over --map, --layout, and --play; then play, map, layout, browser.
    [TestMethod]
    [DataRow("--check --maps --play p --map m --layout l", "check")]
    [DataRow("--maps --play p --map m --layout l", "maps")]
    [DataRow("--play p --map m --layout l", "play")]
    [DataRow("--map m --layout l", "map")]
    [DataRow("--layout l", "layout")]
    [DataRow("--game g --model Fente", "browser")]
    public void TheModeFollowsTheFlagPrecedence(string args, string mode)
    {
        Assert.AreEqual(mode, new LaunchArgs(args.Split(' ')).Mode());
    }

    [TestMethod]
    public void TheModeLooksUpNoValue()
    {
        var args = new LaunchArgs(new[] { "--maps", "--map" });
        args.Mode();
        Assert.AreEqual(0, args.Problems.Count);
    }

    [TestMethod]
    [DataRow("--layout", "a.jsonc")]
    [DataRow("--map", "2duel")]
    public void OneOfTwoExclusiveFlagsIsNoProblem(string flag, string value)
    {
        var args = new LaunchArgs(new[] { flag, value });
        args.Exclusive("--layout", "--map");
        Assert.AreEqual(0, args.Problems.Count);
    }
}
