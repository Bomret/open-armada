using System;
using System.IO;
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
                .Nodes(nodes.Select(n => new NodeSpec { Name = n, Parent = n == "root" ? "" : "root" }).ToArray())
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

    // The shot draws the frame for its age, as it did before sprite nodes shared SpriteArt.
    [TestMethod]
    public void ShotFlipbookShowsTheFrameForTheShotsAgeAndLoops()
    {
        var art = CombatArt.Build(Install(), new[] { Unit(Armed("torp", "ph.wav", "hp01")) });
        var torp = art.Sprite("torp")!; // four frames of 0.125 s
        CollectionAssert.AreEqual(
            new[] { torp.Frames[0], torp.Frames[1], torp.Frames[3], torp.Frames[0], torp.Frames[2] },
            new[] { 0, 0.13, 0.4, 0.5, 0.8 }.Select(torp.FrameAt).ToArray()
        );
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
    public void FireSoundTheInstallLacksIsOneGapAndNotListed()
    {
        var art = CombatArt.Build(Install(), new[] { Unit(Armed("phaser", "gone.wav", "hp01")) });
        CollectionAssert.DoesNotContain(art.Sounds.ToArray(), "gone.wav");
        CollectionAssert.AreEqual(new[] { "gone.wav" }, art.Reports.Single().Names.ToArray());
    }

    [TestMethod]
    public void HardpointTheModelHoldsIsKnownAndOneItLacksIsAGap()
    {
        var unit = Unit(Armed("phaser", "ph.wav", "hp01", "hp99"));
        var art = CombatArt.Build(Install(), new[] { unit });
        Assert.AreEqual("hp01", art.Hardpoint(unit.Unit, "hp01"));
        Assert.IsNull(art.Hardpoint(unit.Unit, "hp99"));
        CollectionAssert.AreEqual(new[] { "hp99" }, art.Reports.Single().Names.ToArray());
    }

    [TestMethod]
    public void HardpointOfAnotherCaseResolvesToTheNodesExactName()
    {
        var unit = Unit(Armed("phaser", "ph.wav", "HP01"));
        var art = CombatArt.Build(Install(), new[] { unit });
        Assert.AreEqual("hp01", art.Hardpoint(unit.Unit, "HP01"));
        Assert.AreEqual(0, art.Reports.Count);
    }

    [TestMethod]
    public void HardpointResolvesToTheNameTheBuiltNodeCarries()
    {
        var unit = Unit(Armed("phaser", "ph.wav", "hp.01"));
        unit = (unit.Unit with { Model = "dotted" }, unit.Profile);
        var art = CombatArt.Build(Install(("SOD/dotted.sod", Model("root", "hp.01"))), new[] { unit });
        Assert.AreEqual("hp_01", art.Hardpoint(unit.Unit, "hp.01"));
        Assert.AreEqual(0, art.Reports.Count);
    }

    [TestMethod]
    public void ModelThatCannotBeReadLeavesEveryHardpointMissing()
    {
        var unit = (
            Placed("ship", 1, 0) with
            {
                Model = "nomodel",
            },
            Profile(new[] { Armed("phaser", "ph.wav", "hp01") }) with
            {
                Fireball = "xfb",
            }
        );
        var art = CombatArt.Build(Install(), new[] { unit });
        Assert.IsNull(art.Hardpoint(unit.Item1, "hp01"));
        CollectionAssert.AreEqual(new[] { "hp01" }, art.Reports.Single().Names.ToArray());
    }

    [TestMethod]
    public void SpriteUsesAnAnimationAnotherTableDefines()
    {
        var art = CombatArt.Build(
            Install(
                (
                    "Sprites/weapon.spr",
                    "sprite_table\n@reference=128\n@tmaterial=additive\ntorp tex2 0 0 32 32 @anim=flip\n"
                ),
                (
                    "Sprites/anims.spr",
                    "sprite_table\n@animation flip\noffset 4 0.5 step\n@auto=column\n@keyframes\nother tex 0 0 8 8\n"
                )
            ),
            new[] { Unit(Armed("torp", "ph.wav", "hp01")) }
        );
        Assert.AreEqual(4, art.Sprite("torp")!.Frames.Count);
        Assert.AreEqual(0.125, art.Sprite("torp")!.FrameSeconds, 1e-9);
    }

    [TestMethod]
    public void SpriteNamedInTwoTablesTakesTheTableFirstInNameOrder()
    {
        var art = CombatArt.Build(
            Install(
                ("Sprites/b.spr", "sprite_table\nphaser texb 0 0 1 1\n"),
                ("Sprites/a.spr", "sprite_table\nphaser texa 0 0 1 1\n")
            ),
            new[] { Unit(Armed("phaser", "ph.wav", "hp01")) }
        );
        Assert.AreEqual("texa", art.Sprite("phaser")!.Sprite.Texture);
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
        var gap = art.Reports.Single();
        StringAssert.Contains(gap.Cause, "fireball");
        CollectionAssert.AreEqual(new[] { unit.Unit.Class }, gap.Names.ToArray());
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
        var gap = art.Reports.Single();
        StringAssert.Contains(gap.Cause, "explosion");
        CollectionAssert.AreEqual(new[] { "none.wav" }, gap.Names.ToArray());
    }

    [TestMethod]
    public void ExplosionNamingNoSoundIsAGapOfItsOwnCause()
    {
        var unit = Unit(Armed("phaser", "", "hp01"));
        var art = CombatArt.Build(Install(("addon/xfb.odf", "dbName = \"xplode\"\r\nlength = 2\r\n")), new[] { unit });
        Assert.AreEqual(2, art.Reports.Count); // the weapon's silence and the explosion's are two causes
        StringAssert.Contains(art.Reports.Single(r => r.Cause.Contains("explosion")).Cause, "sound");
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

    // A table the install cannot read adds no sprites and keeps none of the others from loading (INV-0002).
    // Windows has no POSIX file modes; MSTest reports the test as skipped there.
    [TestMethod, System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    public void SpriteTableThatCannotBeReadAddsNoneAndStopsNothing()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("needs POSIX file modes");
        }

        var files = Install(("Sprites/a.spr", "sprite_table\n"));
        var locked = files.Find("Sprites/a.spr")!;
        File.SetUnixFileMode(locked, UnixFileMode.None);
        try
        {
            // Root reads a file whatever its mode (container CI), so the fault cannot be staged there.
            try
            {
                File.ReadAllBytes(locked);
                Assert.Inconclusive("the file stays readable (running as root)");
            }
            catch (UnauthorizedAccessException) { }

            var art = CombatArt.Build(files, new[] { Unit(Armed("torp", "ph.wav", "hp01")) });
            Assert.IsNotNull(art.Sprite("torp"));
        }
        finally
        {
            File.SetUnixFileMode(locked, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    [TestMethod]
    public void FireAndExplosionSoundThatDifferOnlyInCaseAreListedOnce()
    {
        var unit = Unit(Armed("phaser", "boom.wav", "hp01"));
        var art = CombatArt.Build(
            Install(("addon/xfb.odf", Explosion.Replace("boom.wav", "BOOM.WAV"))),
            new[] { unit }
        );
        Assert.AreEqual(1, art.Sounds.Count());
    }

    [TestMethod]
    public void ViewFailuresJoinTheReportsOncePerCause()
    {
        var art = CombatArt.Build(Install(), new[] { Unit(Armed("phaser", "ph.wav", "hp01")) });
        art.Report("sound cannot be decoded", "ph.wav");
        art.Report("sound cannot be decoded", "ph.wav");
        Assert.AreEqual(2, art.Reports.Single().Count);
    }

    [TestMethod]
    public void EachGapCauseFoundWhileBuildingIsOneWarnEntryEqualToItsLabelLine()
    {
        using var log = new CapturedLog();
        var art = CombatArt.Build(
            Install(),
            new[] { Unit(Armed("phaser", "gone.wav", "hp01", "hp99")), Unit(Armed("phaser", "gone2.wav", "hp99")) }
        );

        Assert.IsTrue(art.Reports.Count >= 2);
        Assert.AreEqual(art.Reports.Count, log.Lines.Count, log.Text);
        for (var i = 0; i < log.Lines.Count; i++)
        {
            StringAssert.Contains(log.Lines[i], " WARN combat: " + art.Reports[i].Text);
        }
    }

    [TestMethod]
    public void AnExplosionDefinitionThatCannotBeReadIsOneErrorEntryWithItsNameAndReasonAndTheGroupedLineIsNotLogged()
    {
        // xfc.odf is an archive member stored compressed, so reading it throws
        var archive = Encoding.Latin1.GetString(ZfsBuilder.Build(0, ZfsBuilder.M("xfc.odf", Explosion, 2)));
        using var log = new CapturedLog();
        var a = Unit(Armed("phaser", "ph.wav", "hp01"), "xfc");
        var b = Unit(Armed("phaser", "ph.wav", "hp01"), "xfc");
        var art = CombatArt.Build(Install(("addon/x.zfs", archive)), new[] { a, b });

        var entry = log.Lines.Single(l => l.Contains("xfc"));
        StringAssert.Contains(entry, " ERROR combat: explosion definition xfc: ");
        Assert.IsFalse(log.Lines.Any(l => l.Contains(" WARN ") && l.Contains("explosion definition cannot be read")));
        StringAssert.Contains(art.Reports.Single().Cause, "explosion definition cannot be read");
    }

    [TestMethod]
    public void ASoundThatCannotBeDecodedIsOneErrorEntryWithItsNameAndReasonAndTheGroupedLineIsNotLogged()
    {
        var art = CombatArt.Build(Install(), new[] { Unit(Armed("phaser", "ph.wav", "hp01")) });
        using var log = new CapturedLog();
        art.Fail("sound", "ph.wav", "bad RIFF header", "sound cannot be decoded, it is not played");

        StringAssert.Contains(log.Lines.Single(), " ERROR combat: sound ph.wav: bad RIFF header");
        StringAssert.Contains(art.Reports.Single().Text, "sound cannot be decoded");
    }
}
