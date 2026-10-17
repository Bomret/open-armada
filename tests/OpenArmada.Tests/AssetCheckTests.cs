using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Assets;
using OpenArmada.Diagnostics;
using OpenArmada.Formats;

namespace OpenArmada.Tests;

[TestClass]
public class AssetCheckTests
{
    // A minimal SOD (no materials, nodes, channels or references), built in code: INV-0001.
    static string Sod(float version)
    {
        var b = new List<byte>(Encoding.ASCII.GetBytes("Storm3D_SW"));
        b.AddRange(BitConverter.GetBytes(version));
        if (version >= 1.7f)
        {
            b.AddRange(new byte[8]);
        }

        return Encoding.Latin1.GetString(b.ToArray());
    }

    sealed class FakeKind(string name, Func<GameFileSystem, IEnumerable<string>> enumerate, Action<string> load)
        : AssetKind
    {
        public override string Name => name;

        public override IEnumerable<string> Enumerate(GameFileSystem f) => enumerate(f);

        public override object Load(GameFileSystem f, string n)
        {
            load(n);

            return n;
        }
    }

    static (int Code, string Output) Run(string? game, params AssetKind[] kinds)
    {
        using var log = new CapturedLog();

        return (AssetCheck.Run(game, kinds), log.Text);
    }

    // One kind that yields a note, an unresolved reference, an unsupported asset, and a failure.
    sealed class MarkerKind : AssetKind
    {
        public override string Name => "mk";

        public override IEnumerable<string> Enumerate(GameFileSystem f) => ["n", "u", "s", "f", "ok"];

        public override object Load(GameFileSystem f, string n) =>
            n switch
            {
                "s" => throw new UnsupportedFormatException("old format"),
                "f" => throw new InvalidDataException("broken"),
                _ => n,
            };

        public override IReadOnlyList<string> Check(GameFileSystem f, string n) =>
            n == "n" ? ["odd"] : base.Check(f, n);

        public override IReadOnlyList<string> Unresolved(GameFileSystem f, string n) => n == "u" ? ["gone"] : [];
    }

    // Every marker of the report starts its text and has one level (INV-0005).
    [TestMethod]
    public void EveryMarkerStartsTheTextAtItsLevel()
    {
        var game = Install(
            ("startrek.zix", "000001\n0 d:\\x x 'Disc\n----\nDIR: SOD\n----\n1 gone.trn\n"),
            ("help/h.htm", ""),
            ("giddi/j.std", ""),
            ("mystery/new.thing", "")
        );
        using var log = new CapturedLog();
        AssetCheck.Run(game, [new MarkerKind()]);
        var levels = log.Lines.Select(l => l.Split(' ', 4)).Select(p => (Level: p[1], Text: p[3])).ToList();
        (string Marker, string Level)[] expected =
        [
            ("MISSING ", "WARN"),
            ("NOTE ", "WARN"),
            ("UNRESOLVED ", "WARN"),
            ("UNSUPPORTED ", "WARN"),
            ("FAIL ", "ERROR"),
            ("UNCLASSIFIED ", "ERROR"),
            ("NOT LOADED ", "INFO"),
            ("OUT OF SCOPE ", "INFO"),
            ("mk: ", "INFO"),
            ("3 loaded, ", "INFO"),
            ("1 unresolved references", "INFO"),
            ("files: ", "INFO"),
        ];
        foreach (var (marker, level) in expected)
        {
            var hits = levels.Where(l => l.Text.StartsWith(marker, StringComparison.Ordinal)).ToList();
            Assert.IsTrue(hits.Count > 0, $"no line starts with {marker}: {log.Text}");
            Assert.IsTrue(hits.All(h => h.Level == level), $"{marker} is not {level}: {log.Text}");
        }
    }

    [TestMethod]
    public void MissingInstallAndUnopenableInstallAreErrorsInAreaCheck()
    {
        using var log = new CapturedLog();
        AssetCheck.Run(null, [new ModelKind()]);
        AssetCheck.Run(temp.MakeDir(), [new ModelKind()]);
        Assert.AreEqual(2, log.Lines.Count(l => l.Contains(" ERROR check: ")), log.Text);
        Assert.AreEqual(2, log.Lines.Count, log.Text);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ExitCodeIsOneExactlyWhenAnErrorEntryInAreaCheckWasWritten(bool broken)
    {
        var game = Install(("SOD/a.sod", broken ? "garbage!!!!" : Sod(1.8f)));
        using var log = new CapturedLog();
        var code = AssetCheck.Run(game, [new ModelKind()]);
        Assert.AreEqual(log.Lines.Any(l => l.Contains(" ERROR check: ")) ? 1 : 0, code);
        Assert.AreEqual(broken ? 1 : 0, code);
    }

    [TestMethod]
    public void ThrowingSinkLeavesTheExitCodeUnchanged()
    {
        var game = Install(("SOD/bad.sod", "garbage!!!!"));
        int plain;
        using (new CapturedLog())
        {
            plain = AssetCheck.Run(game, [new ModelKind()]);
        }

        using var _ = Log.Redirect(_ => throw new InvalidOperationException("sink broke"));
        Assert.AreEqual(plain, AssetCheck.Run(game, [new ModelKind()]));
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
        var kind = new FakeKind(
            "fake",
            _ => new[] { "a", "b", "c" },
            n =>
            {
                if (n == "b")
                    throw new StackOverflowGuardException();
                loaded.Add(n);
            }
        );
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

    // A folder the check cannot list fails the check with its name and the reason; the check still ends.
    // Windows has no POSIX file modes; MSTest reports the test as skipped there.
    [TestMethod, System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    public void UnreadableFolderFailsTheClassificationWithItsName()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("needs POSIX file modes");
        }

        var game = Install(("SOD/a.sod", Sod(1.8f)), ("locked/x.txt", ""));
        var locked = Path.Combine(game, "locked");
        File.SetUnixFileMode(locked, UnixFileMode.None);
        try
        {
            // Root lists a folder whatever its mode (container CI), so the fault cannot be staged there.
            try
            {
                Directory.GetFileSystemEntries(locked);
                Assert.Inconclusive("the folder stays readable (running as root)");
            }
            catch (UnauthorizedAccessException) { }

            var (code, output) = Run(game, new ModelKind());
            Assert.AreEqual((1, true), (code, output.Contains("locked")));
        }
        finally
        {
            File.SetUnixFileMode(locked, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
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
