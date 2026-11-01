using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Formats;
using OpenArmada.Sandbox;
using static OpenArmada.Tests.CombatFixtures;

namespace OpenArmada.Tests;

// The check of the systems' target hardpoints against the unit's model, as the class load makes it (0x460f30).
[TestClass]
public class TargetHardpointsTests
{
    static Sod Model(params string[] nodes) =>
        new() { Version = 1.8f, Nodes = nodes.Select(n => new Sod.Node { Name = n }).ToList() };

    [TestMethod]
    public void AHardpointTheModelLacksIsLeftOutAndNamedOnce()
    {
        var profile = Profile(
            systems:
            [
                Sys("Engines", targetHardpoints: ["hp01", "hp02", "hp09"]),
                Sys("Sensors", targetHardpoints: ["HP09", "hp02"]),
            ]
        );
        var (kept, missing) = TargetHardpoints.Keep(profile, Model("HP01", "hp02"));
        CollectionAssert.AreEqual(new[] { "hp01", "hp02" }, kept.Systems[0].TargetHardpoints.ToArray());
        CollectionAssert.AreEqual(new[] { "hp02" }, kept.Systems[1].TargetHardpoints.ToArray());
        CollectionAssert.AreEqual(new[] { "hp09" }, missing.ToArray());
    }

    [TestMethod]
    public void AModelWithEveryHardpointOrNoModelLeavesTheProfileAsItIs()
    {
        var profile = Profile(systems: [Sys("Engines", targetHardpoints: ["hp01"])]);
        Assert.AreSame(profile, TargetHardpoints.Keep(profile, Model("hp01")).Profile);
        Assert.AreSame(profile, TargetHardpoints.Keep(profile, null).Profile);
        Assert.AreEqual(0, TargetHardpoints.Keep(profile, null).Missing.Count);
    }
}
