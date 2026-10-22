using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Formats;
using OpenArmada.Sandbox;

namespace OpenArmada.Tests;

[TestClass]
public class SystemNamesTests
{
    [TestMethod]
    [DataRow(HitLocation.Engines)]
    [DataRow(HitLocation.LifeSupport)]
    [DataRow(HitLocation.Weapons)]
    [DataRow(HitLocation.ShieldGenerator)]
    [DataRow(HitLocation.Sensors)]
    public void TheSystemAtTheIndexOfAHitLocationIsNamedAfterIt(HitLocation location)
    {
        Assert.AreEqual(location.ToString(), SystemNames.All[(int)location]);
    }

    [TestMethod]
    public void ThereIsOneNameForEachShipSystemLocation()
    {
        Assert.AreEqual((int)HitLocation.Hull, SystemNames.All.Length);
    }
}
