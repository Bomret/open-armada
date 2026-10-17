using System;
using System.IO;
using System.Text.RegularExpressions;
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
    public void Texture_missing_from_the_install_is_one_warn_naming_texture_and_reason()
    {
        using var log = new CapturedLog();
        ViewerLog.TextureFailed(
            Area.Render,
            "missing01",
            new FileNotFoundException("'missing01.tga' is not in the install")
        );
        var line = Assert.ContainsSingle(log.Lines);
        StringAssert.Matches(
            line,
            new Regex(" WARN render: texture \"missing01\" missing: 'missing01.tga' is not in the install$")
        );
    }

    [TestMethod]
    public void Texture_that_cannot_be_read_is_one_error_naming_texture_and_reason()
    {
        using var log = new CapturedLog();
        ViewerLog.TextureFailed(Area.Combat, "corrupt01", new InvalidDataException("bad magic"));
        var line = Assert.ContainsSingle(log.Lines);
        StringAssert.Matches(line, new Regex(" ERROR combat: texture \"corrupt01\" cannot be read: bad magic$"));
    }

    [TestMethod]
    public void Texture_that_failed_is_one_entry_per_run_whichever_view_loads_it()
    {
        using var log = new CapturedLog();
        ViewerLog.TextureFailed(Area.Render, "twice01", new InvalidDataException("bad magic"));
        ViewerLog.TextureFailed(Area.Combat, "TWICE01", new InvalidDataException("bad magic"));
        Assert.ContainsSingle(log.Lines);
    }

    [TestMethod]
    public void Later_asset_using_a_failed_texture_is_one_info_naming_asset_and_texture()
    {
        using var log = new CapturedLog();
        ViewerLog.TextureFailed(Area.Render, "shared01", new InvalidDataException("bad magic"), "a.sod");
        ViewerLog.TextureFailed(Area.Render, "SHARED01", new InvalidDataException("bad magic"), "b.sod");
        Assert.AreEqual(2, log.Lines.Count);
        StringAssert.Matches(
            log.Lines[1],
            new Regex(" INFO render: b.sod: texture \"SHARED01\" did not load \\(see its first entry above\\)$")
        );
    }

    [TestMethod]
    public void Texture_logged_in_one_captured_log_is_logged_again_in_the_next()
    {
        using (new CapturedLog())
        {
            ViewerLog.TextureFailed(Area.Render, "again01", new InvalidDataException("bad magic"));
        }

        using var log = new CapturedLog();
        ViewerLog.TextureFailed(Area.Render, "again01", new InvalidDataException("bad magic"));
        Assert.ContainsSingle(log.Lines);
    }

    [TestMethod]
    public void Saved_screenshot_is_one_info_with_the_absolute_path()
    {
        using var log = new CapturedLog();
        ViewerLog.Screenshot(Area.Browser, "out.png", null);
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
    public void Failed_screenshot_is_one_error_with_path_and_reason()
    {
        using var log = new CapturedLog();
        ViewerLog.Screenshot(Area.Browser, "out.png", "Cannot open file");
        Assert.AreEqual(1, log.Lines.Count);
        StringAssert.Contains(log.Text, " ERROR browser: ");
        StringAssert.Contains(log.Text, Path.GetFullPath("out.png"));
        StringAssert.Contains(log.Text, "Cannot open file");
    }
}
