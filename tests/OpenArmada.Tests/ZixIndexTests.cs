using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Formats;

namespace OpenArmada.Tests;

[TestClass]
public class ZixIndexTests
{
    // Built in code (INV-0001); same layout as startrek.zix: count, volume section, DIR section, entries.
    const string Text = "000003\r\n0 d:\\x x 'Disc\r\n----\r\nDIR: assets.zfs\r\nDIR: sounds\\music\r\n----\r\n1 A.ODF\r\n2 song.wav\r\n";

    [TestMethod]
    public void ParsesDirsInOrderAndEntriesAgainstThem()
    {
        var zix = ZixIndex.Parse(Text);
        CollectionAssert.AreEqual(new[] { "assets.zfs", "sounds\\music" }, zix.Dirs.ToArray());
        Assert.AreEqual(1, zix.Entries["a.odf"]);
        Assert.AreEqual(2, zix.Entries["SONG.WAV"]);
        Assert.AreEqual(0, zix.Faults.Count);
    }

    [TestMethod]
    public void CommentsAndBlankLinesAreIgnored()
    {
        var zix = ZixIndex.Parse("1\n-\n-\n\n# note\n  1 a.odf # trailing\n".Replace("-\n-", "-\nDIR: d\n-"));
        Assert.AreEqual(1, zix.Entries["a.odf"]);
    }

    [TestMethod]
    public void EntryWithUnknownDirIsReportedAndOthersLoad()
    {
        var zix = ZixIndex.Parse(Text + "7 lost.odf\n3 zero.odf\n0 z.odf\nx y\n");
        Assert.IsFalse(zix.Entries.ContainsKey("lost.odf"));
        Assert.AreEqual(4, zix.Faults.Count);
        StringAssert.Contains(zix.Faults[0], "lost.odf");
        Assert.AreEqual(2, zix.Entries["song.wav"]);
    }

    [TestMethod]
    public void NamesAreCutAtTheGamesSixteenCharacterKey()
    {
        // The stock index has a line whose name runs on past the 16 characters of the archive entry.
        var zix = ZixIndex.Parse(Text + "1 testobjectives.tôJ\n");
        Assert.AreEqual(1, zix.Entries["testobjectives.t"]);
    }

    [TestMethod]
    public void NonDirPathLineIsReportedAndKeepsItsSlot()
    {
        var zix = ZixIndex.Parse("1\n-\nCD0 \\disc\nDIR: d\n-\n2 a.odf\n");
        Assert.AreEqual(1, zix.Faults.Count);
        Assert.AreEqual(2, zix.Entries["a.odf"]);
        Assert.IsNull(zix.Dirs[0]);
    }

    [TestMethod]
    public void MissingSectionsAreUnreadable() =>
        Assert.ThrowsExactly<InvalidDataException>(() => ZixIndex.Parse("1\n0 d:\\x x\n"));
}
