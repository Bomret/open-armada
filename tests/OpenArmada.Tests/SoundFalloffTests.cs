using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Sandbox;

namespace OpenArmada.Tests;

// The one falloff of fire and explosion sounds, against the camera's opening distance and reach.
[TestClass]
public class SoundFalloffTests
{
    const double Opening = 661.5; // the stand-in rig's eye distance at its starting height
    const double Reach = 6000;

    [TestMethod]
    public void SoundAtTheOpeningDistanceIsAtLeastMinus20Db()
    {
        Assert.IsTrue(SoundFalloff.For(Opening).DbAt(Opening) >= -20);
        Assert.IsTrue(SoundFalloff.For(50).DbAt(50) >= -20);
    }

    [TestMethod]
    public void CloseSoundIsCapped()
    {
        var falloff = SoundFalloff.For(Opening);
        Assert.AreEqual(falloff.MaxDb, falloff.DbAt(0));
        Assert.AreEqual(falloff.MaxDb, falloff.DbAt(1));
        Assert.IsTrue(falloff.MaxDb <= 0);
    }

    [TestMethod]
    public void NoSoundInsideTheReachIsCutOff()
    {
        var falloff = SoundFalloff.For(Opening);
        Assert.IsTrue(falloff.MaxDistance == 0 || falloff.MaxDistance >= Reach);
        Assert.IsTrue(double.IsFinite(falloff.DbAt(Reach)));
        Assert.IsTrue(falloff.DbAt(Reach) < falloff.DbAt(Opening)); // it still falls off
    }
}
