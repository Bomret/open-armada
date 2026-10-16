using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Assets;
using static OpenArmada.Tests.SodBytes;

namespace OpenArmada.Tests;

// References from one asset to another, and the classification the asset check prints (INV-0001: every
// install is built in code).
[TestClass]
public class CrossReferenceTests
{
    readonly TempInstall temp = new();

    [TestCleanup]
    public void CleanUp() => temp.CleanUp();

    static string Latin1(byte[] b) => Encoding.Latin1.GetString(b);

    static string Model(string texture, params (byte Type, string Node, string Animation, float Offset)[] refs) =>
        Latin1(new SodBytes(1.8f).Lighting(("lit", 1f, 1)).Nodes(new NodeSpec("hull", "", Triangle(texture: texture))).End(refs));

    static string Map(params string[] classes)
    {
        var b = new BznBuilder();
        foreach (var c in classes) b.Objects.Add(new(c, 1, new Vector3(100, 0, 100)));
        return Latin1(b.Build());
    }

    GameFileSystem Install(params (string Path, string Content)[] files) => new(temp.MakeInstall(files));

    [TestMethod]
    public void MapNamesAMissingDefinition()
    {
        var fs = Install(("bzn/m.bzn", Map("ghost", "real")), ("addon/real.odf", "x = 1"));
        CollectionAssert.AreEqual(new[] { "ghost.odf" }, new MapKind().Unresolved(fs, "bzn/m.bzn").ToArray());
    }

    [TestMethod]
    public void DefinitionNamesAMissingModelPhysicsDefinitionAndSound()
    {
        var fs = Install(("addon/d.odf", "baseName = \"nomodel\"\r\nphysicsFile = \"nophys.odf\"\r\nfireSound = \"nosound.wav\"\r\nunitName = \"nothing.sod\""),
            ("SOD/here.sod", Model("")), ("addon/p.odf", "baseName = \"here\"\r\nphysicsFile = \"p.odf\""));
        CollectionAssert.AreEquivalent(new[] { "nomodel", "nophys.odf", "nosound.wav" }, new DefinitionKind().Unresolved(fs, "d.odf").ToArray());
        Assert.AreEqual(0, new DefinitionKind().Unresolved(fs, "p.odf").Count);
    }

    [TestMethod]
    public void DefinitionVisualResolvesAsATextureOrASprite()
    {
        var fs = Install(("addon/d.odf", "smokeTexture = \"puff\"\r\ntexture = \"flash\"\r\nSprite = \"nowhere\""),
            ("Textures/RGB/puff.tga", ""), ("Sprites/t.spr", "sprite_table\n@reference=128\nflash puff 0 0 32 32\n"));
        CollectionAssert.AreEqual(new[] { "nowhere" }, new DefinitionKind().Unresolved(fs, "d.odf").ToArray());
    }

    [TestMethod]
    public void ModelNamesAMissingTextureAndAMissingAnimation()
    {
        var fs = Install(("SOD/m.sod", Model("notex", (4, "hull", "spin", 0f))), ("Textures/RGB/ok.tga", ""));
        CollectionAssert.AreEquivalent(new[] { "notex", "spin" }, new ModelKind().Unresolved(fs, "m.sod").ToArray());
    }

    [TestMethod]
    public void ModelTextureThatExistsIsResolved()
    {
        var fs = Install(("SOD/m.sod", Model("ok")), ("Textures/Index8/ok.tga", ""));
        Assert.AreEqual(0, new ModelKind().Unresolved(fs, "m.sod").Count);
    }

    [TestMethod]
    public void SpriteTableNamesAMissingTexture()
    {
        var fs = Install(("Sprites/t.spr", "sprite_table\n@reference=128\na gone 0 0 32 32\nb here 0 0 32 32\n"), ("Textures/RGB/here.tga", ""));
        CollectionAssert.AreEqual(new[] { "gone" }, new SpriteTableKind().Unresolved(fs, "Sprites/t.spr").ToArray());
    }

    [TestMethod]
    public void CheckListsEveryUnresolvedReferenceWithBothNamesAndDoesNotFail()
    {
        var root = temp.MakeInstall(("bzn/m.bzn", Map("ghost")), ("addon/real.odf", "baseName = \"nomodel\""),
            ("SOD/mod.sod", Model("notex")));
        var w = new StringWriter();
        var code = AssetCheck.Run(root, new AssetKind[] { new MapKind(), new DefinitionKind(), new ModelKind() }, w);
        var output = w.ToString();
        StringAssert.Contains(output, "UNRESOLVED maps/bzn/m.bzn: ghost.odf");
        StringAssert.Contains(output, "UNRESOLVED definitions/real.odf: nomodel");
        StringAssert.Contains(output, "UNRESOLVED models/mod.sod: notex");
        StringAssert.Contains(output, "3 unresolved references");
        Assert.AreEqual(0, code, output);
    }

    [TestMethod]
    public void CheckPrintsClassificationCounts()
    {
        var root = temp.MakeInstall(("SOD/a.sod", Model("")), ("help/h.htm", ""), ("giddi/j.std", ""));
        var w = new StringWriter();
        var code = AssetCheck.Run(root, new AssetKind[] { new ModelKind() }, w);
        StringAssert.Contains(w.ToString(), "files: 1 loaded, 1 out of scope, 1 known but not loaded, 0 unclassified");
        StringAssert.Contains(w.ToString(), "NOT LOADED giddi/j.std");
        Assert.AreEqual(0, code);
    }

    [TestMethod]
    public void FileNoRuleClassifiesFailsTheCheckAndIsNamed()
    {
        var root = temp.MakeInstall(("SOD/a.sod", Model("")), ("mystery/new.thing", ""));
        var w = new StringWriter();
        var code = AssetCheck.Run(root, new AssetKind[] { new ModelKind() }, w);
        Assert.AreEqual(1, code);
        StringAssert.Contains(w.ToString(), "UNCLASSIFIED mystery/new.thing");
    }
}
