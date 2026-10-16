using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Assets;

namespace OpenArmada.Tests;

[TestClass]
public class GameFileSystemTests
{
    // Builds an install in a temp folder from "relative/path" -> content pairs. Fixtures are
    // synthesized here; no byte of a real install is used (INV-0001).
    internal static string MakeInstall(params (string Path, string Content)[] files)
    {
        var root = Path.Combine(Path.GetTempPath(), "oa-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "SOD"));
        foreach (var (path, content) in files)
        {
            var full = Path.Combine(root, path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, content);
        }
        return root;
    }

    [TestMethod]
    public void FindIgnoresCaseAndSeparatorOfRequestedName()
    {
        var fs = new GameFileSystem(MakeInstall(("SOD/Fente.sod", "x")));
        Assert.IsNotNull(fs.Find(@"SOD\Fente.sod"));
        Assert.IsNotNull(fs.Find("sod/FENTE.SOD"));
    }

    [TestMethod]
    public void FindIgnoresCaseOfFoldersAndFilesOnDisk()
    {
        // On a case-sensitive file system the on-disk case differs from every request.
        var fs = new GameFileSystem(MakeInstall(("SOD/Deep Dir/FeNtE.SoD", "x")));
        Assert.AreEqual(1, fs.ReadAllBytes("sod\\deep dir\\fente.sod").Length);
    }

    [TestMethod]
    public void FindReturnsNullForMissingFile()
    {
        var fs = new GameFileSystem(MakeInstall());
        Assert.IsNull(fs.Find("SOD/none.sod"));
    }

    [TestMethod]
    public void BareNameFullColorTextureBeatsEightBit()
    {
        var fs = new GameFileSystem(MakeInstall(("Textures/Index8/a.tga", "8"), ("Textures/RGB/a.tga", "rgb")));
        Assert.AreEqual("rgb", System.Text.Encoding.ASCII.GetString(fs.ReadAllBytes("A.TGA")));
    }

    [TestMethod]
    public void BareNameMainModelFolderBeatsSoftwareCopy()
    {
        var fs = new GameFileSystem(MakeInstall(("SOD/Software/m.sod", "soft"), ("SOD/m.sod", "main")));
        Assert.AreEqual("main", System.Text.Encoding.ASCII.GetString(fs.ReadAllBytes("m.sod")));
    }

    [TestMethod]
    public void ListNamesFilesAcrossFoldersOnceEach()
    {
        var fs = new GameFileSystem(MakeInstall(("SOD/B.SOD", "1"), ("SOD/Software/b.sod", "2"), ("SOD/Software/c.sod", "3"), ("SOD/x.txt", "4")));
        CollectionAssert.AreEquivalent(new[] { "B.SOD", "c.sod" }, fs.List(".sod", "SOD", "SOD/Software").ToArray());
    }

    [TestMethod]
    public void InventoryHoldsEveryFile()
    {
        var fs = new GameFileSystem(MakeInstall(("SOD/a.sod", "1"), ("Textures/RGB/b.tga", "2")));
        CollectionAssert.AreEquivalent(new[] { "SOD/a.sod", "Textures/RGB/b.tga" }, fs.Inventory().ToArray());
    }

    [TestMethod]
    public void FolderWithoutSodIsNotAnInstall()
    {
        var dir = Path.Combine(Path.GetTempPath(), "oa-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        Assert.ThrowsExactly<DirectoryNotFoundException>(() => new GameFileSystem(dir));
    }
}
