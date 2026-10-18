using System.IO;
using System.Linq;
using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Assets;

namespace OpenArmada.Tests;

// Reading a layout's JSONC text into the map asset the sandbox plans; fixtures are text built in code (INV-0001).
[TestClass]
public class LayoutTests
{
    readonly TempInstall temp = new();

    [TestCleanup]
    public void CleanUp() => temp.CleanUp();

    const string Minimal = """
        {
          "bounds": { "minX": 0, "maxX": 1000, "minZ": -500, "maxZ": 500 },
          "objects": [
            { "class": "fbattle", "team": 1, "position": [10, 20, 30] },
            { "class": "kbattle", "team": 2, "position": [-10, 0, 5.5] }
          ]
        }
        """;

    [TestMethod]
    public void ALayoutReadsIntoAMapAssetWithItsObjectsAndBounds()
    {
        var read = Layout.Read("layouts/duel.jsonc", Minimal);
        Assert.IsNull(read.Problem);
        var map = read.Asset!.Map;
        Assert.AreEqual(2, map.Objects.Count);
        Assert.AreEqual("fbattle", map.Objects[0].Class);
        Assert.AreEqual(1, map.Objects[0].Team);
        Assert.AreEqual(new Vector3(10, 20, 30), map.Objects[0].Position);
        Assert.AreEqual("kbattle", map.Objects[1].Class);
        Assert.AreEqual(2, map.Objects[1].Team);
        Assert.AreEqual(new Vector3(-10, 0, 5.5f), map.Objects[1].Position);
        Assert.AreEqual(0, map.Bounds.MinX);
        Assert.AreEqual(1000, map.Bounds.MaxX);
        Assert.AreEqual(-500, map.Bounds.MinZ);
        Assert.AreEqual(500, map.Bounds.MaxZ);
    }

    [TestMethod]
    public void CommentsAndTrailingCommasAreAccepted()
    {
        var read = Layout.Read(
            "a.jsonc",
            """
            // two ships
            {
              /* the playable area */
              "bounds": { "minX": 0, "maxX": 10, "minZ": 0, "maxZ": 10, },
              "objects": [ { "class": "fbattle", "team": 1, "position": [1, 2, 3], }, ],
            }
            """
        );
        Assert.IsNull(read.Problem);
        Assert.AreEqual(1, read.Asset!.Map.Objects.Count);
    }

    [TestMethod]
    [DataRow("", "line 1")]
    [DataRow("{\n  \"bounds\": {\n    \"minX\": 0,,\n", "line 3")]
    [DataRow("[]", "the root: must be an object")]
    [DataRow("""{ "objects": [] }""", "the root: missing required key 'bounds'")]
    [DataRow(
        """{ "bounds": { "minX": 0, "maxX": 1, "minZ": 0, "maxZ": 1 } }""",
        "the root: missing required key 'objects'"
    )]
    [DataRow(
        """{ "bounds": { "minX": 0, "maxX": 1, "minZ": 0, "maxZ": 1 }, "objects": [], "extra": 1 }""",
        "extra: unknown key"
    )]
    [DataRow(
        """{ "bounds": { "minX": 0, "maxX": 1, "minZ": 0 }, "objects": [] }""",
        "bounds: missing required key 'maxZ'"
    )]
    [DataRow(
        """{ "bounds": { "minX": 0, "maxX": 1, "minZ": 0, "maxZ": null }, "objects": [] }""",
        "bounds.maxZ: must be a number, not null"
    )]
    [DataRow(
        """{ "bounds": { "minX": 0, "maxX": "1", "minZ": 0, "maxZ": 1 }, "objects": [] }""",
        "bounds.maxX: must be a number"
    )]
    [DataRow(
        """{ "bounds": { "minX": 1, "maxX": 1, "minZ": 0, "maxZ": 1 }, "objects": [] }""",
        "bounds.minX: 1 is not below maxX 1"
    )]
    [DataRow(
        """{ "bounds": { "minX": 0, "maxX": 1e999, "minZ": 0, "maxZ": 1 }, "objects": [] }""",
        "bounds.maxX: 1e999 is not a finite number"
    )]
    [DataRow(
        """{ "bounds": { "minX": 0, "maxX": 1, "minZ": 0, "maxZ": 1 }, "objects": {} }""",
        "objects: must be an array"
    )]
    [DataRow(
        """{ "bounds": { "minX": 0, "maxX": 1, "minZ": 0, "maxZ": 1 }, "objects": [ 1 ] }""",
        "objects[0]: must be an object"
    )]
    public void ALayoutThatCannotBeReadIsOneProblemNamingTheFileAndTheLineOrPath(string text, string where)
    {
        var read = Layout.Read("layouts/bad.jsonc", text);
        Assert.IsNull(read.Asset);
        StringAssert.StartsWith(read.Problem, "layout 'layouts/bad.jsonc': ");
        StringAssert.Contains(read.Problem, where);
    }

