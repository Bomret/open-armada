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

    [TestMethod]
    public void OrdnanceDefinitionsWithoutShieldKeysUseTheDefaults()
    {
        var ordnance = Beam();

        Assert.AreEqual("xshldx01", ordnance.ShieldGeometryName);
        Assert.AreEqual(0.8f, ordnance.ShieldDuration);
    }
}
