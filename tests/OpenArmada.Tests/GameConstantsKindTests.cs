using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Assets;
using OpenArmada.Formats;

namespace OpenArmada.Tests;

// The game constants kind over an install built in code (INV-0001).
[TestClass]
public class GameConstantsKindTests
{
    readonly TempInstall temp = new();

    [TestCleanup]
    public void CleanUp() => temp.CleanUp();

    GameFileSystem Install(params (string Path, string Content)[] files) => new(temp.MakeInstall(files));

    [TestMethod]
    public void TheKindIsFoundByDiscoveryAndListsOnlyRtsCfg()
    {
        var files = Install(
            ("RTS_CFG.h", "float A = 1.0;\n"),
            ("ART_CFG.h", "float B = 2.0;\n"),
            ("misc/aipdef.h", "x")
        );
        CollectionAssert.Contains(AssetKind.Discover().Select(k => k.Name).ToArray(), GameConstantsKind.KindName);
        CollectionAssert.AreEqual(new[] { "RTS_CFG.h" }, new GameConstantsKind().Enumerate(files).ToArray());
    }

    [TestMethod]
    public void LoadReadsTheOverrideCopyWhenOneWins()
    {
        var files = Install(("RTS_CFG.h", "float A = 1.0;\n"), ("addon/RTS_CFG.h", "float A = 5.0;\n"));
        Assert.IsTrue(GameConstantsKind.Of(files).TryNumber("A", out var a, out _));
        Assert.AreEqual(5.0, a);
    }

    [TestMethod]
    public void AFileReachedOnlyThroughTheIndexIsFoundAndLoaded()
    {
        var zix = "000001\n0 d:\\x x 'Disc\n----\nDIR: misc\n----\n1 RTS_CFG.h\n";
        var files = Install(("startrek.zix", zix), ("misc/RTS_CFG.h", "float A = 7.0;\n"));
        CollectionAssert.AreEqual(new[] { "RTS_CFG.h" }, new GameConstantsKind().Enumerate(files).ToArray());
        Assert.IsTrue(GameConstantsKind.Of(files).TryNumber("A", out var a, out _));
        Assert.AreEqual(7.0, a);
    }

    [TestMethod]
    public void AnOverrideCopyAloneIsListed()
    {
        var files = Install(("addon/RTS_CFG.h", "float A = 5.0;\n"));
        CollectionAssert.AreEqual(new[] { "RTS_CFG.h" }, new GameConstantsKind().Enumerate(files).ToArray());
    }

    [TestMethod]
    public void CheckReturnsTheNotes()
    {
        var files = Install(("RTS_CFG.h", "junk\n"));
        StringAssert.StartsWith(new GameConstantsKind().Check(files, "RTS_CFG.h").Single(), "RTS_CFG.h:1:");
    }

    [TestMethod]
    public void AnEmptyFileFailsTheCheckWithTheFileName()
    {
        var files = Install(("RTS_CFG.h", ""));
        var e = Assert.ThrowsExactly<System.IO.InvalidDataException>(() =>
            new GameConstantsKind().Check(files, "RTS_CFG.h")
        );
        StringAssert.Contains(e.Message, "RTS_CFG.h");
    }
}
