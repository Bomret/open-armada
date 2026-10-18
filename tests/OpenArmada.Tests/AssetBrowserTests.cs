using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Assets;

namespace OpenArmada.Tests;

[TestClass]
public class AssetBrowserTests
{
    sealed class FakeKind(string name, params string[] assets) : AssetKind
    {
        public override string Name => name;

        public override IEnumerable<string> Enumerate(GameFileSystem f) => assets;

        public override object Load(GameFileSystem f, string n) => n;
    }

    sealed class CountingKind(string name, params string[] assets) : AssetKind
    {
        public int Listings { get; private set; }

        public override string Name => name;

        public override IEnumerable<string> Enumerate(GameFileSystem f)
        {
            Listings++;
            return assets;
        }

        public override object Load(GameFileSystem f, string n) => n;
    }

    sealed class ThrowingKind : AssetKind
    {
        public override string Name => "broken";

        public override IEnumerable<string> Enumerate(GameFileSystem f) => throw new IOException("disk gone");

        public override object Load(GameFileSystem f, string n) => n;
    }

    readonly TempInstall temp = new();

    [TestCleanup]
    public void CleanUp() => temp.CleanUp();

    AssetBrowser Open(params AssetKind[] kinds) => AssetBrowser.Open(temp.MakeInstall(), kinds);

    [TestMethod]
    public void No_install_path_names_what_is_missing()
    {
        var b = AssetBrowser.Open(null, new[] { new FakeKind("models") });
        StringAssert.Contains(b.Problem, "--game");
        StringAssert.Contains(b.Problem, "ARMADA_DIR");
        Assert.AreEqual(0, b.Kinds.Count);
    }

    [TestMethod]
    public void Folder_without_install_names_what_is_missing()
    {
        var b = AssetBrowser.Open(temp.MakeDir(), new[] { new FakeKind("models") });
        StringAssert.Contains(b.Problem, "SOD");
    }

    [TestMethod]
    public void Install_lists_every_kind_and_selects_the_first()
    {
        var b = Open(new FakeKind("textures", "a"), new FakeKind("models", "m"));
        Assert.IsNull(b.Problem);
        CollectionAssert.AreEqual(new[] { "models", "textures" }, b.Kinds.Select(k => k.Name).ToArray());
        Assert.AreEqual("models", b.Kind!.Name);
    }

    [TestMethod]
    public void Assets_are_sorted_without_regard_to_case()
    {
        var b = Open(new FakeKind("models", "b", "C", "a"));
        CollectionAssert.AreEqual(new[] { "a", "b", "C" }, b.Assets.ToArray());
    }

    [TestMethod]
    public void Kind_with_no_assets_is_an_empty_list_not_an_error()
    {
        var b = Open(new FakeKind("models"));
        Assert.AreEqual(0, b.Assets.Count);
        Assert.IsNull(b.Problem);
    }

    [TestMethod]
    public void Kind_that_cannot_list_is_empty_and_reports_why()
    {
        var b = Open(new ThrowingKind());
        Assert.AreEqual(0, b.Assets.Count);
        StringAssert.Contains(b.Notice, "disk gone");
    }

    [TestMethod]
    public void Selecting_another_kind_changes_the_assets()
    {
        var b = Open(new FakeKind("models", "m"), new FakeKind("textures", "t"));
        b.SelectKind(1);
        Assert.AreEqual("textures", b.Kind!.Name);
        CollectionAssert.AreEqual(new[] { "t" }, b.Assets.ToArray());
    }

    [TestMethod]
    public void SelectModel_selects_the_models_kind_even_when_another_kind_sorts_first()
    {
        var b = Open(new FakeKind("audio", "fente.wav"), new FakeKind("models", "Fente.sod", "Other.sod"));
        Assert.AreEqual("audio", b.Kind!.Name);
        Assert.AreEqual(0, b.SelectModel("fente"));
        Assert.AreEqual("models", b.Kind!.Name);
        Assert.AreEqual("Fente.sod", b.Assets[0]);
    }

    [TestMethod]
    public void SelectModel_matches_with_or_without_extension_in_any_case()
    {
        var b = Open(new FakeKind("models", "Fente.sod", "Other.sod"));
        Assert.AreEqual(1, b.SelectModel("OTHER.sod"));
        Assert.AreEqual(1, b.SelectModel("other"));
    }

    [TestMethod]
    public void SelectModel_returns_minus_one_when_the_model_or_the_kind_is_absent()
    {
        Assert.AreEqual(-1, Open(new FakeKind("models", "Fente.sod")).SelectModel("nope"));
        Assert.AreEqual(-1, Open(new FakeKind("audio", "a")).SelectModel("a"));
    }

