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
}
