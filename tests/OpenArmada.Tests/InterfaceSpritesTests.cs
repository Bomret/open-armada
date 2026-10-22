using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Assets;

namespace OpenArmada.Tests;

[TestClass]
public class InterfaceSpritesTests
{
    const string Head = "sprite_table\n";

    readonly TempInstall temp = new();

    [TestCleanup]
    public void CleanUp() => temp.CleanUp();

    GameFileSystem Install(params (string Path, string Content)[] files) => new(temp.MakeInstall(files));

    [TestMethod]
    public void Unreadable_global_table_is_one_error_naming_the_table_and_the_reason()
    {
        var files = Install(("Sprites/gui_global.spr", "not a table\n"));
        using var log = new CapturedLog();

        var found = InterfaceSprites.Of(files, "gui_fed.cfg");

        var line = Assert.ContainsSingle(log.Lines);
        StringAssert.Matches(
            line,
            new Regex(
                " ERROR browser: gui_fed.cfg: sprite table \"Sprites/gui_global.spr\" cannot be read: .*sprite_table"
            )
        );
        CollectionAssert.AreEqual(new[] { "Sprites/gui_global.spr" }, found.FailedTables.ToArray());
    }

    [TestMethod]
    public void Table_whose_include_is_not_found_is_one_error_and_the_tables_that_load_still_resolve()
    {
        var files = Install(
            ("Sprites/gui_fed.spr", Head + "@include gone.spr\n"),
            ("Sprites/gui_global.spr", Head + "ok t 0 0 1 1\n")
        );
        using var log = new CapturedLog();

        var found = InterfaceSprites.Of(files, "gui_fed.cfg");

        var line = Assert.ContainsSingle(log.Lines);
        StringAssert.Matches(
            line,
            new Regex(" ERROR browser: gui_fed.cfg: sprite table \"Sprites/gui_fed.spr\" cannot be read: .*gone.spr")
        );
        CollectionAssert.AreEqual(new[] { "ok" }, found.Sprites.Keys.ToArray());
    }

    [TestMethod]
    public void Table_that_cannot_be_opened_is_one_error_and_the_tables_that_load_still_resolve()
    {
        if (OperatingSystem.IsWindows())
            Assert.Inconclusive("file modes are a Unix feature");
        var root = temp.MakeInstall(
            ("Sprites/gui_fed.spr", Head + "a t 0 0 1 1\n"),
            ("Sprites/gui_global.spr", Head + "ok t 0 0 1 1\n")
        );
        var locked = Path.Combine(root, "Sprites", "gui_fed.spr");
        File.SetUnixFileMode(locked, UnixFileMode.None);
        try
        {
            using var log = new CapturedLog();

            var found = InterfaceSprites.Of(new GameFileSystem(root), "gui_fed.cfg");

            var line = Assert.ContainsSingle(log.Lines);
            StringAssert.Matches(
                line,
                new Regex(" ERROR browser: gui_fed.cfg: sprite table \"Sprites/gui_fed.spr\" cannot be read: .+")
            );
            CollectionAssert.AreEqual(new[] { "ok" }, found.Sprites.Keys.ToArray());
        }
        finally
        {
            File.SetUnixFileMode(locked, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    [TestMethod]
    public void Later_configuration_needing_a_failed_table_is_one_info_pointing_back_and_a_repeat_writes_nothing()
    {
        var files = Install(("Sprites/gui_global.spr", "not a table\n"));
        using var log = new CapturedLog();

        InterfaceSprites.Of(files, "gui_fed.cfg");
        InterfaceSprites.Of(files, "gui_kling.cfg");
        InterfaceSprites.Of(files, "gui_fed.cfg");
        InterfaceSprites.Of(files, "gui_kling.cfg");

        Assert.AreEqual(2, log.Lines.Count, log.Text);
        StringAssert.Contains(log.Lines[0], " ERROR browser: gui_fed.cfg: ");
        StringAssert.Matches(
            log.Lines[1],
            new Regex(
                " INFO browser: gui_kling.cfg: sprite table \"Sprites/gui_global.spr\" did not load \\(see its first entry above\\)$"
            )
        );
    }

    [TestMethod]
    public void Race_with_no_table_and_tables_that_load_write_nothing_and_resolve_by_stem_prefix()
    {
        var files = Install(
            ("Sprites/gui_federation.spr", Head + "fed t 0 0 1 1\n"),
            ("Sprites/gui_global.spr", Head + "shared t 0 0 1 1\n"),
            ("Sprites/gui_klingon.spr", Head + "kling t 0 0 1 1\n")
        );
        using var log = new CapturedLog();

        var fed = InterfaceSprites.Of(files, "gui_fed.cfg");
        var none = InterfaceSprites.Of(files, "gui_rom.cfg");

        Assert.AreEqual(0, log.Lines.Count, log.Text);
        CollectionAssert.AreEquivalent(new[] { "fed", "shared" }, fed.Sprites.Keys.ToArray());
        CollectionAssert.AreEqual(new[] { "shared" }, none.Sprites.Keys.ToArray());
        Assert.AreEqual(0, fed.FailedTables.Count + none.FailedTables.Count);
    }

    [TestMethod]
    public void Repeated_sprite_name_resolves_to_the_first_table_in_listing_order()
    {
        var files = Install(
            ("Sprites/gui_fed_a.spr", Head + "same ta 0 0 1 1\n"),
            ("Sprites/gui_fed_b.spr", Head + "SAME tb 0 0 1 1\n")
        );
        var first = new SpriteTableKind().Enumerate(files).First();

        var found = InterfaceSprites.Of(files, "gui_fed.cfg");

        Assert.AreEqual(first.EndsWith("_a.spr") ? "ta" : "tb", found.Sprites["same"].Texture);
    }

    [TestMethod]
    public void Each_captured_log_sees_the_failed_table_error()
    {
        for (var run = 0; run < 2; run++)
        {
            var files = Install(("Sprites/gui_global.spr", "not a table\n"));
            using var log = new CapturedLog();

            InterfaceSprites.Of(files, "gui_fed.cfg");

            StringAssert.Contains(Assert.ContainsSingle(log.Lines), " ERROR browser: ");
        }
    }
}
