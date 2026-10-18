using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Sandbox;

namespace OpenArmada.Tests;

// The split of a Godot keycode with modifier bits, as OS.FindKeycodeFromString("Shift+G") returns it.
[TestClass]
public class KeyMaskTests
{
    const long G = 71;

    [TestMethod]
    public void ShiftGSplitsIntoGWithShiftHeld() =>
        Assert.AreEqual((G, true, false, false, false), KeyMask.Split(G | 1L << 25));

    [TestMethod]
    public void ControlAltAndMetaBitsSetTheirFlags() =>
        Assert.AreEqual((G, false, true, true, true), KeyMask.Split(G | 1L << 28 | 1L << 26 | 1L << 27));

    [TestMethod]
    public void AKeyWithoutModifiersKeepsItsCode() =>
        Assert.AreEqual((G, false, false, false, false), KeyMask.Split(G));
}
