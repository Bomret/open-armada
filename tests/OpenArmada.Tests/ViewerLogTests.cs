using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
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
    public void Browser_problem_is_one_error()
    {
        using var log = new CapturedLog();
        ViewerLog.OpenFailed("no install given");
        StringAssert.Contains(log.Text, " ERROR browser: no install given");
        Assert.AreEqual(1, Log.ErrorCount);
    }

    [TestMethod]
    public void Category_that_cannot_list_is_one_error_with_the_notice()
    {
        using var log = new CapturedLog();
        ViewerLog.ListFailed("cannot list models: disk gone");
        StringAssert.Contains(log.Text, " ERROR browser: cannot list models: disk gone");
        Assert.AreEqual(1, log.Lines.Count);
    }

    [TestMethod]
    public void Failed_preview_is_one_error_with_asset_name_and_reason()
    {
        using var log = new CapturedLog();
        ViewerLog.PreviewFailed("Wviracod", new InvalidDataException("bad magic"));
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