    // A preview that returns the asset's name as its view, writing `entry` to the log first when given.
    static AssetBrowser.CreatePreview Previewing(string? entry = null) =>
        (files, kind, name) =>
        {
            if (entry != null)
            {
                OpenArmada.Diagnostics.Log.Info(OpenArmada.Diagnostics.Area.Browser, entry);
            }

            return new Preview { View = name };
        };

    AssetBrowser OpenWith(IReadOnlyDictionary<string, AssetBrowser.CreatePreview> previews, params AssetKind[] kinds) =>
        AssetBrowser.Open(temp.MakeInstall(), kinds, previews);

    // The text of each captured line after its time stamp.
    static string[] Entries(CapturedLog log) => log.Lines.Select(l => l[(l.IndexOf(' ') + 1)..]).ToArray();

    [TestMethod]
    public void Showing_a_category_and_an_asset_logs_both_before_the_preview_entries()
    {
        var b = OpenWith(
            new Dictionary<string, AssetBrowser.CreatePreview>
            {
                ["models"] = Previewing(),
                ["textures"] = Previewing("preview built"),
            },
            new FakeKind("models", "m"),
            new FakeKind("textures", "a", "b")
        );
        using var log = new CapturedLog();

        var kind = b.ShowKind(1);
        var asset = b.ShowAsset(1);

        CollectionAssert.AreEqual(
            new[]
            {
                "INFO browser: category selected: textures",
                "INFO browser: asset selected: a (category textures)",
                "INFO browser: preview built",
                "INFO browser: asset selected: b (category textures)",
                "INFO browser: preview built",
            },
            Entries(log)
        );
        Assert.AreEqual("a", kind.Preview);
        Assert.AreEqual(0, kind.Asset);
        Assert.AreEqual("b", asset.Preview);
        Assert.AreEqual("", asset.Text);
    }

    [TestMethod]
    public void Each_note_of_a_shown_asset_is_one_warn_after_its_info_on_every_showing()
    {
        var b = OpenWith(
            new Dictionary<string, AssetBrowser.CreatePreview>
            {
                ["models"] = (files, kind, name) =>
                    new Preview { View = name, Notes = name == "a" ? ["line 3 skipped", "line 9 skipped"] : [] },
            },
            new FakeKind("models", "a", "b")
        );
        using var log = new CapturedLog();

        b.ShowAsset(0);
        b.ShowAsset(0);
        b.ShowAsset(1);

        var showing = new[]
        {
            "INFO browser: asset selected: a (category models)",
            "WARN browser: a: line 3 skipped",
            "WARN browser: a: line 9 skipped",
        };
        CollectionAssert.AreEqual(
            showing.Concat(showing).Append("INFO browser: asset selected: b (category models)").ToArray(),
            Entries(log)
        );
    }

    [TestMethod]
    public void Start_without_model_logs_only_the_first_category_and_its_first_asset()
    {
        var b = OpenWith(
            new Dictionary<string, AssetBrowser.CreatePreview> { ["audio"] = Previewing() },
            new FakeKind("audio", "x.wav", "y.wav"),
            new FakeKind("models", "Fente.sod")
        );
        using var log = new CapturedLog();

        var pane = b.Start(null);

        CollectionAssert.AreEqual(
            new[] { "INFO browser: category selected: audio", "INFO browser: asset selected: x.wav (category audio)" },
            Entries(log)
        );
        Assert.AreEqual("x.wav", pane.Preview);
    }

    [TestMethod]
    public void Open_and_start_list_the_first_kind_once()
    {
        var audio = new CountingKind("audio", "x.wav");
        var b = Open(audio, new FakeKind("models", "Fente.sod"));

        b.Start(null);

        Assert.AreEqual(1, audio.Listings);
    }

    [TestMethod]
    public void Open_and_start_with_model_list_the_models_kind_once()
    {
        var models = new CountingKind("models", "Fente.sod");
        var b = Open(new FakeKind("audio", "x.wav"), models);

        b.Start("Fente");

        Assert.AreEqual(1, models.Listings);
    }

    [TestMethod]
    public void Open_and_start_with_model_list_the_models_kind_once_when_it_is_the_first_kind()
    {
        var models = new CountingKind("models", "Fente.sod");
        var b = Open(models, new FakeKind("sounds", "x.wav"));

        b.Start("Fente");

        Assert.AreEqual(1, models.Listings);
    }

