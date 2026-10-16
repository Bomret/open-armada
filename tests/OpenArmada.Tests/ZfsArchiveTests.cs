using System;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Formats;
using static OpenArmada.Tests.ZfsBuilder;

namespace OpenArmada.Tests;

[TestClass]
public class ZfsArchiveTests
{
    static string Text(byte[] b) => Encoding.Latin1.GetString(b);

    [TestMethod]
    public void KeyedDataWithTrailingRemainderReturnsOriginalBytes()
    {
        // 7 bytes: one XORed word plus 3 plain trailing bytes.
        var zfs = ZfsArchive.Open(Build(0x9466279b, M("a.txt", "abcdefg"), M("b.txt", "xy")));
        Assert.AreEqual("abcdefg", Text(zfs.Read("A.TXT")));
        Assert.AreEqual("xy", Text(zfs.Read("b.txt")));
    }

    [TestMethod]
    public void KeyZeroReturnsDataUnchanged()
    {
        var bytes = Build(0, M("a.txt", "abcdefgh"));
        var zfs = ZfsArchive.Open(bytes);
        Assert.AreEqual("abcdefgh", Text(zfs.Read("a.txt")));
        StringAssert.Contains(Text(bytes), "abcdefgh");
    }

    [TestMethod]
    public void ManyMembersSpanChainedBlocks()
    {
        var members = Enumerable.Range(0, 250).Select(i => M($"f{i}.txt", $"v{i}")).ToArray();
        var zfs = ZfsArchive.Open(Build(7, members));
        Assert.AreEqual(250, zfs.Names.Count());
        Assert.AreEqual("v249", Text(zfs.Read("f249.txt")));
    }

    [TestMethod]
    public void DeletedEntryIsSkipped()
    {
        var zfs = ZfsArchive.Open(Build(0, M("a.txt", "a"), M("gone.txt", "b", 1)));
        CollectionAssert.AreEqual(new[] { "a.txt" }, zfs.Names.ToArray());
    }

    [TestMethod]
    public void CompressedEntryIsUnsupported()
    {
        var zfs = ZfsArchive.Open(Build(0, M("c.txt", "zz", 2 | (10u << 8))));
        Assert.ThrowsExactly<UnsupportedFormatException>(() => zfs.Read("c.txt"));
    }

    [TestMethod]
    public void UnknownFlagBitsAreReportedAndEntrySkipped()
    {
        var zfs = ZfsArchive.Open(Build(0, M("a.txt", "a"), M("odd.txt", "b", 8)));
        CollectionAssert.AreEqual(new[] { "a.txt" }, zfs.Names.ToArray());
        Assert.AreEqual(1, zfs.Faults.Count);
        StringAssert.Contains(zfs.Faults[0], "odd.txt");
    }

    [TestMethod]
    public void BadHeaderIsUnreadable()
    {
        foreach (var (at, value) in new[] { (4, 2u), (8, 15u), (12, 99u) })
        {
            var bytes = Build(0, M("a.txt", "a"));
            BitConverter.GetBytes(value).CopyTo(bytes, at);
            Assert.ThrowsExactly<InvalidDataException>(() => ZfsArchive.Open(bytes));
        }
        Assert.ThrowsExactly<InvalidDataException>(() => ZfsArchive.Open(new byte[10]));
        Assert.ThrowsExactly<InvalidDataException>(() =>
            ZfsArchive.Open(Encoding.ASCII.GetBytes("NOPE" + new string('x', 40)))
        );
    }

    [TestMethod]
    public void CutOffBlockChainIsUnreadable()
    {
        var bytes = Build(0, Enumerable.Range(0, 150).Select(i => M($"f{i}", "a")).ToArray());
        Assert.ThrowsExactly<InvalidDataException>(() => ZfsArchive.Open(bytes[..(28 + 0xE14 + 100)]));
    }

    [TestMethod]
    public void ChainLoopIsUnreadable()
    {
        var bytes = Build(0, M("a.txt", "a"));
        BitConverter.GetBytes(28u).CopyTo(bytes, 28); // block points at itself
        Assert.ThrowsExactly<InvalidDataException>(() => ZfsArchive.Open(bytes));
    }

    [TestMethod]
    public void ChainEndingBeforeTheHeaderCountIsUnreadable()
    {
        var bytes = Build(0, Enumerable.Range(0, 150).Select(i => M($"f{i}", "a")).ToArray());
        BitConverter.GetBytes(0u).CopyTo(bytes, 28); // first block says "last", header counts two
        Assert.ThrowsExactly<InvalidDataException>(() => ZfsArchive.Open(bytes));
    }

    [TestMethod]
    public void ChainContinuingPastTheHeaderCountIsUnreadable()
    {
        var bytes = Build(0, Enumerable.Range(0, 150).Select(i => M($"f{i}", "a")).ToArray());
        BitConverter.GetBytes(99u).CopyTo(bytes, 16); // header counts one block, the chain has two
        Assert.ThrowsExactly<InvalidDataException>(() => ZfsArchive.Open(bytes));
    }

    [TestMethod]
    public void FirstBlockInsideTheHeaderIsUnreadable()
    {
        var bytes = Build(0, M("a.txt", "a"));
        BitConverter.GetBytes(4u).CopyTo(bytes, 24);
        Assert.ThrowsExactly<InvalidDataException>(() => ZfsArchive.Open(bytes));
    }

    [TestMethod]
    [Timeout(10000)]
    public void SelfPointingBlockWithHugeFileCountIsUnreadablePromptly()
    {
        var bytes = Build(0, M("a.txt", "a"));
        BitConverter.GetBytes(uint.MaxValue - 1).CopyTo(bytes, 16);
        BitConverter.GetBytes(28u).CopyTo(bytes, 28);
        Assert.ThrowsExactly<InvalidDataException>(() => ZfsArchive.Open(bytes));
    }

    [TestMethod]
    public void HugeFileCountDoesNotAllocate()
    {
        var bytes = Build(0, M("a.txt", "a"));
        BitConverter.GetBytes(uint.MaxValue).CopyTo(bytes, 16);
        Assert.ThrowsExactly<InvalidDataException>(() => ZfsArchive.Open(bytes));
    }

    [TestMethod]
    public void EntryPastEndIsReportedOnceAndOthersLoad()
    {
        var bytes = Build(0, M("a.txt", "a"), M("b.txt", "bb"));
        BitConverter.GetBytes((uint)bytes.Length - 1).CopyTo(bytes, 28 + 4 + 36 + 16); // b.txt offset
        var zfs = ZfsArchive.Open(bytes);
        CollectionAssert.AreEqual(new[] { "a.txt" }, zfs.Names.ToArray());
        Assert.AreEqual(1, zfs.Faults.Count);
        StringAssert.Contains(zfs.Faults[0], "b.txt");
        Assert.AreEqual("a", Text(zfs.Read("a.txt")));
    }

    [TestMethod]
    public void ReadOfUnknownMemberThrows() =>
        Assert.ThrowsExactly<FileNotFoundException>(() => ZfsArchive.Open(Build(0)).Read("x"));
}
