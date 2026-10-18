using System.Linq;
using System.Numerics;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Assets;
using OpenArmada.Sandbox;

namespace OpenArmada.Tests;

[TestClass]
public class MapSummaryTests
{
    readonly TempInstall temp = new();

    [TestCleanup]
    public void CleanUp() => temp.CleanUp();

    const string Physics = "physicsFile = \"p.odf\"\r\n";
    const string Mdf2 = "StartLocations = 2\r\nStart1 = 0 0\r\nStart2 = 117 117\r\n";

    // The definitions every test map can name: a warship, a construction ship, a station on a combat hull,
    // and a nebula with no physics.
    static readonly (string Path, string Content)[] Definitions =
    [
        ("addon/p.odf", "speed = 5"),
        ("addon/battle.odf", "hull = 1"),
        ("addon/fbattle.odf", "#include \"battle.odf\"\r\n" + Physics),
        ("addon/fconst.odf", Physics),
        ("addon/fbase.odf", "#include \"battle.odf\"\r\n" + Physics + "is_starbase = 1"),
        ("addon/mnebula.odf", "x = 1"),
    ];

    static string S(byte[] bytes) => Encoding.Latin1.GetString(bytes);

    MapSummary Summary((string Class, int Team)[] objects, string? mdf = Mdf2, params (string, string)[] extra)
    {
        var b = new BznBuilder();
        foreach (var (cls, team) in objects)
        {
            b.Objects.Add(
                new()
                {
                    Class = cls,
                    Team = team,
                    Position = new Vector3(100, 0, 100),
                }
            );
        }

        var files = Definitions.Concat(extra).Append(("bzn/m.bzn", S(b.Build()))).ToList();
        if (mdf != null)
        {
            files.Add(("bzn/m.mdf", mdf));
        }

        var fs = new GameFileSystem(temp.MakeInstall(files.ToArray()));

        return MapSummary.Of(fs, (MapAsset)new MapKind().Load(fs, "bzn/m.bzn"));
    }

    static string Line(MapSummary s, string field) =>
        s.Lines.Single(l => l.StartsWith(field + ": "))[(field.Length + 2)..];

    [TestMethod]
    public void WarshipsOnTwoTeamsMakeCombatPossibleAtStart()
    {
        var s = Summary([("fbattle", 1), ("fbattle", 2)]);
        Assert.AreEqual("t1, t2", Line(s, "Teams with a warship"));
        Assert.AreEqual("yes", Line(s, "Combat at start"));
    }

    [TestMethod]
    public void WarshipsOnOneTeamMakeNoCombatAtStart()
    {
        var s = Summary([("fbattle", 1), ("fbattle", 1), ("fconst", 2)]);
        Assert.AreEqual("t1", Line(s, "Teams with a warship"));
        Assert.AreEqual("no", Line(s, "Combat at start"));
    }

    [TestMethod]
    public void ATeamZeroWarshipNeverMakesCombatPossible()
    {
        var s = Summary([("fbattle", 0), ("fbattle", 1)]);
        Assert.AreEqual("t1", Line(s, "Teams with a warship"));
        Assert.AreEqual("no", Line(s, "Combat at start"));
    }

    [TestMethod]
    public void ATeamHoldingOnlyAnUnresolvedClassMakesCombatUnknown()
    {
        var s = Summary([("fbattle", 1), ("gone", 2)]);
        Assert.AreEqual("unknown", Line(s, "Combat at start"));
        Assert.AreEqual("gone", Line(s, "Unresolved classes"));
        Assert.IsTrue(s.HasUnresolved);
    }

    [TestMethod]
    public void AStationOnACombatHullAndAConstructionShipAreNoWarships()
    {
        var s = Summary([("fbase", 1), ("fconst", 2), ("mnebula", 3)]);
        Assert.AreEqual("none", Line(s, "Teams with a warship"));
    }

