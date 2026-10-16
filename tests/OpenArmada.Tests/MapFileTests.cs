using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Formats;

namespace OpenArmada.Tests;

[TestClass]
public class MapFileTests
{
    [TestMethod]
    public void ReadsFlatKeyBindings()
    {
        var m = MapFile.Read("Gamekey.map", "# c\n\nSAVE_GAME\t\t\tCTRL+S\n");
        var e = m.Entries.Single();
        Assert.AreEqual(("", "", "SAVE_GAME", "CTRL+S", 3), (e.Section, e.Sign, e.Key, e.Value, e.Line));
    }

    [TestMethod]
    public void ReadsInputBlocksWithSigns()
    {
        var m = MapFile.Read("Input.map", "chat {\n\t+ keyboard\tEnter\n\t- keyboard \tControl\n}\n");
        Assert.AreEqual(2, m.Entries.Count);
        Assert.AreEqual(
            ("chat", "-", "keyboard", "Control"),
            (m.Entries[1].Section, m.Entries[1].Sign, m.Entries[1].Key, m.Entries[1].Value)
        );
    }

    [TestMethod]
    public void ReadsLabelsWithoutTheirBraces()
    {
        var m = MapFile.Read("label.map", "multi {\n\t+ insert_CD\t\t{Please insert Armada CD}\n}\n");
        Assert.AreEqual("Please insert Armada CD", m.Entries.Single().Value);
    }

    [TestMethod]
    public void UnrecognizedLinesAreNotesWithFileAndLineAndTheRestLoads()
    {
        var m = MapFile.Read("x.map", "a {\n\t+ k v\n\tjunk\n}\n}\nb {\n\t+ k2 v2\n");
        CollectionAssert.AreEqual(new[] { "k", "k2" }, m.Entries.Select(e => e.Key).ToArray());
        Assert.IsTrue(m.Notes.Any(n => n.StartsWith("x.map:3:")), string.Join("|", m.Notes));
        Assert.IsTrue(m.Notes.Any(n => n.StartsWith("x.map:5:")), string.Join("|", m.Notes));
        Assert.IsTrue(
            m.Notes.Any(n => n.StartsWith("x.map:6:") && n.Contains("not closed")),
            string.Join("|", m.Notes)
        );
    }

    [TestMethod]
    public void ASignLineOutsideABlockIsANote()
    {
        var m = MapFile.Read("x.map", "+ k v\n");
        Assert.AreEqual(0, m.Entries.Count);
        Assert.AreEqual(1, m.Notes.Count);
    }
}
