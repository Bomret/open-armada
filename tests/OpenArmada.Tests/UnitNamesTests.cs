using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Sandbox;
using static OpenArmada.Tests.CombatFixtures;

namespace OpenArmada.Tests;

[TestClass]
public class UnitNamesTests
{
    static PlacedObject Of(string label, string cls = "c") => Placed(label, 1, 0) with { Class = cls };

    [TestMethod]
    public void AUnitIsNamedByItsLabel()
    {
        var a = Of("Enterprise");

        Assert.AreEqual(a, new UnitNames([a, Of("Other")]).Resolve("Enterprise").Unit);
    }

    [TestMethod]
    public void AUnitWithoutALabelIsNamedByItsClass()
    {
        var a = Of("", "zmama");
        var names = new UnitNames([a]);

        Assert.AreEqual("zmama", names.Name(a));
        Assert.AreEqual(a, names.Resolve("zmama").Unit);
    }

    [TestMethod]
    public void ARepeatedLabelResolvesWithItsPlanPositionFromOne()
    {
        var first = Of("ship");
        var second = Of("ship");
        var names = new UnitNames([first, Of("lone"), second]);

        Assert.AreEqual(first, names.Resolve("ship#1").Unit);
        Assert.AreEqual(second, names.Resolve("ship#2").Unit);
        Assert.AreEqual("ship#2", names.Name(second));
    }

    [TestMethod]
    public void ABareRepeatedLabelIsAProblemNamingTheChoices()
    {
        var names = new UnitNames([Of("ship"), Of("ship")]);
        var (unit, problem) = names.Resolve("ship");

        Assert.IsNull(unit);
        StringAssert.Contains(problem, "'ship' names 2 units, use ship#1 to ship#2");
    }

    [TestMethod]
    [DataRow("nobody")]
    [DataRow("ship#3")]
    [DataRow("ship#0")]
    [DataRow("lone#1")]
    public void AnUnknownNameIsAProblem(string name)
    {
        var (unit, problem) = new UnitNames([Of("ship"), Of("ship"), Of("lone")]).Resolve(name);

        Assert.IsNull(unit);
        Assert.AreEqual($"unknown unit '{name}'", problem);
    }
}