    [TestMethod]
    [DataRow("""{ "team": 1, "position": [0, 0, 0] }""", "objects[1]: missing required key 'class'")]
    [DataRow("""{ "class": "", "team": 1, "position": [0, 0, 0] }""", "objects[1].class: must not be empty")]
    [DataRow("""{ "class": null, "team": 1, "position": [0, 0, 0] }""", "objects[1].class: must be a string, not null")]
    [DataRow("""{ "class": "x", "position": [0, 0, 0] }""", "objects[1]: missing required key 'team'")]
    [DataRow("""{ "class": "x", "team": 9, "position": [0, 0, 0] }""", "objects[1].team: team 9 is outside 0..8")]
    [DataRow("""{ "class": "x", "team": -1, "position": [0, 0, 0] }""", "objects[1].team: team -1 is outside 0..8")]
    [DataRow("""{ "class": "x", "team": 1.5, "position": [0, 0, 0] }""", "objects[1].team: must be a whole number")]
    [DataRow("""{ "class": "x", "team": 1 }""", "objects[1]: missing required key 'position'")]
    [DataRow(
        """{ "class": "x", "team": 1, "position": [0, 0] }""",
        "objects[1].position: must be an array of 3 numbers"
    )]
    [DataRow(
        """{ "class": "x", "team": 1, "position": { "x": 0 } }""",
        "objects[1].position: must be an array of 3 numbers"
    )]
    [DataRow(
        """{ "class": "x", "team": 1, "position": [0, null, 0] }""",
        "objects[1].position[1]: must be a number, not null"
    )]
    [DataRow(
        """{ "class": "x", "team": 1, "position": [0, 0, -1e999] }""",
        "objects[1].position[2]: -1e999 is not a finite number"
    )]
    [DataRow("""{ "class": "x", "team": 1, "position": [0, 0, 0], "hp": 5 }""", "objects[1].hp: unknown key")]
    public void ABadObjectIsAProblemNamingItsJsonPath(string obj, string where)
    {
        var text =
            """{ "bounds": { "minX": 0, "maxX": 1, "minZ": 0, "maxZ": 1 }, "objects": [ { "class": "a", "team": 0, "position": [0, 0, 0] }, """
            + obj
            + " ] }";
        var read = Layout.Read("l.jsonc", text);
        Assert.IsNull(read.Asset);
        Assert.AreEqual("layout 'l.jsonc': " + where, read.Problem![..("layout 'l.jsonc': " + where).Length]);
    }

    [TestMethod]
    public void TheMapCarriesNineTeamRecordsOfTheTeamsOwnSlot()
    {
        var teams = Layout.Read("a.jsonc", Minimal).Asset!.Map.Teams;
        Assert.AreEqual(9, teams.Count);
        Assert.IsTrue(teams.All(t => t.Color == 0));
    }

    [TestMethod]
    public void ReadingTheSameLayoutTwiceGivesEqualSeedInputs()
    {
        var a = Layout.Read("layouts/duel.jsonc", Minimal).Asset!.Map;
        var b = Layout.Read("other/folder/duel.jsonc", Minimal).Asset!.Map;
        Assert.AreEqual("duel.jsonc", a.Description);
        Assert.AreEqual(a.Description, b.Description);
        CollectionAssert.AreEqual(
            a.Objects.Select(o => (o.Handle, o.Id)).ToList(),
            b.Objects.Select(o => (o.Handle, o.Id)).ToList()
        );
    }

    [TestMethod]
    public void TwoObjectsWithIdenticalFieldsAreTwoObjectsWithTheirOwnHandleAndId()
    {
        var text = """
            { "bounds": { "minX": 0, "maxX": 1, "minZ": 0, "maxZ": 1 }, "objects": [
              { "class": "fbattle", "team": 1, "position": [0, 0, 0] },
              { "class": "fbattle", "team": 1, "position": [0, 0, 0] } ] }
            """;
        var objects = Layout.Read("a.jsonc", text).Asset!.Map.Objects;
        Assert.AreEqual(2, objects.Count);
        Assert.AreNotEqual(objects[0].Handle, objects[1].Handle);
        Assert.AreNotEqual(objects[0].Id, objects[1].Id);
    }