    [TestMethod]
    public void Start_with_model_logs_only_the_models_category_and_that_model()
    {
        var b = OpenWith(
            new Dictionary<string, AssetBrowser.CreatePreview> { ["models"] = Previewing() },
            new FakeKind("audio", "x.wav"),
            new FakeKind("models", "Fente.sod", "Other.sod")
        );
        using var log = new CapturedLog();

        var pane = b.Start("other");

        CollectionAssert.AreEqual(
            new[]
            {
                "INFO browser: category selected: models",
                "INFO browser: asset selected: Other.sod (category models)",
            },
            Entries(log)
        );
        Assert.AreEqual("models", b.Kind!.Name);
        Assert.AreEqual(1, pane.Asset);
        Assert.AreEqual("Other.sod", pane.Preview);
    }

    [TestMethod]
    public void Failing_preview_is_one_error_and_the_next_selection_previews_normally()
    {
        var b = OpenWith(
            new Dictionary<string, AssetBrowser.CreatePreview>
            {
                ["models"] = (files, kind, name) =>
                    name == "bad.sod"
                        ? throw new NotSupportedException("bad magic")
                        : new Preview { View = name, Notes = ["a note"] },
            },
            new FakeKind("models", "bad.sod", "good.sod")
        );
        b.Start(null);
        using var log = new CapturedLog();

        var failed = b.ShowAsset(0);
        var next = b.ShowAsset(1);

        CollectionAssert.AreEqual(
            new[]
            {
                "INFO browser: asset selected: bad.sod (category models)",
                "ERROR browser: bad.sod: bad magic",
                "INFO browser: asset selected: good.sod (category models)",
                "WARN browser: good.sod: a note",
            },
            Entries(log)
        );
        Assert.AreEqual("bad.sod: bad magic", failed.Text);
        Assert.IsNull(failed.Preview);
        Assert.AreEqual("good.sod", next.Preview);
    }

    [TestMethod]
    public void Kind_without_preview_is_one_warn_on_each_showing()
    {
        var b = OpenWith(new Dictionary<string, AssetBrowser.CreatePreview>(), new FakeKind("sounds", "a", "b"));
        using var log = new CapturedLog();

        var first = b.ShowAsset(0);
        var again = b.ShowAsset(0);

        Assert.AreEqual(2, Entries(log).Count(e => e == "WARN browser: no preview for sounds"), log.Text);
        Assert.AreEqual("no preview for sounds", first.Text);
        Assert.AreEqual("no preview for sounds", again.Text);
    }

    [TestMethod]
    public void Browser_without_install_is_one_error_and_nothing_else()
    {
        var b = AssetBrowser.Open(null, new[] { new FakeKind("models", "m") });
        using var log = new CapturedLog();

        var pane = b.Start("m");

        var entry = Assert.ContainsSingle(Entries(log));
        Assert.AreEqual($"ERROR browser: {b.Problem}", entry);
        Assert.AreEqual(b.Problem, pane.Text);
    }

    [TestMethod]
    public void Browser_whose_install_cannot_be_opened_is_one_error_and_nothing_else()
    {
        var b = AssetBrowser.Open(temp.MakeDir(), new[] { new FakeKind("models", "m") });
        using var log = new CapturedLog();

        var pane = b.Start("m");

        var entry = Assert.ContainsSingle(Entries(log));
        Assert.AreEqual($"ERROR browser: {b.Problem}", entry);
        Assert.AreEqual(b.Problem, pane.Text);
    }

    [TestMethod]
    public void Kind_whose_listing_fails_is_one_error_when_shown()
    {
        var b = OpenWith(new Dictionary<string, AssetBrowser.CreatePreview>(), new ThrowingKind());
        using var log = new CapturedLog();

        var pane = b.ShowKind(0);

        CollectionAssert.AreEqual(
            new[] { "INFO browser: category selected: broken", "ERROR browser: cannot list broken: disk gone" },
            Entries(log)
        );
        Assert.AreEqual("cannot list broken: disk gone", pane.Text);
        Assert.AreEqual(-1, pane.Asset);
    }

    [TestMethod]
    public void Empty_kind_writes_no_warn_or_error_and_says_so()
    {
        var b = OpenWith(new Dictionary<string, AssetBrowser.CreatePreview>(), new FakeKind("models"));
        using var log = new CapturedLog();

        var pane = b.ShowKind(0);

        CollectionAssert.AreEqual(new[] { "INFO browser: category selected: models" }, Entries(log));
        Assert.AreEqual("no models in this install", pane.Text);
    }

    [TestMethod]
    public void Failure_message_names_asset_and_reason()
    {
        Assert.AreEqual(
            "Wviracod: bad magic",
            AssetBrowser.FailureMessage("Wviracod", new InvalidDataException("bad magic"))
        );
    }
}
