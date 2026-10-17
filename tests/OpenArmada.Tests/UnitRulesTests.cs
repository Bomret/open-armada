using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Assets;
using OpenArmada.Sandbox;

namespace OpenArmada.Tests;

// Which map objects are units, from definitions built in code (INV-0001).
[TestClass]
public class UnitRulesTests
{
    readonly TempInstall temp = new();

    [TestCleanup]
    public void CleanUp() => temp.CleanUp();

    Unit? Classify(string odf)
    {
        var files = new GameFileSystem(temp.MakeInstall(("addon/u.odf", odf)));

        return UnitRules.Classify(ResolvedDefinition.Resolve(files, "u.odf"));
    }

    [TestMethod]
    public void ADefinitionWithPhysicsAndNoStarbaseKeyIsAShipNamedByItsUnitName() =>
        Assert.AreEqual(new Ship("Dreadnought"), Classify("physicsFile = \"u_p.odf\"\r\nunitName = \"Dreadnought\""));

    [DataTestMethod]
    [DataRow("1")]
    [DataRow("1.0")]
    [DataRow("1f")]
    [DataRow("2")]
    public void AStarbaseKeyOtherThanZeroIsAStation(string value) =>
        Assert.AreEqual(
            new Station("Base"),
            Classify($"physicsFile = \"u_p.odf\"\r\nunitName = \"Base\"\r\nis_starbase = {value}")
        );

    [TestMethod]
    public void AStarbaseKeyOfZeroIsAShip() =>
        Assert.AreEqual(
            new Ship("Ship"),
            Classify("physicsFile = \"u_p.odf\"\r\nunitName = \"Ship\"\r\nis_starbase = 0")
        );

    [TestMethod]
    public void ADefinitionWithoutPhysicsIsNoUnit() =>
        Assert.IsNull(Classify("unitName = \"Planet\"\r\nis_starbase = 1"));
}
