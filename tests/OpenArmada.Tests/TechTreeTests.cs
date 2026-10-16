using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Assets;
using OpenArmada.Formats;

namespace OpenArmada.Tests;

[TestClass]
public class TechTreeTests
{
    readonly ResolvedDefinitionTests defs = new();

    [TestCleanup]
    public void CleanUp() => defs.CleanUp();

    [TestMethod]
    public void ParsesNamesWithTheirRequirementsAndSkipsBlankLines()
    {
        var tree = TechTree.Parse("fbase.odf    0\r\n\r\nfconst.odf   1 fbase.odf\r\nffreight.odf\t2 fbase.odf fmining.odf \r\n");
        CollectionAssert.AreEqual(new[] { "fbase.odf", "fconst.odf", "ffreight.odf" }, tree.Entries.Select(e => e.Name).ToArray());
        CollectionAssert.AreEqual(new[] { "fbase.odf", "fmining.odf" }, tree.Entries[2].Requires.ToArray());
        Assert.AreEqual(0, tree.Entries[0].Requires.Count);
    }

    [TestMethod]
    public void ACountThatDisagreesWithTheNamesIsCorrupt()
    {
        var e = Assert.ThrowsExactly<InvalidDataException>(() => TechTree.Parse("a.odf 0\r\nb.odf 2 a.odf\r\n"));
        StringAssert.Contains(e.Message, "line 2");
    }

    [TestMethod]
    public void ALineWithoutACountIsCorrupt() =>
        Assert.ThrowsExactly<InvalidDataException>(() => TechTree.Parse("b.odf x a.odf"));

    [TestMethod]
    public void KindListsDefinitionsAndTreesAndLoadsATree()
    {
        var install = defs.Install(new[] { ("a.odf", "x = 1"), ("one.tt", "a.odf 0\r\n") }, ("b.odf", "x = 2"), ("two.tt", "b.odf 0"));
        CollectionAssert.AreEquivalent(new[] { "a.odf", "b.odf" }, new DefinitionKind().Enumerate(install).ToArray());
        CollectionAssert.AreEquivalent(new[] { "one.tt", "two.tt" }, new TechTreeKind().Enumerate(install).ToArray());
        Assert.AreEqual("a.odf", ((TechTree)new TechTreeKind().Load(install, "one.tt")).Entries[0].Name);
    }

    [TestMethod]
    public void KindsAreFoundByDiscovery()
    {
        Assert.IsTrue(AssetKind.Discover().Any(k => k is DefinitionKind));
        Assert.IsTrue(AssetKind.Discover().Any(k => k is TechTreeKind));
    }

    [TestMethod]
    public void PreviewShowsEachRacesBuildRequirements()
    {
        var install = defs.Install(new[]
        {
            ("fbase.odf", "race = \"federation\""), ("fyard.odf", "race = \"federation\""),
            ("kbase.odf", "race = \"klingon\""), ("odd.odf", "x = 1"),
            ("t.tt", "fbase.odf 0\r\nfyard.odf 1 fbase.odf\r\nkbase.odf 0\r\nodd.odf 0\r\nlost.odf 0\r\n"),
        });
        var text = DefinitionText.Format(install, (TechTree)new TechTreeKind().Load(install, "t.tt"));
        var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
        CollectionAssert.AreEqual(
            new[]
            {
                "federation", "  fbase.odf: no requirements", "  fyard.odf: requires fbase.odf",
                "klingon", "  kbase.odf: no requirements",
                "unknown race", "  lost.odf: no requirements", "  odd.odf: no requirements",
            },
            lines.Where(l => l.Length > 0).ToArray());
    }
}
