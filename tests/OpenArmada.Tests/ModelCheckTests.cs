using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Assets;
using OpenArmada.Formats;
using static OpenArmada.Tests.SodBytes;

namespace OpenArmada.Tests;

// Models and their sprite tables and team colors, through an install built in code (INV-0001).
[TestClass]
public class ModelCheckTests
{
    readonly TempInstall temp = new();

    [TestCleanup]
    public void CleanUp() => temp.CleanUp();

    const string Flip =
        "sprite_table\n@animation flip\noffset 4 1.0 step\n@auto=square\n@keyframes\n@reference=128\ngate gatetex 0 0 32 32 @anim=flip\n";

    GameFileSystem Install(params (string Path, string Content)[] files) => new(temp.MakeInstall(files));

    static string Latin1(byte[] b) => System.Text.Encoding.Latin1.GetString(b);

    [TestMethod]
    public void AnimationIsFoundByItsOwnName()
    {
        var a = ModelRules.FindAnimation(Install(("Sprites/t.spr", Flip)), "flip")!;
        Assert.AreEqual(4, a.Count);
    }

    [TestMethod]
    public void AnimationIsFoundByTheNameOfASpriteThatPlaysIt()
    {
        Assert.AreEqual("flip", ModelRules.FindAnimation(Install(("Sprites/t.spr", Flip)), "gate")!.Name);
    }

    [TestMethod]
    public void UnknownAnimationIsNotFound()
    {
        Assert.IsNull(ModelRules.FindAnimation(Install(("Sprites/t.spr", Flip)), "nothing"));
    }

    [TestMethod]
    public void CheckReportsAnUnknownTextureMaterialWithNodeAndName()
    {
        var files = Install(
            (
                "SOD/m.sod",
                Latin1(
                    new SodBytes(1.8f)
                        .Lighting(("lit", 1f, 1))
                        .Nodes(new NodeSpec("hull", "", Triangle("sparkly")))
                        .End()
                )
            )
        );
        var notes = new ModelKind().Check(files, "m.sod");
        StringAssert.Contains(notes.Single(), "'hull'");
        StringAssert.Contains(notes.Single(), "'sparkly'");
    }

    [TestMethod]
    public void CheckReportsAnUnknownLightingModel()
    {
        var files = Install(
            (
                "SOD/m.sod",
                Latin1(new SodBytes(1.8f).Lighting(("lit", 1f, 9)).Nodes(new NodeSpec("hull", "", Triangle())).End())
            )
        );
        StringAssert.Contains(new ModelKind().Check(files, "m.sod").Single(), "'lit'");
    }

    [TestMethod]
    public void AnAnimationReferenceNoTableDefinesIsUnresolvedNotANote()
    {
        var files = Install(
            (
                "SOD/m.sod",
                Latin1(
                    new SodBytes(1.8f)
                        .Lighting(("lit", 1f, 1))
                        .Nodes(new NodeSpec("hull", "", Triangle()))
                        .End((4, "hull", "spin", 0f))
                )
            )
        );
        Assert.AreEqual(0, new ModelKind().Check(files, "m.sod").Count);
        CollectionAssert.Contains(new ModelKind().Unresolved(files, "m.sod").ToArray(), "spin");
    }

    // Two references give two identical lines; each missing animation removes its own one.
    [TestMethod]
    public void RepeatedReferenceToAMissingAnimationLeavesNoNote()
    {
        var files = Install(
            (
                "SOD/m.sod",
                Latin1(
                    new SodBytes(1.8f)
                        .Lighting(("lit", 1f, 1))
                        .Nodes(new NodeSpec("hull", "", Triangle()))
                        .End((4, "hull", "spin", 0f), (4, "hull", "spin", 0f))
                )
            )
        );
        Assert.AreEqual(
            (0, 1),
            (
                new ModelKind().Check(files, "m.sod").Count,
                new ModelKind().Unresolved(files, "m.sod").Count(u => u == "spin")
            )
        );
    }

    [TestMethod]
    public void CheckAcceptsAnAnimationReferenceASpriteTableDefines()
    {
        var files = Install(
            ("Sprites/t.spr", Flip),
            (
                "SOD/m.sod",
                Latin1(
                    new SodBytes(1.8f)
                        .Lighting(("lit", 1f, 1))
                        .Nodes(new NodeSpec("hull", "", Triangle()))
                        .End((4, "hull", "flip", 0.5f))
                )
            )
        );
        Assert.AreEqual(0, new ModelKind().Check(files, "m.sod").Count);
    }

