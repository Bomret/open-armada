using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Assets;
using OpenArmada.Sandbox;
using static OpenArmada.Tests.CombatFixtures;
using static OpenArmada.Tests.SodBytes;

namespace OpenArmada.Tests;

// The art of a fight resolved from an install built in code (INV-0001): shot sprites, fire sounds,
// explosions, and hardpoints, and the gaps found on the way.
[TestClass]
public class CombatArtTests
{
    readonly TempInstall temp = new();

    [TestCleanup]
    public void CleanUp() => temp.CleanUp();

    const string Sprites =
        "sprite_table\n@reference=128\n@tmaterial=additive\n@animation flip\noffset 4 0.5 step\n@auto=column\n@keyframes\n"
        + "phaser tex 0 0 128 21\ntorp tex2 0 0 32 32 @anim=flip\n";
    const string Explosion = "explSound = \"boom.wav\"\r\ndbName = \"xplode\"\r\nlength = 3.5\r\n";

    static string Latin1(byte[] bytes) => Encoding.Latin1.GetString(bytes);

    static string Model(params string[] nodes) =>
        Latin1(
            new SodBytes(1.8f)
                .Lighting(("lit", 1f, 1))
                .Nodes(nodes.Select(n => new NodeSpec(n, n == "root" ? "" : "root")).ToArray())
                .End()
        );

    // An install with the sprite table, a fire sound, an explosion, and a ship model with a root and hp01.
    GameFileSystem Install(params (string Path, string Content)[] more) =>
        new(
            temp.MakeInstall(
                new[]
                {
                    ("Sprites/weapon.spr", Sprites),
                    ("addon/ph.wav", "x"), // a bare name resolves through the override layer
                    ("addon/boom.wav", "x"),
                    ("addon/xfb.odf", Explosion),
                    ("SOD/xplode.sod", Model("root")),
                    ("SOD/ship.sod", Model("root", "hp01")),
                }
                    .Concat(more)
                    .ToArray()
            )
        );

    static WeaponProfile Armed(string sprite, string sound, params string[] hardpoints) =>
        Weapon() with
        {
            Hardpoints = hardpoints,
            FireSound = sound,
            Ordnance = Beam() with { Sprite = sprite },
        };

    static (PlacedObject Unit, CombatProfile Profile) Unit(WeaponProfile weapon, string fireball = "xfb") =>
        (Placed("ship", 1, 0) with { Model = "ship" }, Profile(new[] { weapon }) with { Fireball = fireball });

    [TestMethod]
    public void SpriteHasItsBlendAndItsFlipbookFrames()
    {
        var art = CombatArt.Build(Install(), new[] { Unit(Armed("torp", "ph.wav", "hp01")) });
        var sprite = art.Sprite("torp")!;
        Assert.AreEqual("additive", sprite.Sprite.Material);
        Assert.AreEqual(4, sprite.Frames.Count);
        Assert.AreEqual(0.125, sprite.FrameSeconds, 1e-9);
        Assert.AreEqual(0, art.Reports.Count);
    }

    [TestMethod]
    public void SpriteMissingFromTheTablesIsOneGapWithItsNameAndCount()
    {
        var a = Unit(Armed("nosuch", "ph.wav", "hp01"));
        var b = Unit(Armed("nosuch", "ph.wav", "hp01"));
        var art = CombatArt.Build(Install(), new[] { a, b });
        Assert.IsNull(art.Sprite("nosuch"));
        var gap = art.Reports.Single();
        CollectionAssert.AreEqual(new[] { "nosuch" }, gap.Names.ToArray());
        Assert.AreEqual(2, gap.Count);
    }

    [TestMethod]
    public void OrdnanceWithoutASpriteNameIsAGapNamingItsWeapon() =>
        CollectionAssert.AreEqual(
            new[] { "w" },
            CombatArt.Build(Install(), new[] { Unit(Armed("", "ph.wav", "hp01")) }).Reports.Single().Names.ToArray()
        );

    [TestMethod]
    public void FireSoundTheInstallHoldsResolves()
    {
        var art = CombatArt.Build(Install(), new[] { Unit(Armed("phaser", "ph.wav", "hp01")) });
        Assert.AreEqual("ph.wav", art.Sound("ph.wav"));
    }

