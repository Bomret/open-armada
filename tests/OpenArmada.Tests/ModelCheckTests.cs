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

    const string Flip = "sprite_table\n@animation flip\noffset 4 1.0 step\n@auto=square\n@keyframes\n@reference=128\ngate gatetex 0 0 32 32 @anim=flip\n";

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
        var files = Install(("SOD/m.sod", Latin1(new SodBytes(1.8f).Lighting(("lit", 1f, 1))
            .Nodes(new NodeSpec("hull", "", Triangle("sparkly"))).End())));
        var notes = new ModelKind().Check(files, "m.sod");
        StringAssert.Contains(notes.Single(), "'hull'");
        StringAssert.Contains(notes.Single(), "'sparkly'");
    }

    [TestMethod]
    public void CheckReportsAnUnknownLightingModel()
    {
        var files = Install(("SOD/m.sod", Latin1(new SodBytes(1.8f).Lighting(("lit", 1f, 9))
            .Nodes(new NodeSpec("hull", "", Triangle())).End())));
        StringAssert.Contains(new ModelKind().Check(files, "m.sod").Single(), "'lit'");
    }

    [TestMethod]
    public void CheckReportsAnAnimationReferenceNoTableDefines()
    {
        var files = Install(("SOD/m.sod", Latin1(new SodBytes(1.8f).Lighting(("lit", 1f, 1))
            .Nodes(new NodeSpec("hull", "", Triangle())).End((4, "hull", "spin", 0f)))));
        StringAssert.Contains(new ModelKind().Check(files, "m.sod").Single(), "'spin'");
    }

    [TestMethod]
    public void CheckAcceptsAnAnimationReferenceASpriteTableDefines()
    {
        var files = Install(("Sprites/t.spr", Flip), ("SOD/m.sod", Latin1(new SodBytes(1.8f).Lighting(("lit", 1f, 1))
            .Nodes(new NodeSpec("hull", "", Triangle())).End((4, "hull", "flip", 0.5f)))));
        Assert.AreEqual(0, new ModelKind().Check(files, "m.sod").Count);
    }

    [TestMethod]
    public void BrokenModelIsReportedForThatModelAndTheCheckGoesOn()
    {
        var bad = Triangle() with { Corners = new[] { (0, 0), (1, 1), (50, 2) } };
        var files = Install(
            ("SOD/bad.sod", Latin1(new SodBytes(1.8f).Lighting(("lit", 1f, 1)).Nodes(new NodeSpec("hull", "", bad), new NodeSpec("lost", "nowhere")).End())),
            ("SOD/ok.sod", Latin1(new SodBytes(1.8f).Lighting(("lit", 1f, 1)).Nodes(new NodeSpec("hull", "", Triangle())).End())));
        var output = new StringWriter();
        var code = AssetCheck.Run(Path.GetDirectoryName(Path.GetDirectoryName(files.Find("bad.sod")!)!), new AssetKind[] { new ModelKind() }, output);
        Assert.AreEqual(0, code);
        StringAssert.Contains(output.ToString(), "NOTE models/bad.sod: node 'hull': a face corner uses vertex 50");
        StringAssert.Contains(output.ToString(), "NOTE models/bad.sod: node 'lost' names the parent 'nowhere'");
        StringAssert.Contains(output.ToString(), "models: 2 loaded");
    }

    [TestMethod]
    public void TeamColorsReadEveryNamedSlot()
    {
        var team = TeamColors.Load(Install(("addon/teamcolor.odf", "red = 1.0 0.0 0.0\nblue = 0 0.4 1\n")));
        Assert.AreEqual(new Vector3(1f, 0f, 0f), team.Color("red"));
        Assert.AreEqual(new Vector3(0f, 0.4f, 1f), team.Color("Blue"));
    }

    [TestMethod]
    public void TeamColorsListTheGamesSlotsInTableOrder()
    {
        var team = TeamColors.Load(Install(("addon/teamcolor.odf", "red = 1 0 0\n")));
        Assert.AreEqual(27, team.Names.Count);
        Assert.AreEqual("empty", team.Names[0]);
        Assert.AreEqual("cerulean", team.Names[26]);
    }

    [TestMethod]
    public void SlotWithoutALineIsBlackAndNoted()
    {
        var team = TeamColors.Load(Install(("addon/teamcolor.odf", "red = 1 0 0\n")));
        Assert.AreEqual(Vector3.Zero, team.Color("green"));
        Assert.IsTrue(team.Notes.Any(n => n.Contains("'green'")));
    }

    [TestMethod]
    public void MissingTeamColorFileIsNotedAndEverySlotIsBlack()
    {
        var team = TeamColors.Load(Install());
        Assert.AreEqual(Vector3.Zero, team.Color("red"));
        Assert.IsTrue(team.Notes.Any(n => n.Contains("teamcolor.odf")));
    }

    [TestMethod]
    public void SlotLineWithoutThreeNumbersIsNoted()
    {
        var team = TeamColors.Load(Install(("addon/teamcolor.odf", "red = 1 0\n")));
        Assert.AreEqual(Vector3.Zero, team.Color("red"));
        Assert.IsTrue(team.Notes.Any(n => n.Contains("'red'")));
    }
}
