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
        StringAssert.Contains(c.Notes[1], "CUT");
        Assert.IsFalse(c.TryNumber("CUT", out _, out var problem));
        StringAssert.Contains(problem, "not a literal");
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
