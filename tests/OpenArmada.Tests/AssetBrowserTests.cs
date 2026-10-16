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

    [TestMethod]
    public void Failure_message_names_asset_and_reason()
    {
        Assert.AreEqual("Wviracod: bad magic", AssetBrowser.FailureMessage("Wviracod", new InvalidDataException("bad magic")));
    }
}
