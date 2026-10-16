using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Formats;

namespace OpenArmada.Tests;

// The RTS_CFG.h reader, with headers built in code (INV-0001).
[TestClass]
public class GameConstantsTests
{
    static GameConstants Read(params string[] lines) =>
        GameConstants.Read("RTS_CFG.h", Windows1252.Encoding.GetBytes(string.Join("\r\n", lines) + "\r\n"));

    [TestMethod]
    public void EveryFormOfDeclarationIsReadByNameWithItsNumber()
    {
        var c = Read(
            "// full line comment",
            "#include \"ART_CFG.h\"\t// trailing",
            "#define EASY_AI \"AI_Easy.dsl\"",
            "#define LIMIT 12",
            "float HEIGHT\t= 450.0;\t// tabs",
            "int BORDER = 2;",
            "float DROP = -20;",
            "float RATE = 0.5f;");
        Assert.AreEqual(450.0, c.TryNumber("HEIGHT", out var h, out _) ? h : double.NaN);
        Assert.AreEqual(2.0, c.TryNumber("BORDER", out var b, out _) ? b : double.NaN);
        Assert.AreEqual(-20.0, c.TryNumber("DROP", out var d, out _) ? d : double.NaN);
        Assert.AreEqual(0.5, c.TryNumber("RATE", out var r, out _) ? r : double.NaN);
        Assert.AreEqual(12.0, c.TryNumber("LIMIT", out var l, out _) ? l : double.NaN);
        Assert.AreEqual(0, c.Notes.Count);
        CollectionAssert.AreEqual(new[] { "ART_CFG.h" }, c.Includes.ToArray());
        CollectionAssert.AreEqual(new[] { "EASY_AI", "LIMIT", "HEIGHT", "BORDER", "DROP", "RATE" }, c.Values.Select(v => v.Name).ToArray());
    }

    [TestMethod]
    public void ExpressionAndReferenceAreKeptAsTextWithANoteNamingTheConstant()
    {
        var c = Read("float ANGLE = (3.14159/6);", "float CUT = 6 * ANCHOR_RADIUS;", "float COST = IMPASSABLE_TERRAIN;");
        Assert.AreEqual("(3.14159/6)", c.Values[0].Text);
        Assert.AreEqual("6 * ANCHOR_RADIUS", c.Values[1].Text);
        Assert.AreEqual("IMPASSABLE_TERRAIN", c.Values[2].Text);
        Assert.AreEqual(3, c.Notes.Count);
        StringAssert.Contains(c.Notes[0], "ANGLE");
        StringAssert.Contains(c.Notes[1], "CUT");
        StringAssert.Contains(c.Notes[2], "COST");
        foreach (var name in new[] { "ANGLE", "CUT", "COST" })
        {
            Assert.IsFalse(c.TryNumber(name, out _, out var problem));
            StringAssert.Contains(problem, "not a literal");
        }
    }

    [TestMethod]
    public void AnUppercaseFloatSuffixIsPartOfTheLiteral()
    {
        var c = Read("float RATE = 1.0F;");
        Assert.IsTrue(c.TryNumber("RATE", out var r, out _));
        Assert.AreEqual(1.0, r);
        Assert.AreEqual(0, c.Notes.Count);
    }

    [TestMethod]
    public void AnExponentFormIsPartOfTheLiteral()
    {
        var c = Read("float BIG = 1e3;", "float SMALL = 1.0e-3f;", "float NEG = -2.5E+2F;");
        Assert.IsTrue(c.TryNumber("BIG", out var big, out _));
        Assert.AreEqual(1000.0, big);
        Assert.IsTrue(c.TryNumber("SMALL", out var small, out _));
        Assert.AreEqual(0.001, small);
        Assert.IsTrue(c.TryNumber("NEG", out var neg, out _));
        Assert.AreEqual(-250.0, neg);
        Assert.AreEqual(0, c.Notes.Count);
    }

    [TestMethod]
    public void ALeadingPlusIsPartOfTheLiteral()
    {
        var c = Read("float UP = +1.0;");
        Assert.IsTrue(c.TryNumber("UP", out var up, out _));
        Assert.AreEqual(1.0, up);
        Assert.AreEqual(0, c.Notes.Count);
    }