    [TestMethod]
    [DataRow("Battle.ODF")]
    [DataRow("odf/battle.odf")]
    [DataRow("odf\\battle.odf")]
    public void AHullIncludeMatchesInAnyCaseAndWithAFolder(string include)
    {
        var s = Summary(
            [("kship", 1)],
            Mdf2,
            ("addon/kship.odf", $"#include \"{include}\"\r\n" + Physics),
            ("odf/battle.odf", "hull = 1")
        );
        Assert.AreEqual("t1", Line(s, "Teams with a warship"));
    }

    [TestMethod]
    [DataRow("#include \"gone.odf\"\r\n" + Physics)]
    [DataRow(Physics + "is_starbase = yes")]
    [DataRow(Physics + "unitName = \"two words\" more")]
    public void AClassWithAChainFaultOrAnUnclassifiableDefinitionIsUnresolved(string odf)
    {
        var s = Summary([("bad", 1)], Mdf2, ("addon/bad.odf", odf));
        Assert.AreEqual("bad", Line(s, "Unresolved classes"));
        Assert.IsTrue(s.HasUnresolved);
    }

    [TestMethod]
    public void AMapWithoutUnresolvedClassesHasNoUnresolvedLine()
    {
        var s = Summary([("fbattle", 1)]);
        Assert.IsFalse(s.Lines.Any(l => l.StartsWith("Unresolved classes")));
        Assert.IsFalse(s.HasUnresolved);
    }

    [TestMethod]
    public void AMapWithAnMdfIsMultiplayerWithItsStartCount()
    {
        var s = Summary([("fbattle", 1)]);
        Assert.AreEqual("multiplayer", Line(s, "Group"));
        Assert.AreEqual("2", Line(s, "Start positions"));
    }

    [TestMethod]
    public void AMapWithoutAnMdfIsACampaignOrOtherMapWithNoStartPositions()
    {
        var s = Summary([("fbattle", 1)], mdf: null);
        Assert.AreEqual("campaign and other", Line(s, "Group"));
        Assert.AreEqual("none", Line(s, "Start positions"));
    }

    static string Starts(int n) =>
        $"StartLocations = {n}\r\n" + string.Concat(Enumerable.Range(1, n).Select(i => $"Start{i} = {i} {i}\r\n"));

    [TestMethod]
    public void TeamEightObjectsAreMonsterUnitsBelowEightStartPositions()
    {
        (string, int)[] objects = [("fbattle", 0), ("fconst", 8), ("fconst", 8), ("mnebula", 0)];
        Assert.AreEqual("t0: fbattle; t8: fconst ×2", Line(Summary(objects, Starts(7)), "Neutral and monster units"));
        Assert.AreEqual("t0: fbattle; t8: fconst ×2", Line(Summary(objects, mdf: null), "Neutral and monster units"));
        Assert.AreEqual("t0: fbattle", Line(Summary(objects, Starts(8)), "Neutral and monster units"));
        Assert.AreEqual("none", Line(Summary([("mnebula", 0)]), "Neutral and monster units"));
    }

    [TestMethod]
    public void ObjectsPerTeamListEachPresentTeamWithItsCountAndClasses()
    {
        var s = Summary([("mnebula", 0), ("fconst", 2), ("fbattle", 2), ("fbattle", 2)]);
        var at = s.Lines.ToList().IndexOf("Objects per team:");
        CollectionAssert.AreEqual(
            new[] { "  t0 (1): mnebula", "  t2 (3): fbattle ×2, fconst" },
            s.Lines.Skip(at + 1).ToArray()
        );
    }

    [TestMethod]
    public void ClassesThatDifferOnlyInCaseAreOneClassInCaseInsensitiveOrder()
    {
        var s = Summary(
            [("mnebula", 0), ("Mbaku", 0), ("mbaku", 0), ("mafield", 0)],
            Mdf2,
            ("addon/mbaku.odf", "x = 1"),
            ("addon/mafield.odf", "x = 1")
        );
        Assert.AreEqual("  t0 (4): mafield, Mbaku ×2, mnebula", s.Lines[^1]);
    }

    [TestMethod]
    public void TeamEightCountsTowardCombatLikeAnyTeam()
    {
        var s = Summary([("fbattle", 1), ("fbattle", 8)]);
        Assert.AreEqual("yes", Line(s, "Combat at start"));
    }
}
