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
    public void DefinitionNamesAMissingDbNameParticleArtAndWorkerBeeModel()
    {
        var fs = Install(("addon/d.odf", "dbName = \"nodb\"\r\nparticleArt = \"noart\"\r\nworkerBeeName = \"nobee\"\r\n"),
            ("addon/e.odf", "dbName = \"here\"\r\nparticleArt = \"here\"\r\nworkerBeeName = \"here\"\r\n"), ("SOD/here.sod", Model("")));
        CollectionAssert.AreEquivalent(new[] { "nodb", "noart", "nobee" }, new DefinitionKind().Unresolved(fs, "d.odf").ToArray());
        Assert.AreEqual(0, new DefinitionKind().Unresolved(fs, "e.odf").Count);
    }

    [TestMethod]
    public void ShotGeometryWithASodSuffixResolves()
    {
        var fs = Install(("addon/d.odf", "shotGeometry = \"here.sod\"\r\n"), ("SOD/here.sod", Model("")));
        Assert.AreEqual(0, new DefinitionKind().Unresolved(fs, "d.odf").Count);
    }

    [TestMethod]
    public void SoundInASoundsSubfolderWithNoIndexEntryResolves()
    {
        var fs = Install(("addon/d.odf", "fireSound = \"x.wav\"\r\nhitSound = \"gone.wav\"\r\n"), ("sounds/effects/x.wav", ""));
        CollectionAssert.AreEqual(new[] { "gone.wav" }, new DefinitionKind().Unresolved(fs, "d.odf").ToArray());
    }

    [TestMethod]
    public void SoundWithAFolderResolvesByItsPathWithinSounds()
    {
        var fs = Install(("addon/d.odf", "fireSound = \"effects/x.wav\"\r\nhitSound = \"other/x.wav\"\r\n"), ("sounds/effects/x.wav", ""));
        CollectionAssert.AreEqual(new[] { "other/x.wav" }, new DefinitionKind().Unresolved(fs, "d.odf").ToArray());
    }

    [TestMethod]
    public void MissingGrandparentIsListedForTheChildWhenTheParentIsOnlyAnInclude()
    {
        // p.odf is a loose file the index places in a folder: no archive member, so no enumerated definition.
        var fs = Install(("addon/c.odf", "#include \"p.odf\"\r\n"), ("misc/p.odf", "#include \"gone.odf\"\r\n"),
            ("startrek.zix", "000001\n0 d:\\x x 'Disc\n----\nDIR: misc\n----\n1 p.odf\n"));
        Assert.IsNotNull(fs.Find("p.odf"));
        var all = new DefinitionKind().Enumerate(fs).SelectMany(d => new DefinitionKind().Unresolved(fs, d)).ToList();
        CollectionAssert.AreEqual(new[] { "gone.odf" }, all);
    }

    [TestMethod]
    public void MissingParentIsAnUnresolvedReferenceAndTheDefinitionStillResolves()
    {
        var root = temp.MakeInstall(("addon/c.odf", "#include \"gone.odf\"\r\nbaseName = \"nomodel\"\r\n"));
        var w = new StringWriter();
        var code = AssetCheck.Run(root, new AssetKind[] { new DefinitionKind() }, w);
        var output = w.ToString();
        StringAssert.Contains(output, "UNRESOLVED definitions/c.odf: gone.odf");
        StringAssert.Contains(output, "UNRESOLVED definitions/c.odf: nomodel");
        Assert.AreEqual(0, code, output);
    }

    [TestMethod]
    public void IncludeCycleStillFailsTheDefinition()
    {
        var root = temp.MakeInstall(("addon/a.odf", "#include \"b.odf\"\r\n"), ("addon/b.odf", "#include \"a.odf\"\r\n"));
        var w = new StringWriter();
        Assert.AreEqual(1, AssetCheck.Run(root, new AssetKind[] { new DefinitionKind() }, w), w.ToString());
    }

    [TestMethod]
    public void ChildInheritingAMissingModelAndAMissingParentIsReportedOnTheParentOnly()
    {
        var fs = Install(("addon/p.odf", "#include \"gone.odf\"\r\nbaseName = \"nomodel\"\r\n"), ("addon/c.odf", "#include \"p.odf\"\r\n"));
        CollectionAssert.AreEquivalent(new[] { "gone.odf", "nomodel" }, new DefinitionKind().Unresolved(fs, "p.odf").ToArray());
        Assert.AreEqual(0, new DefinitionKind().Unresolved(fs, "c.odf").Count);
    }

    [TestMethod]
    public void MissingIncludeOfAnEnumeratedParentIncludedWithAFolderPrefixIsListedForTheParentOnly()
    {
        var fs = Install(("addon/p.odf", "#include \"gone.odf\"\r\n"), ("addon/c.odf", "#include \"addon/p.odf\"\r\n"));
        Assert.IsNotNull(fs.Find("addon/p.odf"));
        var all = new[] { "c.odf", "p.odf" }.SelectMany(d => new DefinitionKind().Unresolved(fs, d)).ToList();
        CollectionAssert.AreEqual(new[] { "gone.odf" }, all);
    }

    [TestMethod]
    public void MissingIncludeOfAnEnumeratedParentIncludedWithABackslashFolderPrefixIsListedForTheParentOnly()
    {
        var fs = Install(("addon/p.odf", "#include \"gone.odf\"\r\n"), ("addon/c.odf", "#include \"addon\\p.odf\"\r\n"));
        Assert.IsNotNull(fs.Find("addon\\p.odf"));
        var all = new[] { "c.odf", "p.odf" }.SelectMany(d => new DefinitionKind().Unresolved(fs, d)).ToList();
        CollectionAssert.AreEqual(new[] { "gone.odf" }, all);
    }

    // misc/p.odf shares its bare name with the enumerated addon/p.odf, which does not hold the include.
    [TestMethod]
    public void MissingIncludeOfALooseParentSharingItsNameWithAnEnumeratedDefinitionIsListedForTheChild()
    {
        var fs = Install(("addon/p.odf", "x = 1\r\n"), ("addon/c.odf", "#include \"misc/p.odf\"\r\n"),
            ("misc/p.odf", "#include \"gone.odf\"\r\n"));
        var all = new[] { "c.odf", "p.odf" }.SelectMany(d => new DefinitionKind().Unresolved(fs, d).Select(u => $"{d}: {u}")).ToList();
        CollectionAssert.AreEqual(new[] { "c.odf: gone.odf" }, all);
    }

    [TestMethod]
    public void MissingIncludeOfAnEnumeratedParentInAChainIsListedForTheParentOnly()
    {
        var fs = Install(("addon/g.odf", "x = 1\r\n"), ("addon/p.odf", "#include \"g.odf\"\r\n#include \"gone.odf\"\r\n"),
            ("addon/c.odf", "#include \"p.odf\"\r\n"));
        var all = new[] { "c.odf", "p.odf", "g.odf" }.SelectMany(d => new DefinitionKind().Unresolved(fs, d)).ToList();
        CollectionAssert.AreEqual(new[] { "gone.odf" }, all);
    }

    [TestMethod]
    public void MissingIncludeOfALooseParentIsListedForEachEnumeratedChildThatReachesIt()
    {
        var fs = Install(("addon/a.odf", "#include \"p.odf\"\r\n"), ("addon/b.odf", "#include \"p.odf\"\r\n"),
            ("misc/p.odf", "#include \"gone.odf\"\r\n"), ("startrek.zix", "000001\n0 d:\\x x 'Disc\n----\nDIR: misc\n----\n1 p.odf\n"));
        CollectionAssert.AreEqual(new[] { "gone.odf" }, new DefinitionKind().Unresolved(fs, "a.odf").ToArray());
        CollectionAssert.AreEqual(new[] { "gone.odf" }, new DefinitionKind().Unresolved(fs, "b.odf").ToArray());
    }

    [TestMethod]
    public void ModelMissingAnimationAndSpriteMissingTextureAreEachPrintedOnce()
    {
        var root = temp.MakeInstall(("SOD/m.sod", Model("", (4, "hull", "spin", 0f))), ("Sprites/t.spr", "sprite_table\n@reference=128\na gone 0 0 32 32\n"));
        var w = new StringWriter();
        AssetCheck.Run(root, new AssetKind[] { new ModelKind(), new SpriteTableKind() }, w);
        var lines = w.ToString().Split('\n');
        Assert.AreEqual(1, lines.Count(l => l.Contains("'spin'") || l.Contains(": spin")), w.ToString());
        Assert.AreEqual(1, lines.Count(l => l.Contains("'gone'") || l.Contains(": gone")), w.ToString());
    }

    [TestMethod]
    public void DefinitionVisualResolvesAsATextureOrASprite()
    {
        var fs = Install(("addon/d.odf", "smokeTexture = \"puff\"\r\ntexture = \"flash\"\r\nSprite = \"nowhere\""),
            ("Textures/RGB/puff.tga", ""), ("Sprites/t.spr", "sprite_table\n@reference=128\nflash puff 0 0 32 32\n"));
        CollectionAssert.AreEqual(new[] { "nowhere" }, new DefinitionKind().Unresolved(fs, "d.odf").ToArray());
    }

    // Sprite tables and definitions decode the same Windows-1252 bytes the same way (0x80 is the euro sign).
    [TestMethod]
    public void SpriteNameWithAByteOfTheWindows1252RangeResolvesFromADefinition()
    {
        var fs = Install(("addon/d.odf", "Sprite = \"a\u0080\""), ("Textures/RGB/puff.tga", ""),
            ("Sprites/t.spr", "sprite_table\n@reference=128\na\u0080 puff 0 0 32 32\n"));
        Assert.AreEqual(0, new DefinitionKind().Unresolved(fs, "d.odf").Count);
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
