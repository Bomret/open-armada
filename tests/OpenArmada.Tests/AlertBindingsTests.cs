using System.Collections.Generic;
using System.Linq;
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
    static readonly string Reference =
        "# set green alert\ngreen {\n\t+ keyboard\tG\n\t- keyboard \tControl\n\t+ keyboard\tShift\n\t- keyboard\tAlt\n}\n"
        + "yellow {\n\t+ keyboard\tY\n\t- keyboard \tControl\n\t+ keyboard\tShift\n\t- keyboard\tAlt\n}\n"
        + "red {\n\t+ keyboard\tR\n\t- keyboard \tControl\n\t+ keyboard\tShift\n\t- keyboard\tAlt\n}\n"
        + "transport {\n\t+ keyboard\tT\n\t- keyboard \tControl\n\t- keyboard\tShift\n\t- keyboard\tAlt\n}\n"
        + string.Concat(
            Enumerable
                .Range(1, 6)
                .Select(n =>
                    $"fire_special_{n} {{\n\t+ keyboard\tF{n}\n\t- keyboard \tControl\n\t- keyboard\tShift\n\t- keyboard\tAlt\n}}\n"
                )
        );

    [TestMethod]
    public void TheSpecialWeaponKeysAreF1ToF6WithNoModifiers()
    {
        var (b, gaps) = Read(("Input.map", Reference));
        Assert.AreEqual(4, b.SpecialSlot(new KeyChord { Key = "F4" }));
        Assert.AreEqual(4, b.SpecialSlot(new KeyChord { Key = "f4" }));
        Assert.IsNull(b.SpecialSlot(new KeyChord { Key = "F4", Shift = true }));
        Assert.IsNull(b.SpecialSlot(new KeyChord { Key = "F7" }));
        Assert.AreEqual(new KeyChord { Key = "F6" }, b.SpecialChord(6));
        Assert.IsNull(b.SpecialChord(7));
        Assert.AreEqual(0, gaps.Count);
    }

    [TestMethod]
    public void AMissingSpecialWeaponBlockIsOneGapAndLeavesThatKeyUnbound()
    {
        var (b, gaps) = Read(("Input.map", Reference.Replace("fire_special_3 {", "other {")));
        Assert.AreEqual(1, gaps.Count);
        StringAssert.Contains(gaps[0], "fire_special_3");
        Assert.IsNull(b.SpecialChord(3));
        Assert.AreEqual(4, b.SpecialSlot(new KeyChord { Key = "F4" }));
    }

    [TestMethod]
    public void TheTransportChordIsTWithNoModifiers()
    {
        var (b, gaps) = Read(("Input.map", Reference));
        Assert.IsTrue(b.IsTransport(new KeyChord { Key = "T" }));
        Assert.IsFalse(b.IsTransport(new KeyChord { Key = "T", Shift = true }));
        Assert.IsFalse(b.IsTransport(new KeyChord { Key = "G", Shift = true }));
        Assert.AreEqual(new KeyChord { Key = "T" }, b.Transport);
        Assert.AreEqual(0, gaps.Count);
    }

    [TestMethod]
    public void AMissingTransportBlockIsOneGapAndLeavesTheOrderUnbound()
    {
        var (b, gaps) = Read(("Input.map", Reference.Replace("transport {", "other {")));
        Assert.AreEqual(1, gaps.Count);
        StringAssert.Contains(gaps[0], "transport");
        Assert.IsNull(b.Transport);
        Assert.IsFalse(b.IsTransport(new KeyChord { Key = "T" }));
        Assert.AreEqual(AlertStance.Green, b.For(new KeyChord { Key = "G", Shift = true }));
    }

    [TestMethod]
    public void AMissingInputMapLeavesTheTransportOrderUnbound()
    {
        var (b, _) = Read(("other.txt", "x"));
        Assert.IsNull(b.Transport);
    }

    (AlertBindings Bindings, List<string> Gaps) Read(params (string Path, string Content)[] files)
    {
        var gaps = new List<string>();

        return (AlertBindings.Read(new GameFileSystem(temp.MakeInstall(files)), gaps.Add), gaps);
    }

    [TestMethod]
    public void TheReferenceBindingsAreShiftGShiftYAndShiftR()
    {
        var (b, gaps) = Read(("Input.map", Reference));
        Assert.AreEqual(AlertStance.Green, b.For(new KeyChord { Key = "G", Shift = true }));
        Assert.AreEqual(AlertStance.Yellow, b.For(new KeyChord { Key = "Y", Shift = true }));
        Assert.AreEqual(AlertStance.Red, b.For(new KeyChord { Key = "R", Shift = true }));
        Assert.AreEqual(0, gaps.Count);
    }

    [TestMethod]
    public void AChordWithOtherModifiersOrAnotherKeyIsBoundToNothing()
    {
        var (b, _) = Read(("Input.map", Reference));
        Assert.IsNull(b.For(new KeyChord { Key = "G" }));
        Assert.IsNull(
            b.For(
                new KeyChord
                {
                    Key = "G",
                    Shift = true,
                    Control = true,
                }
            )
        );
        Assert.IsNull(
            b.For(
                new KeyChord
                {
                    Key = "G",
                    Shift = true,
                    Alt = true,
                }
            )
        );
        Assert.IsNull(b.For(new KeyChord { Key = "H", Shift = true }));
    }

    [TestMethod]
    public void AChangedBindingChangesTheKey()
    {
        var (b, _) = Read(
            ("Input.map", Reference.Replace("\tG\n", "\tF5\n").Replace("+ keyboard\tShift", "- keyboard\tShift"))
        );
        Assert.AreEqual(AlertStance.Green, b.For(new KeyChord { Key = "F5" }));
        Assert.IsNull(b.For(new KeyChord { Key = "G", Shift = true }));
    }

    [TestMethod]
    public void AKeyIsMatchedWithoutRegardToCase()
    {
        var (b, _) = Read(("Input.map", Reference.Replace("\tG\n", "\tg\n")));
        Assert.AreEqual(AlertStance.Green, b.For(new KeyChord { Key = "G", Shift = true }));
    }

    [TestMethod]
    public void AMissingInputMapIsAGapAndBindsNothing()
    {
        var (b, gaps) = Read(("other.txt", "x"));
        Assert.AreEqual(1, gaps.Count);
        StringAssert.Contains(gaps[0], "Input.map");
        Assert.IsNull(b.For(new KeyChord { Key = "G", Shift = true }));
    }

    [TestMethod]
    public void AMissingBindingIsAGapAndTheOthersStillWork()
    {
        var (b, gaps) = Read(("Input.map", Reference.Replace("yellow {", "other {")));
        Assert.AreEqual(1, gaps.Count);
        StringAssert.Contains(gaps[0], "yellow");
        Assert.AreEqual(AlertStance.Red, b.For(new KeyChord { Key = "R", Shift = true }));
    }

    [TestMethod]
    public void ABindingWithoutAKeyIsAGap()
    {
        var (_, gaps) = Read(("Input.map", Reference.Replace("\t+ keyboard\tR\n", "")));
        Assert.AreEqual(1, gaps.Count);
        StringAssert.Contains(gaps[0], "red");
    }

    [TestMethod]
    public void AMalformedLineInAnAlertBlockIsNamedInTheGapOfThatBinding()
    {
        var (_, gaps) = Read(("Input.map", Reference.Replace("\t+ keyboard\tR\n", "\t+ keyboard\n")));
        Assert.AreEqual(1, gaps.Count);
        StringAssert.Contains(gaps[0], "Input.map:");
        StringAssert.Contains(gaps[0], "unrecognized line '+ keyboard'");
    }

    [TestMethod]
    public void AnUnreadableInputMapIsOneErrorEntryEqualToItsGapLine()
    {
        using var log = new CapturedLog();
        var (_, gaps) = Read(("other.txt", "x"));
        StringAssert.EndsWith(log.Lines.Single(), " ERROR combat: " + gaps.Single());
    }

    [TestMethod]
    public void AMissingBindingIsOneWarnEntryEqualToItsGapLine()
    {
        using var log = new CapturedLog();
        var (_, gaps) = Read(("Input.map", Reference.Replace("yellow {", "other {")));
        StringAssert.EndsWith(log.Lines.Single(), " WARN combat: " + gaps.Single());
    }

    [TestMethod]
    public void AMalformedLineOutsideTheAlertBlocksIsNoGap()
    {
        var (_, gaps) = Read(("Input.map", Reference + "other {\n\tbroken\n}\n"));
        Assert.AreEqual(0, gaps.Count);
    }
}