    [TestMethod]
    public void AnIntWhoseValueIsNotAnIntegerLiteralIsKeptAsTextWithANote()
    {
        var c = Read("int A = 1e3;", "int B = 1.5;", "int C = 2f;", "int D = +4;");
        foreach (var name in new[] { "A", "B", "C" })
        {
            Assert.IsFalse(c.TryNumber(name, out _, out var problem));
            StringAssert.Contains(problem, "not a literal");
        }
        Assert.AreEqual("1e3", c.Values[0].Text);
        Assert.AreEqual(3, c.Notes.Count);
        Assert.IsTrue(c.TryNumber("D", out var d, out _));
        Assert.AreEqual(4.0, d);
    }

    [TestMethod]
    public void AHexLiteralIsKeptAsTextWithANote()
    {
        var c = Read("int MASK = 0x10;");
        Assert.AreEqual("0x10", c.Values[0].Text);
        Assert.AreEqual(1, c.Notes.Count);
        Assert.IsFalse(c.TryNumber("MASK", out _, out var problem));
        StringAssert.Contains(problem, "not a literal");
    }

    [TestMethod]
    public void ADefineExpressionIsKeptAsTextWithANote()
    {
        var c = Read("#define X (1+2)");
        Assert.AreEqual("(1+2)", c.Values[0].Text);
        Assert.IsFalse(c.TryNumber("X", out _, out var problem));
        StringAssert.Contains(problem, "not a literal");
        StringAssert.Contains(c.Notes.Single(), "RTS_CFG.h:1");
    }

    [TestMethod]
    public void ADefineWithoutAValueIsAnUnrecognizedLine()
    {
        var c = Read("#define FLAG");
        Assert.AreEqual(0, c.Values.Count);
        StringAssert.Contains(c.Notes.Single(), "RTS_CFG.h:1");
    }

    [TestMethod]
    public void AQuotedDefineMayHoldTwoSlashes()
    {
        var c = Read("#define URL \"http://x\"");
        Assert.AreEqual("http://x", c.Values[0].Text);
        Assert.AreEqual(0, c.Notes.Count);
    }

    [TestMethod]
    public void LookupReportsMissingAndTextualDistinctly()
    {
        var c = Read("#define EASY_AI \"AI_Easy.dsl\"");
        Assert.IsFalse(c.TryNumber("NOPE", out _, out var missing));
        StringAssert.Contains(missing, "missing");
        Assert.IsFalse(c.TryNumber("EASY_AI", out _, out var text));
        StringAssert.Contains(text, "text");
        Assert.AreEqual("AI_Easy.dsl", c.Values[0].Text);
    }

    [TestMethod]
    public void UnrecognizedLineMissingSemicolonAndDuplicateAreNotes()
    {
        var c = Read("float A = 1.0;", "struct x {", "float B = 2.0", "float A = 3.0;");
        Assert.AreEqual(3, c.Notes.Count);
        StringAssert.Contains(c.Notes[0], "RTS_CFG.h:2");
        StringAssert.Contains(c.Notes[1], "RTS_CFG.h:3");
        StringAssert.Contains(c.Notes[1], "';'");
        StringAssert.Contains(c.Notes[2], "RTS_CFG.h:4");
        StringAssert.Contains(c.Notes[2], "declared again");
        Assert.IsTrue(c.TryNumber("A", out var a, out _));
        Assert.AreEqual(3.0, a);
        Assert.IsTrue(c.TryNumber("B", out var b, out _));
        Assert.AreEqual(2.0, b);
        Assert.AreEqual(2, c.Values.Count);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("  \r\n\t\r\n")]
    public void EmptyInputFailsWithAReasonNamingTheFile(string text)
    {
        var e = Assert.ThrowsExactly<InvalidDataException>(() => GameConstants.Read("RTS_CFG.h", Encoding.ASCII.GetBytes(text)));
        StringAssert.Contains(e.Message, "RTS_CFG.h");
    }

    [TestMethod]
    public void BinaryInputFailsWithAReasonNamingTheFile()
    {
        var e = Assert.ThrowsExactly<InvalidDataException>(() => GameConstants.Read("RTS_CFG.h", new byte[] { 0, 1, 2, 0xFF }));
        StringAssert.Contains(e.Message, "RTS_CFG.h");
    }
}
