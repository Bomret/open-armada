using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Formats;

namespace OpenArmada.Tests;

[TestClass]
public class OdfTests
{
    static string[] Tokens(string text, string key) =>
        Odf.Parse(text).Entries.Single(e => e.Key == key).Tokens.ToArray();

    [TestMethod]
    public void ParsesKeyValueWithTabsAndSpaces() =>
        CollectionAssert.AreEqual(new[] { "4.0" }, Tokens("maxRoll\t\t= 4.0\r\n", "maxRoll"));

    [TestMethod]
    public void QuotedStringWithSpacesIsOneToken() =>
        CollectionAssert.AreEqual(new[] { "Star Base" }, Tokens("unitName = \"Star Base\"", "unitName"));

    [TestMethod]
    public void SeveralQuotedNamesAndNumbersAreSeparateTokens()
    {
        CollectionAssert.AreEqual(new[] { "hp20", "hp21" }, Tokens("hps = \"hp20\" \"hp21\"", "hps"));
        CollectionAssert.AreEqual(new[] { "1.0", "0.0", "0.5f" }, Tokens("lightColor = 1.0 0.0 0.5f", "lightColor"));
    }

    [TestMethod]
    public void CommentsAreDroppedButNotInsideQuotes()
    {
        var odf = Odf.Parse("// full line\r\nname = \"a//b\" // trailing\r\nshot = 2.0f//tight\r\n");
        Assert.AreEqual(2, odf.Entries.Count);
        CollectionAssert.AreEqual(new[] { "a//b" }, odf.Entries[0].Tokens.ToArray());
        CollectionAssert.AreEqual(new[] { "2.0f" }, odf.Entries[1].Tokens.ToArray());
        Assert.AreEqual(0, odf.Problems.Count);
    }

    [TestMethod]
    public void ValueContinuesOnIndentedLines()
    {
        var odf = Odf.Parse("names = \r\n\t\"a b\" \"c\"\r\n\t\"d\"\r\nnext = 1\r\n");
        CollectionAssert.AreEqual(new[] { "a b", "c", "d" }, odf.Entries[0].Tokens.ToArray());
        Assert.AreEqual("next", odf.Entries[1].Key);
    }

    [TestMethod]
    public void QuotedNamesOnFollowingLinesContinueTheValueEvenWithoutIndent()
    {
        var odf = Odf.Parse("hps = \"a\" \"b\"\r\n\"c\" \"d\"\r\n// note\r\n\"e\"\r\nnext = 1\r\n");
        CollectionAssert.AreEqual(new[] { "a", "b", "c", "d", "e" }, odf.Entries[0].Tokens.ToArray());
        Assert.AreEqual(0, odf.Problems.Count);
    }

    [TestMethod]
    public void EmptyValueIsAnEmptyList() =>
        Assert.AreEqual(0, Tokens("names =\r\n", "names").Length);

    [TestMethod]
    public void IncludesAreListedInOrderAndSectionsAreRecorded()
    {
        var odf = Odf.Parse("#include \"a.odf\"\r\n[Builder]\r\nweapon1 = \"x\"\r\n#include \"b.odf\"\r\n");
        CollectionAssert.AreEqual(new[] { "a.odf", "b.odf" }, odf.Includes.ToArray());
        Assert.AreEqual("Builder", odf.Entries.Single().Section);
        Assert.AreEqual("weapon1", odf.Entries.Single().Key);
    }

    [TestMethod]
    public void UnrecognizedLinesAndUnterminatedQuotesAreReportedWithTheirLine()
    {
        var odf = Odf.Parse("a = 1\r\nthis is not a pair\r\nb = \"open\r\nc = 3\r\n");
        Assert.AreEqual(2, odf.Problems.Count);
        StringAssert.StartsWith(odf.Problems[0], "line 2");
        StringAssert.StartsWith(odf.Problems[1], "line 3");
        CollectionAssert.AreEqual(new[] { "a", "c" }, odf.Entries.Select(e => e.Key).ToArray());
    }
}