    [TestMethod]
    public void BrokenModelIsReportedForThatModelAndTheCheckGoesOn()
    {
        var bad = Triangle() with { Corners = new[] { (0, 0), (1, 1), (50, 2) } };
        var files = Install(
            (
                "SOD/bad.sod",
                Latin1(
                    new SodBytes(1.8f)
                        .Lighting(("lit", 1f, 1))
                        .Nodes(new NodeSpec("hull", "", bad), new NodeSpec("lost", "nowhere"))
                        .End()
                )
            ),
            (
                "SOD/ok.sod",
                Latin1(new SodBytes(1.8f).Lighting(("lit", 1f, 1)).Nodes(new NodeSpec("hull", "", Triangle())).End())
            )
        );
        var output = new StringWriter();
        var code = AssetCheck.Run(
            Path.GetDirectoryName(Path.GetDirectoryName(files.Find("bad.sod")!)!),
            new AssetKind[] { new ModelKind() },
            output
        );
        Assert.AreEqual(0, code);
        StringAssert.Contains(output.ToString(), "NOTE models/bad.sod: node 'hull': a face corner uses vertex 50");
        StringAssert.Contains(output.ToString(), "NOTE models/bad.sod: node 'lost' names the parent 'nowhere'");
        StringAssert.Contains(output.ToString(), "models: 2 loaded");
    }

    static byte[] Model(params NodeSpec[] nodes) => new SodBytes(1.8f).Lighting(("lit", 1f, 1)).Nodes(nodes).End();

    [TestMethod]
    public void LightingMaterialOfAnotherModelIsFoundByName()
    {
        // Lighting materials are global by name in the game: a model may use one another model defines.
        var files = Install(
            (
                "SOD/a.sod",
                Latin1(
                    new SodBytes(1.8f)
                        .Lighting(("shared", 1f, 2))
                        .Nodes(new NodeSpec("hull", "", Triangle(lighting: "shared")))
                        .End()
                )
            ),
            ("SOD/b.sod", Latin1(Model(new NodeSpec("hull", "", Triangle(lighting: "shared")))))
        );
        var b = (Sod)new ModelKind().Load(files, "b.sod");
        Assert.AreEqual(2, ModelRules.FindLightingMaterial(files, b, "shared")!.Model);
        Assert.AreEqual(0, new ModelKind().Check(files, "b.sod").Count);
    }

    // The game loads the override copy of a model, never the stock copy it replaces, so the override's
    // material is the one other models find.
    [TestMethod]
    public void LightingMaterialOfAnOverrideModelWinsOverTheStockCopy()
    {
        var files = Install(
            ("SOD/a.sod", Latin1(new SodBytes(1.8f).Lighting(("shared", 1f, 1)).Nodes().End())),
            ("addon/a.sod", Latin1(new SodBytes(1.8f).Lighting(("shared", 1f, 2)).Nodes().End())),
            ("SOD/b.sod", Latin1(Model(new NodeSpec("hull", "", Triangle(lighting: "shared")))))
        );
        var b = (Sod)new ModelKind().Load(files, "b.sod");
        Assert.AreEqual(2, ModelRules.FindLightingMaterial(files, b, "shared")!.Model);
    }

    // A software-renderer copy behind a main model of its name is never loaded by the game, so a material
    // only that copy defines is defined nowhere.
    [TestMethod]
    public void LightingMaterialOnlyAShadowedSoftwareCopyDefinesIsNotFound()
    {
        var files = Install(
            ("SOD/a.sod", Latin1(Model())),
            ("SOD/Software/a.sod", Latin1(new SodBytes(1.8f).Lighting(("soft", 1f, 2)).Nodes().End())),
            ("SOD/b.sod", Latin1(Model(new NodeSpec("hull", "", Triangle(lighting: "soft")))))
        );
        var b = (Sod)new ModelKind().Load(files, "b.sod");
        Assert.IsNull(ModelRules.FindLightingMaterial(files, b, "soft"));
    }

    [TestMethod]
    public void EmptyLightingMaterialNameIsNoMaterialEvenWhenTheModelDefinesOneNamedThat()
    {
        var files = Install(
            (
                "SOD/m.sod",
                Latin1(
                    new SodBytes(1.8f)
                        .Lighting(("", 1f, 2))
                        .Nodes(new NodeSpec("hull", "", Triangle(lighting: "")))
                        .End()
                )
            )
        );
        var sod = (Sod)new ModelKind().Load(files, "m.sod");
        Assert.IsNull(ModelRules.FindLightingMaterial(files, sod, ""));
        StringAssert.Contains(string.Join("\n", ModelRules.Analyze(files, sod).Problems), "name no lighting material");
    }

    [TestMethod]
    public void LightingMaterialNoModelDefinesIsReportedWithNodeAndName()
    {
        var files = Install(("SOD/m.sod", Latin1(Model(new NodeSpec("hull", "", Triangle(lighting: "ghost"))))));
        var note = new ModelKind().Check(files, "m.sod").Single();
        StringAssert.Contains(note, "'hull'");
        StringAssert.Contains(note, "'ghost'");
    }

