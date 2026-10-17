using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Formats;

namespace OpenArmada.Tests;

[TestClass]
public class InterfaceConfigTests
{
    static InterfaceConfig Read(string text, params (string Name, string Text)[] others)
    {
        var map = others.ToDictionary(o => o.Name, o => o.Text);

        return InterfaceConfig.Read("gui_x.cfg", text, n => map.GetValueOrDefault(n));
    }

    [TestMethod]
    public void ReadsTextAndNumberValuesIgnoringCommentsAndBlanks()
    {
        var c = Read("// head\n\nspeedCursor\t=\t\"standard\"\t// why\nbarColor = 1       0.6   .5\n");
        var e = c.Entries;
        Assert.AreEqual(2, e.Count);
        Assert.IsTrue(e[0].IsText);
        CollectionAssert.AreEqual(new[] { "standard" }, e[0].Values);
        CollectionAssert.AreEqual(new[] { "1", "0.6", ".5" }, e[1].Values);
        Assert.AreEqual(4, e[1].Line);
        Assert.AreEqual(0, c.Notes.Count);
    }

    [TestMethod]
    public void IncludeExpandsWhereItStandsAndEntriesNameTheirFile()
    {
        var c = Read("a = 1\n#include \"g.cfg\"\nc = 3\n", ("g.cfg", "b = 2\n"));
        CollectionAssert.AreEqual(new[] { "a", "b", "c" }, c.Entries.Select(e => e.Key).ToArray());
        Assert.AreEqual("g.cfg", c.Entries[1].File);
    }

    [TestMethod]
    public void IncludeCycleAndMissingIncludeAreNotesAndTheRestLoads()
    {
        var c = Read(
            "#include \"a.cfg\"\n#include \"nope.cfg\"\nz = 1\n",
            ("a.cfg", "#include \"gui_x.cfg\"\n#include \"a.cfg\"\ny = 2\n"),
            ("gui_x.cfg", "q = 9\n")
        );
        Assert.IsTrue(c.Entries.Any(e => e.Key == "z") && c.Entries.Any(e => e.Key == "y"));
        Assert.IsTrue(c.Notes.Any(n => n.StartsWith("a.cfg:2:") && n.Contains("cycle")), string.Join("|", c.Notes));
        Assert.IsTrue(
            c.Notes.Any(n => n.StartsWith("gui_x.cfg:2:") && n.Contains("nope.cfg")),
            string.Join("|", c.Notes)
        );
    }

    [TestMethod]
    public void UnrecognizedLinesAreNotesWithFileAndLineAndTheRestLoads()
    {
        var c = Read("a = 1\nthis is junk\nb = 1 x\nc = \"open\nd = 2\n");
        CollectionAssert.AreEqual(new[] { "a", "d" }, c.Entries.Select(e => e.Key).ToArray());
        Assert.AreEqual(3, c.Notes.Count);
        StringAssert.StartsWith(c.Notes[0], "gui_x.cfg:2:");
        StringAssert.StartsWith(c.Notes[1], "gui_x.cfg:3:");
        StringAssert.StartsWith(c.Notes[2], "gui_x.cfg:4:");
    }

    [TestMethod]
    public void ElementsSitAtTheirPositionsAndChildrenOffsetByTheirPanel()
    {
        var c = Read(
            """
            speedDefaultCursor = "c"
            speedPanelArea = 146 327 247 40
            speedSeparatorArea = 15 4 8 33
            speedSeparator = "speed_separator"
            otherPanelArea = 5 6 7 8
            stray = 1 2 3 4
            """
        );
        var panel = c.Elements.Single(e => e.Name == "speedPanelArea");
        Assert.AreEqual((146f, 327f, 247f, 40f), (panel.X, panel.Y, panel.Width, panel.Height));
        Assert.IsTrue(panel.IsPanel);
        var sep = c.Elements.Single(e => e.Name == "speedSeparatorArea");
        Assert.AreEqual((161f, 331f, 8f, 33f), (sep.X, sep.Y, sep.Width, sep.Height));
        Assert.AreEqual("speed_separator", sep.Sprite);
        var other = c.Elements.Single(e => e.Name == "otherPanelArea");
        Assert.AreEqual((5f, 6f), (other.X, other.Y));
        Assert.IsFalse(other.IsPanel);
    }

    [TestMethod]
    public void BackgroundPanelPiecesNameTheirSpriteAndSitInThePanel()
    {
        var c = Read(
            """
            speedDefaultCursor = "c"
            speedPanelArea = 100 200 247 40
            speedBackgroundPanel = "speed_panel"
            speedBackgroundPanelSize = 2
            speedBackgroundPanel_0 = 0 0 128 40
            speedBackgroundPanel_1 = 128 0 119 40
            """
        );
        var pieces = c
            .Elements.Where(e => e.Sprite != null && e.Sprite.StartsWith("speed_panel."))
            .OrderBy(e => e.Sprite)
            .ToList();
        Assert.AreEqual(2, pieces.Count);
        Assert.AreEqual("speed_panel.1", pieces[1].Sprite);
        Assert.AreEqual((228f, 200f, 119f, 40f), (pieces[1].X, pieces[1].Y, pieces[1].Width, pieces[1].Height));
    }

    [TestMethod]
    public void IncludeOfAFileAlreadyReadIsANoteNotACycle()
    {
        var c = Read("#include \"g.cfg\"\n#include \"g.cfg\"\n", ("g.cfg", "b = 2\n"));
        Assert.AreEqual(1, c.Entries.Count);
        var note = c.Notes.Single();
        StringAssert.StartsWith(note, "gui_x.cfg:2:");
        StringAssert.Contains(note, "already read");
    }

    [TestMethod]
    public void APanelPieceWithoutFourNumbersIsANote()
    {
        var c = Read(
            "speedDefaultCursor = \"c\"\nspeedPanelArea = 1 2 3 4\nspeedBackgroundPanel = \"p\"\nspeedBackgroundPanel_0 = 0 0 5\n"
        );
        Assert.IsFalse(c.Elements.Any(e => e.Sprite == "p.0"));
        StringAssert.StartsWith(c.Notes.Single(), "gui_x.cfg:4:");
    }

    [TestMethod]
    public void NonFiniteNumbersAreNotNumbers()
    {
        var c = Read("a = NaN\nb = Infinity 1\nc = 2\n");
        CollectionAssert.AreEqual(new[] { "c" }, c.Entries.Select(e => e.Key).ToArray());
        Assert.AreEqual(2, c.Notes.Count);
    }

    [TestMethod]
    public void AnAreaWithoutFourNumbersIsANote()
    {
        var c = Read("aArea = 1 2 3\n");
        Assert.AreEqual(0, c.Elements.Count);
        Assert.IsTrue(c.Notes.Single().StartsWith("gui_x.cfg:1:"));
    }
}
