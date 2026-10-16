using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Assets;
using static OpenArmada.Tests.ZfsBuilder;

namespace OpenArmada.Tests;

// Archives, the index and the override layer inside GameFileSystem. Every archive and index is built
// in code (INV-0001).
[TestClass]
public class GameFileSystemArchiveTests
{
    readonly TempInstall temp = new();

    [TestCleanup]
    public void CleanUp() => temp.CleanUp();

    static string Latin1(byte[] b) => Encoding.Latin1.GetString(b);

    static string Zix(string dirs, string entries) => $"000001\n0 d:\\x x 'Disc\n----\n{dirs}\n----\n{entries}\n";

    // An install with assets.zfs (key set) and a "misc" folder, both named by the index.
    GameFileSystem Basic(params (string, string)[] more)
    {
        var files = new[]
        {
            ("assets.zfs", Latin1(Build(0x9466279b, M("a.odf", "from archive")))),
            ("misc/m.txt", "from folder"),
            ("startrek.zix", Zix("DIR: assets.zfs\nDIR: misc", "1 A.ODF\n2 M.TXT")),
        };
        return new GameFileSystem(temp.MakeInstall(files.Concat(more).ToArray()));
    }

    static string Read(GameFileSystem fs, string name) => Latin1(fs.ReadAllBytes(name));

    [TestMethod]
    public void IndexNamesResolveFromArchiveAndFolderRegardlessOfCase()
    {
        var fs = Basic();
        Assert.AreEqual("from archive", Read(fs, "a.ODF"));
        Assert.AreEqual("from folder", Read(fs, "m.txt"));
        Assert.AreEqual(0, fs.Faults.Count);
    }

    [TestMethod]
    public void IndexDirFolderIgnoresCaseAndSeparatorOnDisk()
    {
        var fs = new GameFileSystem(temp.MakeInstall(
            ("Sounds/Music/s.wav", "tune"), ("startrek.zix", Zix("DIR: sounds\\music", "1 s.wav"))));
        Assert.AreEqual("tune", Read(fs, "S.WAV"));
    }

    [TestMethod]
    public void AddonLooseFileBeatsArchiveCopy() =>
        Assert.AreEqual("addon loose", Read(Basic(("Addon/A.odf", "addon loose")), "a.odf"));

    [TestMethod]
    public void AddonArchiveBeatsIndexAssignment() =>
        Assert.AreEqual("addon zfs", Read(Basic(("addon/x.zfs", Latin1(Build(0, M("A.ODF", "addon zfs"))))), "a.odf"));

    [TestMethod]
    public void AddonLooseBeatsAddonArchive() =>
        Assert.AreEqual("loose", Read(Basic(("addon/a.odf", "loose"), ("addon/x.zfs", Latin1(Build(0, M("a.odf", "zfs"))))), "a.odf"));

    [TestMethod]
    public void NameNotInIndexFallsBackToLooseFolders()
    {
        var fs = Basic(("SOD/n.sod", "model"));
        Assert.AreEqual("model", Read(fs, "n.sod"));
    }

    [TestMethod]
    public void InstallWithoutIndexLoadsLooseFilesAsBefore()
    {
        var fs = new GameFileSystem(temp.MakeInstall(("SOD/n.sod", "model")));
        Assert.AreEqual("model", Read(fs, "n.sod"));
        Assert.AreEqual(0, fs.Faults.Count);
    }

    [TestMethod]
    public void BadArchiveIsReportedOnceAndOtherFilesStillLoad()
    {
        var fs = Basic(("addon/bad.zfs", "not an archive at all, but long enough to hold a header......"));
        Assert.AreEqual(1, fs.Faults.Count);
        StringAssert.Contains(fs.Faults[0], "bad.zfs");
        Assert.AreEqual("from archive", Read(fs, "a.odf"));
        Assert.AreEqual("from folder", Read(fs, "m.txt"));
        Assert.AreEqual(1, fs.Faults.Count);
    }

