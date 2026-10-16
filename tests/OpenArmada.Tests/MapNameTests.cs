using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Sandbox;

namespace OpenArmada.Tests;

[TestClass]
public class MapNameTests
{
    static readonly string[] Maps = { "bzn/2duel.bzn", "bzn/4mama.bzn" };

    [TestMethod]
    [DataRow("2duel")]
    [DataRow("2DUEL")]
    [DataRow("2Duel.bzn")]
    [DataRow("BZN/2DUEL.BZN")]
    [DataRow("bzn\\2duel")]
    public void ASpellingOfTheNameResolvesToTheInstallsMap(string spelling) =>
        Assert.AreEqual("bzn/2duel.bzn", MapName.Resolve(Maps, spelling).Name);

    [TestMethod]
    public void AnUnknownMapIsAProblemNamingItAndTheMapsOfTheInstall()
    {
        var r = MapName.Resolve(Maps, "nope");
        Assert.IsNull(r.Name);
        StringAssert.Contains(r.Problem, "'nope'");
        StringAssert.Contains(r.Problem, "4mama");
    }
}
