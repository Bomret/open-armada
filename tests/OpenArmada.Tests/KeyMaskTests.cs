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

    // Godot 4.7.2: KEY_ESCAPE is 4194305, KEY_A is 65, KEY_SHIFT is 4194325.
    [TestMethod]
    public void APlainKeyReads() => Assert.IsTrue(KeyMask.Reads("Escape", 4194305));

    [TestMethod]
    public void APlainLetterReads() => Assert.IsTrue(KeyMask.Reads("A", 65));

    // OS.FindKeycodeFromString("G+Shift") gives the Shift key: the G in front is no modifier.
    [TestMethod]
    public void AKeyNameInFrontOfAModifierDoesNotRead() => Assert.IsFalse(KeyMask.Reads("G+Shift", 4194325));
}
