using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Assets;
using OpenArmada.Formats;

namespace OpenArmada.Tests;

[TestClass]
public class InterfaceKindTests
{
    readonly TempInstall temp = new();

    [TestCleanup]
    public void CleanUp() => temp.CleanUp();

    GameFileSystem Install(params (string Path, string Content)[] files) => new(temp.MakeInstall(files));

    [TestMethod]
    public void InterfaceConfigurationsAreTheGuiFilesOfMiscAndIncludesResolveThere()
    {
        var files = Install(
            ("misc/gui_fed.cfg", "#include \"gui_glob.cfg\"\nb = \"é\"\n"),
            ("misc/gui_glob.cfg", "a = 1\n"),
            ("misc/other.cfg", "x"),
            ("CCA.cfg", "int x;")
        );
        var kind = new InterfaceConfigKind();
        CollectionAssert.AreEquivalent(
            new[] { "misc/gui_fed.cfg", "misc/gui_glob.cfg" },
            kind.Enumerate(files).ToArray()
        );
        var cfg = (InterfaceConfig)kind.Load(files, "misc/gui_fed.cfg");
        CollectionAssert.AreEqual(new[] { "a", "b" }, cfg.Entries.Select(e => e.Key).ToArray());
        Assert.AreEqual("é", cfg.Entries[1].Values[0]); // Windows-1252 byte 0xE9
    }

    [TestMethod]
    public void AnAddonCopyOfAConfigurationOrOfItsIncludeWinsOverTheMiscCopy()
    {
        var files = Install(
            ("misc/gui_fed.cfg", "#include \"gui_glob.cfg\"\na = 1\n"),
            ("misc/gui_glob.cfg", "g = 1\n"),
            ("addon/gui_glob.cfg", "g = 2\n"),
            ("addon/gui_fed.cfg", "#include \"gui_glob.cfg\"\na = 3\n")
        );
        var cfg = (InterfaceConfig)new InterfaceConfigKind().Load(files, "misc/gui_fed.cfg");
        CollectionAssert.AreEqual(new[] { "2", "3" }, cfg.Entries.Select(e => e.Values[0]).ToArray());
    }

    [TestMethod]
    public void InterfaceConfigurationCheckReturnsItsNotes()
    {
        var files = Install(("misc/gui_fed.cfg", "a = 1\njunk\n"));
        StringAssert.StartsWith(new InterfaceConfigKind().Check(files, "misc/gui_fed.cfg").Single(), "gui_fed.cfg:2:");
    }

    [TestMethod]
    public void MapsAreTheRootMapFilesDecodedAsWindows1252()
    {
        var files = Install(
            ("label.map", "m {\n + t {café}\n}\n"),
            ("Input.map", "a {\n + k v\n}\n"),
            ("misc/x.map", "")
        );
        var kind = new KeyMapKind();
        CollectionAssert.AreEquivalent(new[] { "label.map", "Input.map" }, kind.Enumerate(files).ToArray());
        Assert.AreEqual("café", ((MapFile)kind.Load(files, "label.map")).Entries.Single().Value);
    }

    [TestMethod]
    public void MapCheckReturnsNotesWithTheFileName()
    {
        var files = Install(("label.map", "junk\n"));
        StringAssert.StartsWith(new KeyMapKind().Check(files, "label.map").Single(), "label.map:1:");
    }

    [TestMethod]
    public void SettingsAreTheRootSetFiles()
    {
        var files = Install(("mshell.set", "fed1.bzn\n"), ("shell.set", "x"));
        var kind = new SettingsKind();
        CollectionAssert.AreEquivalent(new[] { "mshell.set", "shell.set" }, kind.Enumerate(files).ToArray());
        Assert.AreEqual(1, ((SettingsFile)kind.Load(files, "mshell.set")).Entries.Count);
    }

    [TestMethod]
    public void FontsAreTheRootFntFilesInAnyCase()
    {
        var files = Install(("BZONE.FNT", ""), ("other.txt", ""));
        CollectionAssert.AreEqual(new[] { "BZONE.FNT" }, new FontKind().Enumerate(files).ToArray());
    }

    [TestMethod]
    public void CursorsAreTheCurFilesOfTheCursorsFolder()
    {
        var files = Install(("cursors/cursor1.cur", ""), ("cursor_x.cur", ""));
        CollectionAssert.AreEqual(new[] { "cursors/cursor1.cur" }, new CursorKind().Enumerate(files).ToArray());
    }

    [TestMethod]
    public void KindsAreFoundByDiscovery()
    {
        var names = AssetKind.Discover().Select(k => k.Name).ToArray();
        foreach (
            var n in new[]
            {
                InterfaceConfigKind.KindName,
                SettingsKind.KindName,
                TextTableKind.KindName,
                KeyMapKind.KindName,
                FontKind.KindName,
                CursorKind.KindName,
            }
        )
            CollectionAssert.Contains(names, n);
    }
}