    [TestMethod]
    public void GroupsNamingNoLightingMaterialAreReportedOncePerModel()
    {
        // The game gives such a group its default material; the engine stands in for it and says so once.
        var files = Install(
            (
                "SOD/m.sod",
                Latin1(
                    Model(new NodeSpec("a", "", Triangle(lighting: "")), new NodeSpec("b", "", Triangle(lighting: "")))
                )
            )
        );
        var note = new ModelKind().Check(files, "m.sod").Single();
        StringAssert.Contains(note, "2 groups");
        StringAssert.Contains(note, "no lighting material");
    }

    [TestMethod]
    public void UnknownSpriteTextureMaterialIsReportedWithTableAndSprite()
    {
        var files = Install(
            (
                "Sprites/t.spr",
                "sprite_table\n@tmaterial=sparkly\ngate gatetex 0 0 32 32\n@tmaterial=additive\nok gatetex 0 0 32 32\n"
            )
        );
        var notes = new SpriteTableKind()
            .Check(files, "Sprites/t.spr")
            .Where(n => n.Contains("texture material"))
            .ToList();
        Assert.AreEqual(1, notes.Count);
        StringAssert.Contains(notes[0], "'sparkly'");
        StringAssert.Contains(notes[0], "'gate'");
        StringAssert.Contains(notes[0], "t.spr");
    }

    [TestMethod]
    public void TeamColorNotesReachTheAssetCheck()
    {
        var files = Install(("addon/teamcolor.odf", "red = 1 0\n"));
        var kind = new TeamColorKind();
        var notes = kind.Check(files, kind.Enumerate(files).Single());
        Assert.IsTrue(notes.Any(n => n.Contains("'red'")));
    }

    [TestMethod]
    public void MissingTeamColorFileIsReportedByTheAssetCheck()
    {
        var files = Install();
        var kind = new TeamColorKind();
        Assert.IsTrue(kind.Check(files, kind.Enumerate(files).Single()).Any(n => n.Contains("teamcolor.odf")));
    }

    [TestMethod]
    public void AnimationReferenceToAMissingNodeIsReported()
    {
        var files = Install(
            ("Sprites/t.spr", Flip),
            (
                "SOD/m.sod",
                Latin1(
                    new SodBytes(1.8f)
                        .Lighting(("lit", 1f, 1))
                        .Nodes(new NodeSpec("hull", "", Triangle()))
                        .End((4, "gone", "flip", 0f))
                )
            )
        );
        StringAssert.Contains(new ModelKind().Check(files, "m.sod").Single(), "'gone'");
    }

    [TestMethod]
    public void AnimationReferenceToANodeWithoutAMeshIsReported()
    {
        var files = Install(
            ("Sprites/t.spr", Flip),
            (
                "SOD/m.sod",
                Latin1(
                    new SodBytes(1.8f)
                        .Lighting(("lit", 1f, 1))
                        .Nodes(new NodeSpec("pivot", ""))
                        .End((4, "pivot", "flip", 0f))
                )
            )
        );
        StringAssert.Contains(new ModelKind().Check(files, "m.sod").Single(), "'pivot'");
    }

    [TestMethod]
    public void AnimationReferenceDrivesEveryNodeOfThatName()
    {
        var files = Install(
            ("Sprites/t.spr", Flip),
            (
                "SOD/m.sod",
                Latin1(
                    new SodBytes(1.8f)
                        .Lighting(("lit", 1f, 1))
                        .Nodes(new NodeSpec("hull", "", Triangle()), new NodeSpec("hull", "", Triangle()))
                        .End((4, "hull", "flip", 0f))
                )
            )
        );
        var analysis = ModelRules.Analyze(files, (Sod)new ModelKind().Load(files, "m.sod"));
        Assert.AreEqual(2, analysis.Runs.Count);
    }

    [TestMethod]
    public void LightingModelByteZeroOfAnOldFileIsReportedWithItsRealByte()
    {
        var files = Install(
            (
                "SOD/m.sod",
                Latin1(
                    new SodBytes(1.4f)
                        .TextureMaterials()
                        .Lighting(("old", 1f, 0))
                        .Nodes(new NodeSpec("hull", "", Triangle("default", "", "old")))
                        .End()
                )
            )
        );
        var note = new ModelKind().Check(files, "m.sod").Single();
        StringAssert.Contains(note, "'old'");
        StringAssert.Contains(note, "byte 0");
    }

    [TestMethod]
    public void TeamColorsReadEveryNamedSlot()
    {
        var team = TeamColors.Load(Install(("addon/teamcolor.odf", "red = 1.0 0.0 0.0\nblue = 0 0.4 1\n")));
        Assert.AreEqual(new Vector3(1f, 0f, 0f), team.Color("red"));
        Assert.AreEqual(new Vector3(0f, 0.4f, 1f), team.Color("Blue"));
    }