    [TestMethod]
    public void UnreadableIndexArchiveReportsOnceNotPerEntry()
    {
        var fs = new GameFileSystem(temp.MakeInstall(
            ("assets.zfs", "garbage garbage garbage garbage garbage"), ("misc/m.txt", "ok"),
            ("startrek.zix", Zix("DIR: assets.zfs\nDIR: misc", "1 a.odf\n1 b.odf\n2 m.txt"))));
        Assert.AreEqual(1, fs.Faults.Count);
        Assert.AreEqual("ok", Read(fs, "m.txt"));
    }

    [TestMethod]
    public void EntryPastEndOfArchiveIsReportedOnceAndOthersLoad()
    {
        var bytes = Build(0, M("a.odf", "a"), M("b.odf", "bb"));
        System.BitConverter.GetBytes((uint)bytes.Length).CopyTo(bytes, 28 + 4 + 36 + 16);
        var fs = new GameFileSystem(temp.MakeInstall(("assets.zfs", Latin1(bytes)), ("startrek.zix", Zix("DIR: assets.zfs", "1 a.odf\n1 b.odf"))));
        Assert.AreEqual("a", Read(fs, "a.odf"));
        Assert.IsNull(fs.Find("b.odf"));
        Assert.AreEqual(2, fs.Faults.Count); // the entry, and the index line that names it
        Assert.IsTrue(fs.Faults.Any(f => f.Contains("b.odf") && f.Contains("assets.zfs")));
    }

    [TestMethod]
    public void IndexEntryWhoseFileIsMissingIsReportedOnceAndOthersLoad()
    {
        var fs = new GameFileSystem(temp.MakeInstall(("misc/m.txt", "ok"), ("startrek.zix", Zix("DIR: misc", "1 gone.txt\n1 m.txt"))));
        Assert.IsNull(fs.Find("gone.txt"));
        Assert.IsNull(fs.Find("gone.txt"));
        Assert.AreEqual("ok", Read(fs, "m.txt"));
        Assert.AreEqual(1, fs.Faults.Count);
        StringAssert.Contains(fs.Faults[0], "gone.txt");
    }

    [TestMethod]
    public void IndexDirThatDoesNotExistIsReportedOnce()
    {
        var fs = new GameFileSystem(temp.MakeInstall(("startrek.zix", Zix("DIR: nowhere", "1 a.txt\n1 b.txt"))));
        Assert.AreEqual(1, fs.Faults.Count);
        StringAssert.Contains(fs.Faults[0], "nowhere");
    }

    [TestMethod]
    public void BadIndexIsReportedAndLooseFilesStillLoad()
    {
        var fs = new GameFileSystem(temp.MakeInstall(("startrek.zix", "garbage"), ("SOD/n.sod", "model")));
        Assert.AreEqual(1, fs.Faults.Count);
        Assert.AreEqual("model", Read(fs, "n.sod"));
    }

    [TestMethod]
    public void InventoryMarksArchiveMembers()
    {
        var fs = Basic();
        CollectionAssert.Contains(fs.Inventory().ToArray(), "assets.zfs::a.odf");
        CollectionAssert.Contains(fs.Inventory().ToArray(), "misc/m.txt");
    }

    [TestMethod]
    public void MembersListsAnArchiveByName()
    {
        var fs = Basic();
        CollectionAssert.AreEqual(new[] { "a.odf" }, fs.Members("ASSETS.ZFS").ToArray());
        Assert.AreEqual(0, fs.Members("nothere.zfs").Count());
    }

    [TestMethod]
    public void FaultsSurfaceInTheAssetCheckWithoutStoppingIt()
    {
        var game = temp.MakeInstall(("SOD/a.sod", "Storm3D_SW" + "\0\0\0\0"), ("addon/bad.zfs", "garbage garbage garbage garbage garbage garbage"));
        var w = new StringWriter();
        var code = AssetCheck.Run(game, new AssetKind[] { new ModelKind() }, w);
        Assert.AreEqual(1, code);
        StringAssert.Contains(w.ToString(), "FAIL archive/addon/bad.zfs");
        StringAssert.Contains(w.ToString(), "models/a.sod");
    }
}
