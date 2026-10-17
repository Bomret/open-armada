using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Assets;
using OpenArmada.Sandbox;

namespace OpenArmada.Tests;

// The alert bindings read from an Input.map built in code (INV-0001).
[TestClass]
public class AlertBindingsTests
{
    readonly TempInstall temp = new();

    [TestCleanup]
    public void CleanUp() => temp.CleanUp();

    // One block per stance: the key and the modifiers `+`/`-` lines name.
    const string Reference =
        "# set green alert\ngreen {\n\t+ keyboard\tG\n\t- keyboard \tControl\n\t+ keyboard\tShift\n\t- keyboard\tAlt\n}\n"
        + "yellow {\n\t+ keyboard\tY\n\t- keyboard \tControl\n\t+ keyboard\tShift\n\t- keyboard\tAlt\n}\n"
        + "red {\n\t+ keyboard\tR\n\t- keyboard \tControl\n\t+ keyboard\tShift\n\t- keyboard\tAlt\n}\n";

    (AlertBindings Bindings, List<string> Gaps) Read(params (string Path, string Content)[] files)
    {
        var gaps = new List<string>();
        return (AlertBindings.Read(new GameFileSystem(temp.MakeInstall(files)), gaps.Add), gaps);
    }

    [TestMethod]
    public void TheReferenceBindingsAreShiftGShiftYAndShiftR()
    {
        var (b, gaps) = Read(("Input.map", Reference));
        Assert.AreEqual(AlertStance.Green, b.For(new KeyChord("G", Shift: true)));
        Assert.AreEqual(AlertStance.Yellow, b.For(new KeyChord("Y", Shift: true)));
        Assert.AreEqual(AlertStance.Red, b.For(new KeyChord("R", Shift: true)));
        Assert.AreEqual(0, gaps.Count);
    }

    [TestMethod]
    public void AChordWithOtherModifiersOrAnotherKeyIsBoundToNothing()
    {
        var (b, _) = Read(("Input.map", Reference));
        Assert.IsNull(b.For(new KeyChord("G")));
        Assert.IsNull(b.For(new KeyChord("G", Shift: true, Control: true)));
        Assert.IsNull(b.For(new KeyChord("G", Shift: true, Alt: true)));
        Assert.IsNull(b.For(new KeyChord("H", Shift: true)));
    }

    [TestMethod]
    public void AChangedBindingChangesTheKey()
    {
        var (b, _) = Read(
            ("Input.map", Reference.Replace("\tG\n", "\tF5\n").Replace("+ keyboard\tShift", "- keyboard\tShift"))
        );
        Assert.AreEqual(AlertStance.Green, b.For(new KeyChord("F5")));
        Assert.IsNull(b.For(new KeyChord("G", Shift: true)));
    }

    [TestMethod]
    public void AKeyIsMatchedWithoutRegardToCase()
    {
        var (b, _) = Read(("Input.map", Reference.Replace("\tG\n", "\tg\n")));
        Assert.AreEqual(AlertStance.Green, b.For(new KeyChord("G", Shift: true)));
    }

    [TestMethod]
    public void AMissingInputMapIsAGapAndBindsNothing()
    {
        var (b, gaps) = Read(("other.txt", "x"));
        Assert.AreEqual(1, gaps.Count);
        StringAssert.Contains(gaps[0], "Input.map");
        Assert.IsNull(b.For(new KeyChord("G", Shift: true)));
    }

    [TestMethod]
    public void AMissingBindingIsAGapAndTheOthersStillWork()
    {
        var (b, gaps) = Read(("Input.map", Reference.Replace("yellow {", "other {")));
        Assert.AreEqual(1, gaps.Count);
        StringAssert.Contains(gaps[0], "yellow");
        Assert.AreEqual(AlertStance.Red, b.For(new KeyChord("R", Shift: true)));
    }

    [TestMethod]
    public void ABindingWithoutAKeyIsAGap()
    {
        var (_, gaps) = Read(("Input.map", Reference.Replace("\t+ keyboard\tR\n", "")));
        Assert.AreEqual(1, gaps.Count);
        StringAssert.Contains(gaps[0], "red");
    }
}
