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

    // OS.FindKeycodeFromString as Godot reads a name: the last part a key (code 0 when unknown), each part in front a
    // modifier bit (none when unknown). The keys R, A, and Shift and the modifiers Shift and Ctrl.
    static long Find(string name)
    {
        var parts = name.Split('+');
        long code = parts[^1] switch
        {
            "R" => 82,
            "A" => 65,
            "Shift" => 4194325,
            _ => 0,
        };
        foreach (var part in parts[..^1])
            code |= part switch
            {
                "Shift" => 1L << 25,
                "Ctrl" => 1L << 28,
                _ => 0,
            };

        return code;
    }

    [TestMethod]
    public void AnUnknownModifierIsNamedInTheProblem() =>
        Assert.AreEqual(
            "'Control+R' is no key name: 'Control' is not a modifier Godot knows",
            KeyMask.Problem("Control+R", Find)
        );

    [TestMethod]
    public void AnUnknownKeyIsNoKeyName() =>
        Assert.AreEqual("'Shift+Foo' is no key name", KeyMask.Problem("Shift+Foo", Find));

    [TestMethod]
    public void AChordThatReadsHasNoProblem() => Assert.IsNull(KeyMask.Problem("Ctrl+Shift+R", Find));

    [TestMethod]
    public void AKeyNameInFrontOfAModifierIsNamedInTheProblem() =>
        Assert.AreEqual(
            "'G+Shift' is no key name: 'G' is not a modifier Godot knows",
            KeyMask.Problem("G+Shift", Find)
        );

    [TestMethod]
    public void AModifierGivenTwiceIsNoKeyName() =>
        Assert.AreEqual("'Shift+Shift+R' is no key name", KeyMask.Problem("Shift+Shift+R", Find));
}
