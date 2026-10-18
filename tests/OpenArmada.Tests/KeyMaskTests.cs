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

    // OS.FindKeycodeFromString("Shift+Foo") gives code 0 with the Shift bit.
    [TestMethod]
    public void AnUnknownKeyWithAModifierDoesNotRead() => Assert.IsFalse(KeyMask.Reads("Shift+Foo", 1L << 25));

    // OS.FindKeycodeFromString drops a modifier name it does not know: "Control+G" gives the bare G.
    [TestMethod]
    public void ADroppedModifierDoesNotRead() => Assert.IsFalse(KeyMask.Reads("Control+G", G));

    [TestMethod]
    public void AChordWithEveryModifierSetReads() =>
        Assert.IsTrue(KeyMask.Reads("Ctrl+Shift+G", G | 1L << 25 | 1L << 28));
}
