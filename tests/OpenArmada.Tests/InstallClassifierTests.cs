using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Assets;

namespace OpenArmada.Tests;

[TestClass]
public class InstallClassifierTests
{
    static FileClass ClassOf(string entry, params string[] others) =>
        InstallClassifier.Classify(others.Append(entry).Distinct()).Single(c => c.Entry == entry).Class;

    [TestMethod]
    [DataRow("SOD/a.sod")]
    [DataRow("sod/Software/a.sod")]
    [DataRow("Textures/RGB/a.tga")]
    [DataRow("Textures/RGB/Armada.pal")]
    [DataRow("Sprites/a.spr")]
    [DataRow("bitmaps/ms/a.bmp")]
    [DataRow("bzn/a.bzn")]
    [DataRow("bzn/a.mdf")]
    [DataRow("bzn/a.bmp")]
    [DataRow("3D_Paths/a.pth")]
    [DataRow("sounds/music/a.wav")]
    [DataRow("cursors/a.cur")]
    [DataRow("misc/gui_fed.cfg")]
    [DataRow("input.map")]
    [DataRow("shell.set")]
    [DataRow("string.ldl")]
    [DataRow("BZONE.FNT")]
    [DataRow("Armada.pal")]
    [DataRow("splash.bmp")]
    [DataRow("addon/a.odf")]
    [DataRow("addon/a.zfs")]
    [DataRow("startrek.zix")]
    [DataRow("assets.zfs")]
    [DataRow("assets.zfs::a.odf")]
    [DataRow("assets.zfs::a.tt")]
    [DataRow("objtxt.zfs::anything.txt")]
    public void FilesSomeKindLoadsAreLoaded(string entry) => Assert.AreEqual(FileClass.Loaded, ClassOf(entry));

    [TestMethod]
    [DataRow("animations/a.bik")]
    [DataRow("help/help/a.htm")]
    [DataRow("AI/AIPs/a.aip")]
    [DataRow("AI/a.dsl")]
    [DataRow("missions/a.drl")]
    [DataRow("dll/a.dll")]
    [DataRow("Armada.exe")]
    [DataRow("uninstall/a.exe")]
    [DataRow("woncfg.cfg")]
    [DataRow("texture.dat")]
    [DataRow("misc/aipdef.h")]
    [DataRow("misc/usrmsn01.aip")]
    [DataRow("Logs/logfile.txt")]
    public void VideoHelpAiCodeInstallNetworkAndCopyProtectionAreOutOfScope(string entry) =>
        Assert.AreEqual(FileClass.OutOfScope, ClassOf(entry));

    [TestMethod]
    [DataRow("misc/moon.alb")]
    [DataRow("misc/usrmsn01.trn")]
    [DataRow("giddi/joystick.std")]
    [DataRow("RTS_CFG.h")]
    [DataRow("shell_cfg.h")]
    [DataRow("CCA.cfg")]
    [DataRow("events.odf")]
    [DataRow("bmp/Links.bmp")]
    [DataRow("wav/Install.wav")]
    [DataRow("addon/sub/a.odf")]
    [DataRow("Textures/RGB/pspbrwse.jbf")]
    [DataRow("Textures/Index8/Xspark01")]
    public void UnknownFileGroupsAreKnownButNotLoaded(string entry) =>
        Assert.AreEqual(FileClass.KnownNotLoaded, ClassOf(entry));

    [TestMethod]
    public void MipLevelsOfAnUnbrokenChainAreLoadedAndLevelsPastAGapAreNot()
    {
        string[] chain = { "Textures/RGB/a.tga", "Textures/RGB/a.1", "Textures/RGB/a.2", "Textures/RGB/a.4" };
        Assert.AreEqual(FileClass.Loaded, ClassOf("Textures/RGB/a.2", chain));
        Assert.AreEqual(FileClass.KnownNotLoaded, ClassOf("Textures/RGB/a.4", chain));
    }

    [TestMethod]
    public void FileNoRuleClassifiesIsUnclassified()
    {
        Assert.AreEqual(FileClass.Unclassified, ClassOf("mystery/new.thing"));
        Assert.AreEqual(FileClass.Unclassified, ClassOf("other.zfs::a.dat"));
    }
}