    [TestMethod]
    public void FireSoundTheInstallLacksIsOneGapAndNoSound()
    {
        var art = CombatArt.Build(Install(), new[] { Unit(Armed("phaser", "gone.wav", "hp01")) });
        Assert.IsNull(art.Sound("gone.wav"));
        CollectionAssert.AreEqual(new[] { "gone.wav" }, art.Reports.Single().Names.ToArray());
    }

    [TestMethod]
    public void HardpointTheModelHoldsIsKnownAndOneItLacksIsAGap()
    {
        var unit = Unit(Armed("phaser", "ph.wav", "hp01", "hp99"));
        var art = CombatArt.Build(Install(), new[] { unit });
        Assert.IsTrue(art.Hardpoint(unit.Unit, "hp01"));
        Assert.IsFalse(art.Hardpoint(unit.Unit, "hp99"));
        CollectionAssert.AreEqual(new[] { "hp99" }, art.Reports.Single().Names.ToArray());
    }

    [TestMethod]
    public void ModelThatCannotBeReadLeavesEveryHardpointMissing()
    {
        var unit = (
            Placed("ship", 1, 0) with
            {
                Model = "nomodel",
            },
            Profile(new[] { Armed("phaser", "ph.wav", "hp01") })
        );
        var art = CombatArt.Build(Install(), new[] { unit });
        Assert.IsFalse(art.Hardpoint(unit.Item1, "hp01"));
    }

    [TestMethod]
    public void ExplosionHasItsSoundModelAndLength()
    {
        var unit = Unit(Armed("phaser", "ph.wav", "hp01"));
        var explosion = CombatArt.Build(Install(), new[] { unit }).Explosion(unit.Unit)!;
        Assert.AreEqual("boom.wav", explosion.Sound);
        Assert.AreEqual("xplode", explosion.Model);
        Assert.AreEqual(3.5f, explosion.Length);
    }

    [TestMethod]
    public void MissingExplosionDefinitionIsAGapAndNoExplosion()
    {
        var unit = Unit(Armed("phaser", "ph.wav", "hp01"), "nofire");
        var art = CombatArt.Build(Install(), new[] { unit });
        Assert.IsNull(art.Explosion(unit.Unit));
        CollectionAssert.AreEqual(new[] { "nofire" }, art.Reports.Single().Names.ToArray());
    }

    [TestMethod]
    public void UnitWithoutAFireballIsAGap()
    {
        var unit = Unit(Armed("phaser", "ph.wav", "hp01"), "");
        var art = CombatArt.Build(Install(), new[] { unit });
        Assert.IsNull(art.Explosion(unit.Unit));
        Assert.AreEqual(1, art.Reports.Count);
    }

    [TestMethod]
    public void ExplosionSoundTheInstallLacksIsAGapAndTheModelStillShows()
    {
        var unit = Unit(Armed("phaser", "ph.wav", "hp01"));
        var art = CombatArt.Build(
            Install(("addon/xfb.odf", "explSound = \"none.wav\"\r\ndbName = \"xplode\"\r\nlength = 2\r\n")),
            new[] { unit }
        );
        var explosion = art.Explosion(unit.Unit)!;
        Assert.IsNull(explosion.Sound);
        Assert.AreEqual("xplode", explosion.Model);
        CollectionAssert.AreEqual(new[] { "none.wav" }, art.Reports.Single().Names.ToArray());
    }

    [TestMethod]
    public void ListsWhatTheFightCanShowOnceEach()
    {
        var a = Unit(Armed("phaser", "ph.wav", "hp01"));
        var b = Unit(Armed("phaser", "ph.wav", "hp01"));
        var art = CombatArt.Build(Install(), new[] { a, b });
        Assert.AreEqual(1, art.Sprites.Count());
        CollectionAssert.AreEquivalent(new[] { "ph.wav", "boom.wav" }, art.Sounds.ToArray());
        CollectionAssert.AreEqual(new[] { "xplode" }, art.ExplosionModels.ToArray());
    }

    [TestMethod]
    public void ViewFailuresJoinTheReportsOncePerCause()
    {
        var art = CombatArt.Build(Install(), new[] { Unit(Armed("phaser", "ph.wav", "hp01")) });
        art.Report("sound cannot be decoded", "ph.wav");
        art.Report("sound cannot be decoded", "ph.wav");
        Assert.AreEqual(2, art.Reports.Single().Count);
    }
}
