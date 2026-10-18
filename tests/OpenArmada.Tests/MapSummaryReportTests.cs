using System;
using System.Linq;
using System.Numerics;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Assets;
using OpenArmada.Sandbox;

namespace OpenArmada.Tests;

// The --maps run's report, from installs built in code (INV-0001).
[TestClass]
public class MapSummaryReportTests
{
    readonly TempInstall temp = new();

    [TestCleanup]
    public void CleanUp() => temp.CleanUp();

    static string Bzn(string cls)
    {
        var b = new BznBuilder();
        b.Objects.Add(
            new()
            {
                Class = cls,
                Team = 1,
                Position = new Vector3(100, 0, 100),
            }
        );

        return Encoding.Latin1.GetString(b.Build());
    }

    // Three maps in name order: "a" summarizes cleanly, "b" names a class with no definition, "c" cannot be read.
    string Install() =>
        temp.MakeInstall(
            ("addon/p.odf", "speed = 5"),
            ("addon/fconst.odf", "physicsFile = \"p.odf\""),
            ("bzn/c.bzn", "not a map"),
            ("bzn/b.bzn", Bzn("gone")),
            ("bzn/a.bzn", Bzn("fconst"))
        );

    // "<time> <LEVEL> <area>: <text>" split into its level, area, and text.
    static (string Level, string Area, string Text) Entry(string line)
    {
        var parts = line.Split(' ', 4);

        return (parts[1], parts[2].TrimEnd(':'), parts[3]);
    }

    [TestMethod]
    public void EveryMapGetsOneEntryInNameOrderAndAnUnreadableMapIsOneErrorThatStopsNothing()
    {
        using var log = new CapturedLog();
        var code = MapSummaryReport.Run(Install());
        var entries = log.Lines.Select(Entry).ToList();

        Assert.AreEqual(0, code);
        Assert.IsTrue(entries.All(e => e.Area == "maps"), log.Text);
        CollectionAssert.AreEqual(
            new[] { "a", "b", "c" },
            entries.Select(e => e.Text.Split(':')[0]).Distinct().ToArray(),
            log.Text
        );
        Assert.IsTrue(entries.Where(e => e.Text.StartsWith("a: ")).All(e => e.Level == "INFO"), log.Text);
        Assert.IsTrue(entries.Where(e => e.Text.StartsWith("b: ")).All(e => e.Level == "WARN"), log.Text);
        var c = entries.Where(e => e.Text.StartsWith("c: ")).ToList();
        Assert.AreEqual(1, c.Count, log.Text);
        Assert.AreEqual("ERROR", c[0].Level);
    }

    // The same level per cause as the asset check (INV-0005): a format the reader does not read is one WARN.
    [TestMethod]
    public void AnUnsupportedMapIsOneWarnNamingTheMapAndTheReason()
    {
        var save = Encoding.Latin1.GetString(new BznBuilder { MissionSave = false }.Build());
        var root = temp.MakeInstall(("bzn/save.bzn", save));

        using var log = new CapturedLog();
        var code = MapSummaryReport.Run(root);

        Assert.AreEqual(0, code);
        var entry = Entry(log.Lines.Single());
        Assert.AreEqual(("WARN", "maps", "save: BZN save game (missionSave false) is not read"), entry);
    }

    [TestMethod]
    public void AMapEntryCarriesTheLinesThePreviewShows()
    {
        var root = Install();
        var fs = new GameFileSystem(root);
        var summary = MapSummary.Of(fs, (MapAsset)new MapKind().Load(fs, "bzn/a.bzn"));

        using var log = new CapturedLog();
        MapSummaryReport.Run(root);

        CollectionAssert.AreEqual(
            summary.Lines.ToArray(),
            log.Lines.Select(Entry).Where(e => e.Text.StartsWith("a: ")).Select(e => e.Text[3..]).ToArray()
        );
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("/no/such/install")]
    public void NoInstallOrOneThatCannotOpenIsTheAssetChecksErrorAndExitOne(string? game)
    {
        string expected;
        using (var check = new CapturedLog())
        {
            AssetCheck.Run(game, Array.Empty<AssetKind>());
            expected = Entry(check.Lines.Single()).Text;
        }

        using var log = new CapturedLog();
        var code = MapSummaryReport.Run(game);

        Assert.AreEqual(1, code);
        var entry = Entry(log.Lines.Single());
        Assert.AreEqual(("ERROR", "maps", expected), entry);
    }
}
