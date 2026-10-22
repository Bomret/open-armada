using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Sandbox;
using static OpenArmada.Tests.CombatFixtures;

namespace OpenArmada.Tests;

// The shield hit effect and the shield-down overlay the combat world decides, over units built in code.
[TestClass]
public class ShieldEffectTests
{
    static OrdnanceProfile Torpedo(
        bool ignoreShield = false,
        string geometry = OrdnanceProfile.DefaultShieldGeometry,
        float duration = OrdnanceProfile.DefaultShieldDuration
    ) =>
        Beam(
            damage: 10,
            threshold: 99,
            label: "photontorpedo",
            speed: 100,
            life: 5,
            ignoreShield: ignoreShield,
            shieldGeometry: geometry,
            shieldDuration: duration
        );

    // a (team 1, at 0) fires one shot of `ordnance` at b (team 2, 50 away, shields `shields`); returns the world.
    static (CombatWorld World, PlacedObject A, PlacedObject B) Fight(
        OrdnanceProfile ordnance,
        float shields = 100,
        float radius = 0,
        IReadOnlyList<SystemProfile>? systems = null
    )
    {
        var a = Placed("a", 1, 0);
        var b = Placed("b", 2, 50);
        var world = new CombatWorld(
            new UnitPoses([a, b]),
            [
                new CombatUnit
                {
                    Unit = a,
                    Profile = Profile([Weapon(ordnance, delay: 100, range: 200)], hull: 100),
                    DefinitionName = "a.odf",
                    Radius = 0,
                    Mobile = true,
                },
                new CombatUnit
                {
                    Unit = b,
                    Profile = Profile(maxHealth: shields, hull: 100, systems: systems),
                    DefinitionName = "b.odf",
                    Radius = radius,
                    Mobile = true,
                },
            ],
            1,
            () => 0,
            (_, _, pose) => pose.Position
        );
        world.Attack(a, b);

        return (world, a, b);
    }

    [TestMethod]
    public void ATorpedoLandingOnRaisedShieldsRaisesOneEffectWithTheOrdnancesGeometryAndDuration()
    {
        var (world, _, b) = Fight(Torpedo(geometry: "xcustom", duration: 1.5f));

        var effects = world.Step(1).OfType<ShieldHitEffect>().ToList();

        Assert.AreEqual(1, effects.Count);
        Assert.AreSame(b, effects[0].Unit);
        Assert.AreEqual("xcustom", effects[0].Geometry);
        Assert.AreEqual(1.5, effects[0].Duration, 1e-6);
    }

    [TestMethod]
    public void TheEffectColorReadsTheShieldsLeftAfterTheHit()
    {
        var (world, _, _) = Fight(Torpedo());

        var effect = world.Step(1).OfType<ShieldHitEffect>().Single();

        Assert.AreEqual(0.9, effect.Fraction, 1e-6);
        Assert.AreEqual(0.1, effect.Red, 1e-6);
        Assert.AreEqual(0.9, effect.Green, 1e-6);
    }

    [TestMethod]
    public void AHitThatEmptiesTheShieldsStillRaisesTheEffectFullyRed()
    {
        var (world, _, _) = Fight(Torpedo(), shields: 10);

        var effect = world.Step(1).OfType<ShieldHitEffect>().Single();

        Assert.AreEqual(1, effect.Red, 1e-6);
        Assert.AreEqual(0, effect.Green, 1e-6);
    }

    [TestMethod]
    public void AHitOnAUnitWithShieldsAtZeroRaisesNoEffect()
    {
        var (world, _, b) = Fight(Torpedo());
        world.State(b)!.Shields = 0;

        Assert.AreEqual(0, world.Step(1).OfType<ShieldHitEffect>().Count());
    }

    [TestMethod]
    public void OrdnanceThatIgnoresShieldsRaisesNoEffect()
    {
        var (world, _, _) = Fight(Torpedo(ignoreShield: true));

        var events = world.Step(1);

        Assert.AreEqual(1, events.OfType<ShotLanded>().Count());
        Assert.AreEqual(0, events.OfType<ShieldHitEffect>().Count());
    }

    [TestMethod]
    public void TheEffectSitsOnTheShieldSurfaceTowardTheHit()
    {
        var (world, _, _) = Fight(Torpedo(), radius: 8);

        var effect = world.Step(1).OfType<ShieldHitEffect>().Single();

        // the torpedo comes from -X toward the center at x=50; the contact is on the bubble facing it
        Assert.AreEqual(50 - 8 * CombatWorld.BubbleRadii, effect.Contact.X, 1e-3);
        Assert.AreEqual(0, effect.Contact.Z, 1e-3);
    }