    [TestMethod]
    public void SlotTakesTheFirstKeyThatStartsWithItsName()
    {
        var team = TeamColors.Load(
            Install(("addon/teamcolor.odf", "redish = 1 0 0\nred = 0 1 0\nBLUEgreen = 0 0 1\n"))
        );
        Assert.AreEqual(new Vector3(1f, 0f, 0f), team.Color("red"));
        Assert.AreEqual(new Vector3(0f, 0f, 1f), team.Color("blue"));
    }

    [TestMethod]
    public void SlotIgnoresAShorterKeyAndAKeyThatOnlyEndsInItsName()
    {
        var team = TeamColors.Load(Install(("addon/teamcolor.odf", "re = 1 0 0\nbrickred = 0 1 0\n")));
        Assert.AreEqual(Vector3.Zero, team.Color("red"));
        Assert.AreEqual(new Vector3(0f, 1f, 0f), team.Color("brickred"));
    }

    [TestMethod]
    public void StockShapedFileGivesNoNoteAndBlackForItsThreeMissingSlots()
    {
        // The stock file's shape, built from the slot names (INV-0001): every slot but these three, plus `entity`.
        string[] missing = { "alien", "burntsienna", "cerulean" };
        var given = TeamColors.SlotNames.Where(n => !missing.Contains(n)).ToList();
        var odf =
            string.Concat(given.Select((n, i) => System.FormattableString.Invariant($"{n} = {i / 100f:0.00} 0.5 1\n")))
            + "entity = 0.8 0.8 0.8\n";
        var team = TeamColors.Load(Install(("addon/teamcolor.odf", odf)));
        Assert.AreEqual(0, team.Notes.Count);
        foreach (var n in missing)
        {
            Assert.AreEqual(Vector3.Zero, team.Color(n));
        }
        for (var i = 0; i < given.Count; i++)
        {
            Assert.AreEqual(new Vector3(i / 100f, 0.5f, 1f), team.Color(given[i]), given[i]);
        }
    }

    [TestMethod]
    public void MalformedFirstMatchIsBlackAndNotedWithItsKey()
    {
        var team = TeamColors.Load(Install(("addon/teamcolor.odf", "redish = 1 0\nred = 0 1 0\n")));
        Assert.AreEqual(Vector3.Zero, team.Color("red"));
        var note = team.Notes.Single();
        StringAssert.Contains(note, "'red'");
        StringAssert.Contains(note, "'redish'");
    }

    [TestMethod]
    public void TeamColorOfAnUnknownSlotFailsNamingTheSlot()
    {
        var team = TeamColors.Load(Install(("addon/teamcolor.odf", "red = 1 0 0\n")));
        var e = Assert.ThrowsExactly<KeyNotFoundException>(() => team.Color("plaid"));
        StringAssert.Contains(e.Message, "team-color slot 'plaid'");
    }

    [TestMethod]
    public void TeamColorsListTheGamesSlotsInTableOrder()
    {
        Assert.AreEqual(27, TeamColors.SlotNames.Count);
        Assert.AreEqual("empty", TeamColors.SlotNames[0]);
        Assert.AreEqual("cerulean", TeamColors.SlotNames[26]);
    }

    [TestMethod]
    public void SlotWithoutALineIsBlackAndNotNoted()
    {
        var team = TeamColors.Load(Install(("addon/teamcolor.odf", "red = 1 0 0\n")));
        Assert.AreEqual(Vector3.Zero, team.Color("green"));
        Assert.AreEqual(0, team.Notes.Count);
    }

    [TestMethod]
    public void AssetCheckDoesNotReportASlotWithoutALine()
    {
        var files = Install(("addon/teamcolor.odf", "red = 1 0 0\n"));
        var kind = new TeamColorKind();
        Assert.AreEqual(0, kind.Check(files, kind.Enumerate(files).Single()).Count);
    }

    [TestMethod]
    public void MissingTeamColorFileIsNotedAndEverySlotIsBlack()
    {
        var team = TeamColors.Load(Install());
        foreach (var n in TeamColors.SlotNames)
        {
            Assert.AreEqual(Vector3.Zero, team.Color(n));
        }
        StringAssert.Contains(team.Notes.Single(), "teamcolor.odf");
    }

    [TestMethod]
    public void SlotLineWithoutThreeNumbersIsNoted()
    {
        var team = TeamColors.Load(Install(("addon/teamcolor.odf", "red = 1 0\n")));
        Assert.AreEqual(Vector3.Zero, team.Color("red"));
        Assert.IsTrue(team.Notes.Any(n => n.Contains("'red'")));
    }
}
