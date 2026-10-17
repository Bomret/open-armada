using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Assets;
using OpenArmada.Diagnostics;

namespace OpenArmada.Tests;

[TestClass]
public class ViewerLogTests
{
    [TestMethod]
    public void Category_selection_is_one_info_naming_the_category()
    {
        using var log = new CapturedLog();
        ViewerLog.CategorySelected("models");
        Assert.AreEqual(1, log.Lines.Count);
        StringAssert.Contains(log.Text, " INFO browser: ");
        StringAssert.Contains(log.Text, "models");
    }

    [TestMethod]
    public void Asset_selection_is_one_info_naming_category_and_asset()
    {
        using var log = new CapturedLog();
        ViewerLog.AssetSelected("models", "Fente.sod");
        Assert.AreEqual(1, log.Lines.Count);
        StringAssert.Contains(log.Text, " INFO browser: ");
        StringAssert.Contains(log.Text, "models");
        StringAssert.Contains(log.Text, "Fente.sod");
    }

    [TestMethod]
    public void Browser_failure_is_one_error()
    {
        using var log = new CapturedLog();
        ViewerLog.Failed("no install given");
        StringAssert.Contains(log.Text, " ERROR browser: no install given");
        Assert.AreEqual(1, Log.ErrorCount);
    }

    [TestMethod]
    public void No_browser_failure_writes_nothing()
    {
        using var log = new CapturedLog();
        ViewerLog.Failed(null);
        Assert.AreEqual(0, log.Lines.Count);
    }

    [TestMethod]
    public void Sprite_table_texture_that_did_not_load_is_one_warn_with_texture_and_reason()
    {
        using var log = new CapturedLog();
        ViewerLog.TextureNotLoaded("sprites.spr", "hull01", new InvalidDataException("bad magic"));
        Assert.AreEqual(1, log.Lines.Count);
        StringAssert.Contains(log.Text, " WARN browser: ");
        StringAssert.Contains(log.Text, "sprites.spr");
        StringAssert.Contains(log.Text, "hull01");
        StringAssert.Contains(log.Text, "bad magic");
    }

    [TestMethod]
    public void Failed_preview_is_one_error_with_asset_name_and_reason()
    {
        using var log = new CapturedLog();
        ViewerLog.Failed(AssetBrowser.FailureMessage("Wviracod", new InvalidDataException("bad magic")));
        Assert.AreEqual(1, log.Lines.Count);
        StringAssert.Contains(log.Text, " ERROR browser: Wviracod: bad magic");
    }

    [TestMethod]
    public void Start_problems_are_one_warn_each()
    {
        using var log = new CapturedLog();
        ViewerLog.StartProblems(["first", "second"]);
        Assert.AreEqual(2, log.Lines.Count);
        StringAssert.Contains(log.Lines[0], " WARN browser: first");
        StringAssert.Contains(log.Lines[1], " WARN browser: second");
    }

    [TestMethod]
    public void Missing_preview_is_one_warn_naming_the_kind()
    {
        using var log = new CapturedLog();
        ViewerLog.NoPreview("sounds");
        StringAssert.Contains(log.Text, " WARN browser: no preview for sounds");
        Assert.AreEqual(1, log.Lines.Count);
    }

    [TestMethod]
    public void Preview_notes_are_one_warn_each_with_the_asset_name()
    {
        using var log = new CapturedLog();
        ViewerLog.Notes("keys.odf", ["line 3 skipped", "line 9 skipped"]);
        Assert.AreEqual(2, log.Lines.Count);
        StringAssert.Contains(log.Lines[0], " WARN browser: keys.odf: line 3 skipped");
        StringAssert.Contains(log.Lines[1], " WARN browser: keys.odf: line 9 skipped");
    }

    [TestMethod]
    public void No_notes_write_nothing()
    {
        using var log = new CapturedLog();
        ViewerLog.Notes("keys.odf", []);
        Assert.AreEqual(0, log.Lines.Count);
    }

    [TestMethod]
    public void Unusable_texture_is_one_warn_in_area_render_naming_texture_and_reason()
    {
        using var log = new CapturedLog();
        ViewerLog.TextureUnavailable("hull01", "not a readable TGA");
        Assert.AreEqual(1, log.Lines.Count);
        StringAssert.Contains(log.Text, " WARN render: ");
        StringAssert.Contains(log.Text, "hull01");
        StringAssert.Contains(log.Text, "not a readable TGA");
    }

    [TestMethod]
    public void Saved_screenshot_is_one_info_with_the_absolute_path()
    {
        using var log = new CapturedLog();
        ViewerLog.Screenshot("out.png", null);
        Assert.AreEqual(1, log.Lines.Count);
        StringAssert.Contains(log.Text, " INFO browser: ");
        StringAssert.Contains(log.Text, Path.GetFullPath("out.png"));
    }

    [TestMethod]
    public void A_viewport_with_no_image_is_a_failed_capture_naming_the_cause()
    {
        StringAssert.Contains(ViewerLog.CaptureFailure(false, null), "no viewport image");
    }

    [TestMethod]
    public void A_captured_image_fails_only_with_the_save_error()
    {
        Assert.IsNull(ViewerLog.CaptureFailure(true, null));
        Assert.AreEqual("Cannot open file", ViewerLog.CaptureFailure(true, "Cannot open file"));
    }

    [TestMethod]
    public void A_mesh_texture_that_is_also_a_problem_gets_no_render_warn()
    {
        using var log = new CapturedLog();
        var misses = new System.Collections.Generic.Dictionary<string, string?>
        {
            ["hull01"] = "not a readable TGA",
            ["glow"] = "missing",
        };
        ViewerLog.TexturesUnavailable(
            misses,
            new System.Collections.Generic.HashSet<string> { "HULL01" },
            new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase)
        );
        Assert.AreEqual(1, log.Lines.Count);
        StringAssert.Contains(log.Text, " WARN render: ");
        StringAssert.Contains(log.Text, "glow");
    }

    [TestMethod]
    public void A_builder_logs_a_missing_mesh_texture_once_across_builds()
    {
        using var log = new CapturedLog();
        var logged = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var none = new System.Collections.Generic.HashSet<string>();
        for (var build = 0; build < 2; build++)
        {
            var misses = new System.Collections.Generic.Dictionary<string, string?> { ["glow"] = "missing" };
            ViewerLog.TexturesUnavailable(misses, none, logged);
        }
        Assert.AreEqual(1, log.Lines.Count);
    }

    [TestMethod]
    public void Failed_screenshot_is_one_error_with_path_and_reason()
    {
        using var log = new CapturedLog();
        ViewerLog.Screenshot("out.png", "Cannot open file");
        Assert.AreEqual(1, log.Lines.Count);
        StringAssert.Contains(log.Text, " ERROR browser: ");
        StringAssert.Contains(log.Text, Path.GetFullPath("out.png"));
        StringAssert.Contains(log.Text, "Cannot open file");
    }
}
