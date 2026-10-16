using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Assets;
using OpenArmada.Formats;

namespace OpenArmada.Tests;

[TestClass]
public class SpriteTableKindTests
{
    readonly TempInstall temp = new();

    [TestCleanup]
    public void CleanUp() => temp.CleanUp();

    const string Head = "sprite_table\n";

    GameFileSystem Install(params (string Path, string Content)[] files) => new(temp.MakeInstall(files));

    [TestMethod]
    public void ListsEveryTableOfTheSpritesFolderWithItsFolder() =>
        CollectionAssert.AreEquivalent(new[] { "Sprites/a.spr", "Sprites/b.SPR" },
            new SpriteTableKind().Enumerate(Install(("Sprites/a.spr", Head), ("Sprites/b.SPR", Head), ("Sprites/readme.txt", "x"))).ToArray());

    [TestMethod]
    public void LoadResolvesIncludesInTheSpritesFolder()
    {
        var files = Install(("Sprites/m.spr", Head + "@include i.spr\n"), ("Sprites/i.spr", Head + "s t 0 0 1 1\n"));
        var table = (SpriteTable)new SpriteTableKind().Load(files, "Sprites/m.spr");
        Assert.AreEqual("s", table.Sprites.Single().Name);
    }

    [TestMethod]
    public void CycleFailsTheTablesInvolvedAndLeavesTheOthersLoadable()
    {
        var files = Install(("Sprites/a.spr", Head + "@include b.spr\n"), ("Sprites/b.spr", Head + "@include a.spr\n"), ("Sprites/ok.spr", Head + "s t 0 0 1 1\n"));
        var kind = new SpriteTableKind();
        Assert.ThrowsExactly<InvalidDataException>(() => kind.Load(files, "Sprites/a.spr"));
        Assert.AreEqual(1, ((SpriteTable)kind.Load(files, "Sprites/ok.spr")).Sprites.Count);
    }

    [TestMethod]
    public void MissingIncludeFailsTheTableNamingTheInclude()
    {
        var files = Install(("Sprites/a.spr", Head + "@include gone.spr\n"));
        var e = Assert.ThrowsExactly<InvalidDataException>(() => new SpriteTableKind().Load(files, "Sprites/a.spr"));
        StringAssert.Contains(e.Message, "gone.spr");
    }

    [TestMethod]
    public void MissingTextureIsUnresolvedOnceAndNoNote()
    {
        var files = Install(("Sprites/a.spr", Head + "here t_ok 0 0 1 1\nlost t_gone 0 0 1 1\nlost2 t_gone 0 0 1 1\n"), ("Textures/RGB/t_ok.tga", "x"));
        Assert.AreEqual(0, new SpriteTableKind().Check(files, "Sprites/a.spr").Count);
        CollectionAssert.AreEqual(new[] { "t_gone" }, new SpriteTableKind().Unresolved(files, "Sprites/a.spr").ToArray());
    }

    [TestMethod]
    public void AnimationOfAnotherTableOfTheInstallAnimatesTheSprite()
    {
        var files = Install(("Sprites/anims.spr", Head + "@animation r\noffset 2 1 step\n@auto=row\n@keyframes\n"),
            ("Sprites/a.spr", Head + "s t 0 0 8 8 @anim=r\n"), ("Textures/RGB/t.tga", "x"));
        var table = (SpriteTable)new SpriteTableKind().Load(files, "Sprites/a.spr");
        Assert.AreEqual(2, table.Frames(table.Sprites[0]).Count);
        Assert.AreEqual(0, new SpriteTableKind().Check(files, "Sprites/a.spr").Count);
    }

    [TestMethod]
    public void IsFoundByDiscovery() =>
        Assert.IsTrue(AssetKind.Discover().Any(k => k is SpriteTableKind));
}