    static UnitCombatState WithGenerator() =>
        State(Profile(systems: [Sys(SystemNames.ShieldGenerator, 5, repairTime: 1)]));

    [TestMethod]
    public void ADestroyedShieldGeneratorShowsTheOverlay()
    {
        var unit = WithGenerator();
        Assert.IsFalse(unit.ShieldDownOverlay);

        unit.DestroySystem(SystemNames.ShieldGenerator);

        Assert.IsTrue(unit.ShieldDownOverlay);
    }

    [TestMethod]
    public void ARepairedShieldGeneratorRemovesTheOverlay()
    {
        var unit = WithGenerator();
        unit.DestroySystem(SystemNames.ShieldGenerator);

        unit.RestoreSystem(SystemNames.ShieldGenerator);

        Assert.IsFalse(unit.ShieldDownOverlay);
    }

    [TestMethod]
    public void AUnitTurningDerelictLosesTheOverlayAndRegainsItWhenCrewRecovers()
    {
        var unit = WithGenerator();
        unit.DestroySystem(SystemNames.ShieldGenerator);

        unit.Derelict = true;
        Assert.IsFalse(unit.ShieldDownOverlay);

        unit.Derelict = false;
        Assert.IsTrue(unit.ShieldDownOverlay);
    }

    [TestMethod]
    public void AShieldGeneratorDestroyedOnADerelictShowsNoOverlay()
    {
        var unit = WithGenerator();
        unit.Derelict = true;

        unit.DestroySystem(SystemNames.ShieldGenerator);

        Assert.IsFalse(unit.ShieldDownOverlay);
    }

    [TestMethod]
    public void AUnitWithoutAShieldGeneratorShowsNoOverlay() => Assert.IsFalse(State(Profile()).ShieldDownOverlay);

    [TestMethod]
    public void ABeamRaisesOneEffectOnceATenthOfItsLifeHasPassed()
    {
        var (world, _, b) = Fight(Beam(life: 1, shieldGeometry: "xcustom"));

        Assert.AreEqual(0, world.Step(0.1).OfType<ShieldHitEffect>().Count());
        var effects = world.Step(0.04).OfType<ShieldHitEffect>().ToList();
        var later = world.Step(2).OfType<ShieldHitEffect>().Count();

        Assert.AreEqual(1, effects.Count);
        Assert.IsTrue(effects[0].FollowsBeam);
        Assert.AreSame(b, effects[0].Unit);
        Assert.AreEqual("xcustom", effects[0].Geometry);
        Assert.AreEqual(0, later);
    }

    [TestMethod]
    public void ABeamCutOffBeforeATenthOfItsLifeRaisesNeitherEffectNorSprite()
    {
        var (world, a, _) = Fight(Beam(life: 1));
        world.Step(0.06);
        world.State(a)!.Destroyed = true;

        var events = world.Step(2);

        Assert.AreEqual(0, events.OfType<ShieldHitEffect>().Count());
        Assert.AreEqual(0, events.OfType<HullHitSprite>().Count());
    }

    [TestMethod]
    public void ABeamOnAUnitWithShieldsAtZeroRaisesTheHullHitSpriteAtTwiceTheSize()
    {
        var (world, _, b) = Fight(Beam(life: 1, size: 3));
        world.State(b)!.Shields = 0;

        var events = world.Step(0.2);

        var sprite = events.OfType<HullHitSprite>().Single();
        Assert.AreEqual(6f, sprite.Scale);
        Assert.AreSame(b, sprite.Unit);
        Assert.AreEqual(0, events.OfType<ShieldHitEffect>().Count());
    }

    [TestMethod]
    public void ABeamOfOrdnanceThatIgnoresShieldsRaisesTheHullHitSprite()
    {
        var (world, _, _) = Fight(Beam(life: 1, ignoreShield: true));

        var events = world.Step(0.2);

        Assert.AreEqual(1, events.OfType<HullHitSprite>().Count());
        Assert.AreEqual(0, events.OfType<ShieldHitEffect>().Count());
    }

    [TestMethod]
    public void ABeamEffectTimingDoesNotDependOnTheFrameRate()
    {
        var (slow, _, _) = Fight(Beam(life: 1));
        var (fast, _, _) = Fight(Beam(life: 1));
        var slowCount = slow.Step(0.5).OfType<ShieldHitEffect>().Count();
        var fastCount = Enumerable.Range(0, 25).Sum(_ => fast.Step(0.02).OfType<ShieldHitEffect>().Count());

        Assert.AreEqual(1, slowCount);
        Assert.AreEqual(1, fastCount);
    }
}
