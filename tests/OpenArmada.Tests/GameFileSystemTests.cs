using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Assets;

namespace OpenArmada.Tests;

[TestClass]
public class GameFileSystemTests
{
    // Per-test temp folders and installs, deleted by CleanUp; one instance per test, so nothing is shared.
    readonly TempInstall temp = new();

    [TestCleanup]
    public void CleanUp() => temp.CleanUp();

    [TestMethod]
    public void FindIgnoresCaseAndSeparatorOfRequestedName()
    {
        var fs = new GameFileSystem(temp.MakeInstall(("SOD/Fente.sod", "x")));
        Assert.IsNotNull(fs.Find(@"SOD\Fente.sod"));
        Assert.IsNotNull(fs.Find("sod/FENTE.SOD"));
    }

    [TestMethod]
    public void FindIgnoresCaseOfFoldersAndFilesOnDisk()
    {
        // On a case-sensitive file system the on-disk case differs from every request.
        var fs = new GameFileSystem(temp.MakeInstall(("SOD/Deep Dir/FeNtE.SoD", "x")));
        Assert.AreEqual(1, fs.ReadAllBytes("sod\\deep dir\\fente.sod").Length);
    }

    [TestMethod]
    public void FindReturnsNullForMissingFile()
    {
        var fs = new GameFileSystem(temp.MakeInstall());
        Assert.IsNull(fs.Find("SOD/none.sod"));
    }

    [TestMethod]
    public void BareNameFullColorTextureBeatsEightBit()
    {
        var fs = new GameFileSystem(temp.MakeInstall(("Textures/Index8/a.tga", "8"), ("Textures/RGB/a.tga", "rgb")));
        Assert.AreEqual("rgb", System.Text.Encoding.ASCII.GetString(fs.ReadAllBytes("A.TGA")));
    }

    [TestMethod]
    public void BareNameMainModelFolderBeatsSoftwareCopy()
    {
        var fs = new GameFileSystem(temp.MakeInstall(("SOD/Software/m.sod", "soft"), ("SOD/m.sod", "main")));
        Assert.AreEqual("main", System.Text.Encoding.ASCII.GetString(fs.ReadAllBytes("m.sod")));
    }

    [TestMethod]
    public void ListNamesFilesAcrossFoldersOnceEach()
    {
        var fs = new GameFileSystem(
            temp.MakeInstall(
                ("SOD/B.SOD", "1"),
                ("SOD/Software/b.sod", "2"),
                ("SOD/Software/c.sod", "3"),
                ("SOD/x.txt", "4")
            )
        );
        CollectionAssert.AreEquivalent(new[] { "B.SOD", "c.sod" }, fs.List(".sod", "SOD", "SOD/Software").ToArray());
    }

    [TestMethod]
    public void InventoryHoldsEveryFile()
    {
        var fs = new GameFileSystem(temp.MakeInstall(("SOD/a.sod", "1"), ("Textures/RGB/b.tga", "2")));
        CollectionAssert.AreEquivalent(new[] { "SOD/a.sod", "Textures/RGB/b.tga" }, fs.Inventory().ToArray());
    }

    [TestMethod]
    public void FolderWithoutSodIsNotAnInstall()
    {
        var dir = temp.MakeDir();
        Assert.ThrowsExactly<DirectoryNotFoundException>(() => new GameFileSystem(dir));
    }

    [TestMethod]
    public void SodEntryThatIsAFileIsNotAnInstall()
    {
        var dir = temp.MakeDir();
        File.WriteAllText(Path.Combine(dir, "SOD"), "x");
        Assert.ThrowsExactly<DirectoryNotFoundException>(() => new GameFileSystem(dir));
    }

    [TestMethod]
    public void RootThatIsAFileIsNotAnInstall()
    {
        var file = Path.Combine(temp.MakeDir(), "game");
        File.WriteAllText(file, "x");
        Assert.ThrowsExactly<DirectoryNotFoundException>(() => new GameFileSystem(file));
    }

    [TestMethod]
    public void BareModelNameIgnoresTextureFolders()
    {
        var fs = new GameFileSystem(temp.MakeInstall(("Textures/RGB/x.sod", "tex"), ("SOD/x.sod", "model")));
        Assert.AreEqual("model", System.Text.Encoding.ASCII.GetString(fs.ReadAllBytes("x.sod")));
    }

    [TestMethod]
    public void BareTextureNameIgnoresModelFolders()
    {
        var fs = new GameFileSystem(temp.MakeInstall(("SOD/x.tga", "model")));
        Assert.IsNull(fs.Find("x.tga"));
    }
}