    [TestMethod]
    public void AMissingFileIsAProblemNamingIt()
    {
        var path = Path.Combine(temp.MakeDir(), "none.jsonc");
        var read = Layout.Load(path);
        Assert.IsNull(read.Asset);
        StringAssert.StartsWith(read.Problem, $"layout '{path}': cannot be read: ");
    }

    [TestMethod]
    public void ADirectoryIsAProblemNamingIt()
    {
        var dir = temp.MakeDir();
        var read = Layout.Load(dir);
        Assert.IsNull(read.Asset);
        StringAssert.StartsWith(read.Problem, $"layout '{dir}': cannot be read: ");
    }

    [TestMethod]
    public void AFileIsReadAndNamedByItsPath()
    {
        var path = Path.Combine(temp.MakeDir(), "duel.jsonc");
        File.WriteAllText(path, Minimal);
        var read = Layout.Load(path);
        Assert.IsNull(read.Problem);
        Assert.AreEqual(path, read.Asset!.Name);
        Assert.AreEqual("duel.jsonc", read.Asset.Map.Description);
    }

    [TestMethod]
    public void ARelativeLayoutPathResolvesAgainstTheStartDirectory()
    {
        var start = Path.Combine(temp.MakeDir(), "play");
        Assert.AreEqual(
            Path.Combine(Path.GetDirectoryName(start)!, "layouts", "a.jsonc"),
            Layout.Resolve(Path.Combine("..", "layouts", "a.jsonc"), start)
        );
    }

    [TestMethod]
    [DataRow("/home/me/play", "/home/me/play")]
    [DataRow(null, "/srv/project")]
    [DataRow("play", "/srv/project")]
    public void TheStartDirectoryIsAnAbsolutePwdElseTheCurrentDirectory(string? pwd, string start)
    {
        Assert.AreEqual(start, Layout.Start(pwd, "/srv/project"));
    }

    [TestMethod]
    public void AnAbsoluteLayoutPathStaysUnchanged()
    {
        var path = Path.Combine(temp.MakeDir(), "a.jsonc");
        Assert.AreEqual(path, Layout.Resolve(path, Path.Combine(temp.MakeDir(), "play")));
    }

