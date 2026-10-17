using System;
using System.Collections.Generic;
using System.IO;
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
    [DataRow("RTS_CFG.h")]
    [DataRow("shell.set")]
    [DataRow("string.ldl")]
    [DataRow("BZONE.FNT")]
    [DataRow("Armada.pal")]
    [DataRow("splash.bmp")]
    [DataRow("addon/a.odf")]
    [DataRow("addon/a.zfs")]
    [DataRow("addon/a.sod")]
    [DataRow("addon/a.tga")]
    [DataRow("addon/a.tt")]
    [DataRow("addon/b.zfs::a.sod")]
    [DataRow("addon/b.zfs::a.tga")]
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

    // The files a GOG install or its unpacked installer adds beside the game.
    [TestMethod]
    [DataRow("__redist/DirectX/DXSETUP.exe")]
    [DataRow("__support/add/gameux.cmd")]
    [DataRow("commonappdata/GOG.com/supportInstaller/uninstall.dll")]
    [DataRow("tmp/GOG200.png")]
    [DataRow("app/webcache.zip")]
    [DataRow("goggame-1582325353.info")]
    [DataRow("ipxwrapper-readme.txt")]
    [DataRow("firewall_u.cmd")]
    [DataRow("directplay-win64.reg")]
    [DataRow("dxcfg.ini")]
    [DataRow("Manual.pdf")]
    public void GogInstallerAndWrapperFilesAreOutOfScope(string entry) =>
        Assert.AreEqual(FileClass.OutOfScope, ClassOf(entry));

    [TestMethod]
    [DataRow("ARMADA.PRF")]
    [DataRow("misc/moon.alb")]
    [DataRow("misc/usrmsn01.trn")]
    [DataRow("giddi/joystick.std")]
    [DataRow("ART_CFG.h")]
    [DataRow("game_cfg.h")]
    [DataRow("Dynamic_Localized_Strings.h")]
    [DataRow("shell_cfg.h")]
    [DataRow("CCA.cfg")]
    [DataRow("events.odf")]
    [DataRow("bmp/Links.bmp")]
    [DataRow("wav/Install.wav")]
    [DataRow("ScrnThms")]
    [DataRow("Dcphr")]
    [DataRow("addon/sub/a.odf")]
    [DataRow("addon/a.wav")]
    [DataRow("addon/a.pal")]
    [DataRow("addon/b.zfs::a.wav")]
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

    // A numbered suffix no mip level has (0, past 9, past the range of an int) is a texture extra, not a crash.
    [TestMethod]
    [DataRow("Textures/RGB/a.0")]
    [DataRow("Textures/RGB/a.10")]
    [DataRow("Textures/RGB/a.99999999999")]
    public void NumberedSuffixNoMipLevelHasIsATextureExtra(string entry) =>
        Assert.AreEqual(FileClass.KnownNotLoaded, ClassOf(entry, "Textures/RGB/a.tga"));

    // An install holding files in and beside every kind's scope, override copies, and copies an override
    // or a loose file shadows, classified with the install opened.
    static IReadOnlyList<Classified> ClassifyFixture()
    {
        var temp = new TempInstall();
        try
        {
            string Tga(int size) => System.Text.Encoding.Latin1.GetString(TgaTests.Make(size, size, 24));
            string[] files =
            {
                "SOD/a.sod",
                "SOD/Software/b.sod",
                "SOD/sub/c.sod",
                "SOD/readme.txt",
                "Textures/RGB/t.tga",
                "Textures/Index8/u.tga",
                "Textures/RGB/sub/v.tga",
                "Textures/RGB/pal1.pal",
                "Armada.pal",
                "Sprites/s.spr",
                "Sprites/sub/w.spr",
                "Sprites/x.txt",
                "bitmaps/ms/m.bmp",
                "bzn/k.bzn",
                "bzn/n.bmp",
                "bzn/o.xyz",
                "splash.bmp",
                "other.bmp",
                "sounds/music/e.wav",
                "sounds/f.wav",
                "wav/g.wav",
                "cursors/c.cur",
                "cursors/sub/d.cur",
                "misc/gui_a.cfg",
                "misc/other.cfg",
                "RTS_CFG.h",
                "ART_CFG.h",
                "addon/ART_CFG.h",
                "l.map",
                "l.set",
                "l.ldl",
                "l.fnt",
                "addon/h.odf",
                "addon/sub/i.odf",
                "events.odf",
                "j.odf",
                "addon/p.sod",
                "addon/r.wav",
                "addon/l.fnt",
                "addon/l.map",
                "addon/l.set",
                "addon/Armada.pal",
                "addon/splash.bmp",
                "addon/gui_a.cfg",
            };
            var root = temp.MakeInstall(
                files
                    .Select(f => (f, ""))
                    .Append(("addon/q.tga", Tga(8)))
                    .Append(("addon/q.1", Tga(4)))
                    .Append(("Textures/RGB/q.tga", Tga(16)))
                    .Append(("Textures/RGB/q.1", Tga(8)))
                    .Append(
                        (
                            "assets.zfs",
                            System.Text.Encoding.Latin1.GetString(
                                ZfsBuilder.Build(
                                    0,
                                    ZfsBuilder.M("a.sod", ""),
                                    ZfsBuilder.M("t.tga", ""),
                                    ZfsBuilder.M("z.odf", "")
                                )
                            )
                        )
                    )
                    .Append(("startrek.zix", "000001\n0 d:\\x x 'Disc\n----\nDIR: assets.zfs\n----\n1 z.odf\n"))
                    .ToArray()
            );
            var fs = new GameFileSystem(root);

            return InstallClassifier.Classify(fs.Inventory(), fs);
        }
        finally
        {
            temp.CleanUp();
        }
    }

    // The Loaded entries of the fixture are exactly the files the game reads, listed by hand: each kind's
    // assets by the lookup the kind loads them with (a bare name takes the override copy, a texture copy
    // another shadows is still checked), and each mip level a texture reads beside its base. The archive and
    // the index are containers, read by the file system rather than a kind.
    [TestMethod]
    public void LoadedEntriesAreExactlyTheFilesTheKindsRead()
    {
        string[] expected =
        {
            "addon/armada.pal",
            "addon/gui_a.cfg",
            "addon/h.odf",
            "addon/l.fnt",
            "addon/l.map",
            "addon/l.set",
            "addon/p.sod",
            "addon/q.1",
            "addon/q.tga",
            "addon/splash.bmp",
            "assets.zfs::z.odf",
            "bitmaps/ms/m.bmp",
            "bzn/k.bzn",
            "bzn/n.bmp",
            "cursors/c.cur",
            "cursors/sub/d.cur",
            "l.ldl",
            "rts_cfg.h",
            "sod/a.sod",
            "sod/software/b.sod",
            "sounds/f.wav",
            "sounds/music/e.wav",
            "sprites/s.spr",
            "textures/index8/u.tga",
            "textures/rgb/pal1.pal",
            "textures/rgb/q.1",
            "textures/rgb/q.tga",
            "textures/rgb/t.tga",
        };
        var loaded = ClassifyFixture()
            .Where(c => c.Class == FileClass.Loaded && !c.Entry.EndsWith(".zfs") && !c.Entry.EndsWith(".zix"))
            .Select(c => c.Entry.ToLowerInvariant())
            .ToHashSet();
        Assert.AreEqual(string.Join(" ", expected.Order()), string.Join(" ", loaded.Order()));
    }

    // An interface configuration loads by its bare name, so an override copy wins over misc/.
    [TestMethod]
    public void InterfaceConfigOverrideIsLoadedAndTheStockCopyIsShadowed()
    {
        var classified = ClassifyFixture();
        Classified Of(string entry) => classified.Single(c => c.Entry == entry);
        Assert.AreEqual(
            (FileClass.Loaded, FileClass.KnownNotLoaded, "shadowed copies"),
            (Of("addon/gui_a.cfg").Class, Of("misc/gui_a.cfg").Class, Of("misc/gui_a.cfg").Group)
        );
    }

    // A stock mip level the stock texture does not read, while the override texture reads a level of its
    // name, is a copy the override shadows rather than a texture extra.
    [TestMethod]
    public void StockMipLevelOfAnOverrideTextureIsAShadowedCopy()
    {
        var temp = new TempInstall();
        try
        {
            string Tga(int size) => System.Text.Encoding.Latin1.GetString(TgaTests.Make(size, size, 24));
            var root = temp.MakeInstall(
                ("addon/q.tga", Tga(8)),
                ("addon/q.1", Tga(4)),
                ("Textures/RGB/q.tga", Tga(16)),
                ("Textures/RGB/q.1", Tga(4))
            );
            var fs = new GameFileSystem(root);
            var stock = InstallClassifier.Classify(fs.Inventory(), fs).Single(c => c.Entry == "Textures/RGB/q.1");
            Assert.AreEqual((FileClass.KnownNotLoaded, "shadowed copies"), (stock.Class, stock.Group));
        }
        finally
        {
            temp.CleanUp();
        }
    }

    [TestMethod]
    public void FileNoRuleClassifiesIsUnclassified()
    {
        Assert.AreEqual(FileClass.Unclassified, ClassOf("mystery/new.thing"));
        Assert.AreEqual(FileClass.Unclassified, ClassOf("other.zfs::a.dat"));
    }
}
