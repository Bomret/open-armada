using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Assets;

namespace OpenArmada.Tests;

[TestClass]
public class AssetCheckTests
{
    // A minimal SOD (no materials, nodes, channels or references), built in code: INV-0001.
    static string Sod(float version)
    {
        var b = new List<byte>(Encoding.ASCII.GetBytes("Storm3D_SW"));
        b.AddRange(BitConverter.GetBytes(version));
        if (version >= 1.7f) b.AddRange(new byte[8]);
        return Encoding.Latin1.GetString(b.ToArray());
    }

    sealed class FakeKind(string name, Func<GameFileSystem, IEnumerable<string>> enumerate, Action<string> load) : AssetKind
    {
        public override string Name => name;
        public override IEnumerable<string> Enumerate(GameFileSystem f) => enumerate(f);
        public override object Load(GameFileSystem f, string n) { load(n); return n; }
    }

    static (int Code, string Output) Run(string? game, params AssetKind[] kinds)
    {
        var w = new StringWriter();
        return (AssetCheck.Run(game, kinds, w), w.ToString());
    }

    // Per-test temp folders, deleted by CleanUp; one instance per test, so nothing is shared.
    readonly TempInstall temp = new();

    string Install(params (string, string)[] files) => temp.MakeInstall(files);

    [TestMethod]
    public void ValidModelsPassAndExitZero()
    {
        var (code, output) = Run(Install(("SOD/a.sod", Sod(1.8f))), new ModelKind());
        Assert.AreEqual(0, code);
        StringAssert.Contains(output, "1 loaded");
    }

    [TestMethod]
    public void CorruptModelIsReportedByNameAndReasonAndExitsNonZero()
    {
        var (code, output) = Run(Install(("SOD/bad.sod", "garbage!!!!"), ("SOD/ok.sod", Sod(1.8f))), new ModelKind());
        Assert.AreEqual(1, code);
        StringAssert.Contains(output, "bad.sod");
        StringAssert.Contains(output, "not a SOD file");
        StringAssert.Contains(output, "1 failed");
        StringAssert.Contains(output, "1 loaded");
    }

    [TestMethod]
    public void UnsupportedModelIsListedSeparatelyAndDoesNotFailTheCheck()
    {
        var (code, output) = Run(Install(("SOD/Wviracod.SOD", Sod(1.0f))), new ModelKind());
        Assert.AreEqual(0, code);
        StringAssert.Contains(output, "unsupported");
        StringAssert.Contains(output, "Wviracod.SOD");
        StringAssert.Contains(output, "0 failed");
    }

    // A software-renderer copy that shares its name with a main model is a different file: it is listed
    // under its folder and checked too.
    [TestMethod]
    public void SoftwareModelWithTheNameOfAMainModelIsCheckedToo()
    {
        var (_, output) = Run(Install(("SOD/a.sod", Sod(1.8f)), ("SOD/Software/a.sod", Sod(1.8f))), new ModelKind());
        StringAssert.Contains(output, "models: 2 loaded, 0 unsupported, 0 failed");
    }

    // A model that only the override layer holds is a model too; the game loads it by its bare name.
    [TestMethod]
    public void ModelOnlyTheOverrideLayerHoldsIsChecked()
    {
        var (_, output) = Run(Install(("addon/o.sod", Sod(1.8f))), new ModelKind());
        StringAssert.Contains(output, "models: 1 loaded, 0 unsupported, 0 failed");
    }

    [TestMethod]
    [DataRow(0f)]
    [DataRow(-1f)]
    [DataRow(1.05f)]
    [DataRow(1.3f)]
    [DataRow(2.0f)]
    [DataRow(float.PositiveInfinity)]
    [DataRow(float.NaN)]
    public void ModelWithUnrecognisedVersionIsCorruptAndFailsTheCheck(float version)
    {
        var (code, output) = Run(Install(("SOD/v.sod", Sod(version))), new ModelKind());
        Assert.AreEqual(1, code);
        StringAssert.Contains(output, "FAIL models/v.sod: unrecognised SOD version");
        StringAssert.Contains(output, "1 failed");
    }

    [TestCleanup]
    public void CleanUp() => temp.CleanUp();

    [TestMethod]
    public void ThrowingReaderFailsThatAssetAndTheRestStillLoad()
    {
        var loaded = new List<string>();
        var kind = new FakeKind("fake", _ => new[] { "a", "b", "c" }, n =>
        {
            if (n == "b") throw new StackOverflowGuardException();
            loaded.Add(n);
        });
        var (code, output) = Run(Install(), kind);
        Assert.AreEqual(1, code);
        CollectionAssert.AreEqual(new[] { "a", "c" }, loaded);
        StringAssert.Contains(output, "FAIL fake/b: reader blew up");
    }

    sealed class StackOverflowGuardException() : Exception("reader blew up");

    [TestMethod]
    public void KindThatCannotEnumerateIsAFailureNotACrash()
    {
        var kind = new FakeKind("fake", _ => throw new IOException("disk gone"), _ => { });
        var (code, output) = Run(Install(), kind);
        Assert.AreEqual(1, code);
        StringAssert.Contains(output, "disk gone");
    }

    [TestMethod]
    public void EmptyKindIsReportedAsEmpty()
    {
        var (code, output) = Run(Install(), new ModelKind());
        Assert.AreEqual(0, code);
        StringAssert.Contains(output, "models: empty");
    }

    [TestMethod]
    public void MissingGamePathNamesWhatIsMissingAndExitsNonZero()
    {
        var (code, output) = Run(null, new ModelKind());
        Assert.AreEqual(1, code);
        StringAssert.Contains(output, "--game");
        StringAssert.Contains(output, "ARMADA_DIR");
    }

    [TestMethod]
    public void FolderWithoutInstallNamesTheFolderAndExitsNonZero()
    {
        var dir = temp.MakeDir();
        var (code, output) = Run(dir, new ModelKind());
        Assert.AreEqual(1, code);
        StringAssert.Contains(output, dir);
        StringAssert.Contains(output, "SOD");
    }

    [TestMethod]
    public void DiscoverFindsTheModelKind()
    {
        Assert.IsTrue(AssetKind.Discover().Any(k => k is ModelKind));
    }
}