    [TestMethod]
    public void TheCheckedInExampleReadsAsTwoHostileShipsAndGodotSkipsItsFolder()
    {
        var dir = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "OpenArmada.csproj")))
            dir = dir.Parent;
        Assert.IsNotNull(dir, "OpenArmada.csproj not found above the test binaries");
        var layouts = Path.Combine(dir.FullName, "layouts");
        Assert.IsTrue(File.Exists(Path.Combine(layouts, ".gdignore")));
        var read = Layout.Load(Path.Combine(layouts, "two-ships.jsonc"));
        Assert.IsNull(read.Problem);
        var objects = read.Asset!.Map.Objects;
        Assert.AreEqual(2, objects.Count);
        Assert.IsTrue(OpenArmada.Sandbox.CombatRules.Hostile(objects[0].Team, objects[1].Team));
    }

    static string WithObject(string obj, string top = "") =>
        """{ "bounds": { "minX": 0, "maxX": 1, "minZ": 0, "maxZ": 1 }, """ + top + """ "objects": [ """ + obj + " ] }";

    [TestMethod]
    public void LeftOutOptionalFieldsTakeTheirDocumentedValues()
    {
        var map = Layout
            .Read("a.jsonc", WithObject("""{ "class": "fbattle", "team": 1, "position": [0, 0, 0] }"""))
            .Asset!.Map;
        var o = map.Objects.Single();
        Assert.AreEqual((Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ), (o.Right, o.Up, o.Front));
        Assert.AreEqual("fbattle", o.Label);
        Assert.AreEqual("", map.BackgroundArt);
        Assert.IsTrue(map.Teams.All(t => t.Color == 0));
        Assert.AreEqual(new Vector3(0.35f, 0.35f, 0.4f), map.Ambient);
    }

    [TestMethod]
    public void GivenOptionalFieldsReachTheMap()
    {
        var map = Layout
            .Read(
                "a.jsonc",
                WithObject(
                    """{ "class": "fbattle", "team": 1, "position": [0, 0, 0], "label": "flagship", "right": [0, 0, -1], "up": [0, 1, 0], "front": [1, 0, 0] }""",
                    """ "background": "bspace1.sod", "teamColors": { "1": 4, "2": 6 }, "ambient": [0.1, 0.2, 0.3], """
                )
            )
            .Asset!.Map;
        var o = map.Objects.Single();
        Assert.AreEqual("flagship", o.Label);
        Assert.AreEqual((new Vector3(0, 0, -1), Vector3.UnitY, Vector3.UnitX), (o.Right, o.Up, o.Front));
        Assert.AreEqual("bspace1.sod", map.BackgroundArt);
        Assert.AreEqual(4, map.Teams[1].Color);
        Assert.AreEqual(6, map.Teams[2].Color);
        Assert.AreEqual(0, map.Teams[3].Color);
        Assert.AreEqual(new Vector3(0.1f, 0.2f, 0.3f), map.Ambient);
    }

    [TestMethod]
    public void ZeroAxesReachTheMapUnchanged()
    {
        var o = Layout
            .Read(
                "a.jsonc",
                WithObject(
                    """{ "class": "x", "team": 1, "position": [0, 0, 0], "right": [0, 0, 0], "up": [0, 0, 0], "front": [0, 0, 0] }"""
                )
            )
            .Asset!.Map.Objects.Single();
        Assert.AreEqual(Vector3.Zero, o.Right);
    }

    [TestMethod]
    [DataRow(
        """{ "class": "x", "team": 1, "position": [0, 0, 0], "right": [1, 0, 0] }""",
        "objects[0]: gives right but not up, front; give all three axes or none"
    )]
    [DataRow(
        """{ "class": "x", "team": 1, "position": [0, 0, 0], "up": [0, 1, 0], "front": [0, 0, 1] }""",
        "objects[0]: gives up, front but not right; give all three axes or none"
    )]
    [DataRow(
        """{ "class": "x", "team": 1, "position": [0, 0, 0], "right": [1, 0, 0], "up": [0, 1, 0], "front": [0, 0, null] }""",
        "objects[0].front[2]: must be a number, not null"
    )]
    [DataRow(
        """{ "class": "x", "team": 1, "position": [0, 0, 0], "right": [1, 0, 0], "up": [0, 1e999, 0], "front": [0, 0, 1] }""",
        "objects[0].up[1]: 1e999 is not a finite number"
    )]
    [DataRow(
        """{ "class": "x", "team": 1, "position": [0, 0, 0], "label": 5 }""",
        "objects[0].label: must be a string, not 5"
    )]
    [DataRow(
        """{ "class": "x", "team": 1, "position": [0, 0, 0], "label": null }""",
        "objects[0].label: must be a string, not null"
    )]
    [DataRow("""{ "class": "x", "team": 1, "team": 2, "position": [0, 0, 0] }""", "objects[0].team: duplicate key")]
    public void ABadOptionalObjectFieldIsAProblemNamingItsJsonPath(string obj, string where)
    {
        Assert.AreEqual("layout 'l.jsonc': " + where, Layout.Read("l.jsonc", WithObject(obj)).Problem);
    }

    [TestMethod]
    [DataRow(""" "background": 1, """, "background: must be a string, not 1")]
    [DataRow(""" "background": null, """, "background: must be a string, not null")]
    [DataRow(""" "ambient": [1, 1], """, "ambient: must be an array of 3 numbers [x, y, z], not an array of 2")]
    [DataRow(""" "ambient": [1, 1, -1e999], """, "ambient[2]: -1e999 is not a finite number")]
    [DataRow(""" "teamColors": [1, 2], """, "teamColors: must be an object, not an array of 2")]
    [DataRow(""" "teamColors": { "9": 1 }, """, "teamColors.9: unknown key; keys here: 0, 1, 2, 3, 4, 5, 6, 7, 8")]
    [DataRow(""" "teamColors": { "1": "red" }, """, "teamColors.1: must be a whole number, not \"red\"")]
    [DataRow(""" "teamColors": { "1": 1.5 }, """, "teamColors.1: must be a whole number, not 1.5")]
    [DataRow(""" "ambient": [1, 1, 1], "ambient": [0, 0, 0], """, "ambient: duplicate key")]
    public void ABadOptionalLayoutFieldIsAProblemNamingItsJsonPath(string top, string where)
    {
        var text = WithObject("""{ "class": "x", "team": 1, "position": [0, 0, 0] }""", top);
        Assert.AreEqual("layout 'l.jsonc': " + where, Layout.Read("l.jsonc", text).Problem);
    }
}
