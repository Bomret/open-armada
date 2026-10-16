using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Assets;
using OpenArmada.Sandbox;
using static OpenArmada.Tests.CombatFixtures;

namespace OpenArmada.Tests;

// The combat world: orders, targets, firing, shots in flight, and destruction, over units and a random source
// built in code. The pose source is the real UnitPoses, so units stand where they are placed.
[TestClass]
public class CombatWorldTests
{
    // A unit of the world under test: its object and the profile it fights with.
    sealed record Spec(PlacedObject Object, CombatProfile Profile, bool Mobile = true, float Radius = 0);

    static Spec Fighter(string label, int team, float x, WeaponProfile? weapon = null, float shields = 100) =>
        new(Placed(label, team, x), Profile(weapon == null ? null : new[] { weapon }, maxHealth: shields, hull: 100));

    static CombatWorld World(out UnitPoses poses, Func<double>? random = null, params Spec[] specs)
    {
        poses = new UnitPoses(specs.Select(s => s.Object));
        return new CombatWorld(
            poses,
            specs.Select(s => new CombatUnit(s.Object, s.Profile, s.Object.Label + ".odf", s.Radius, s.Mobile)),
            1,
            random ?? (() => 0)
        );
    }

    static CombatWorld World(Func<double>? random, params Spec[] specs) => World(out _, random, specs);

    static int Fired(IEnumerable<CombatEvent> events, string shooter) =>
        events.OfType<ShotFired>().Count(e => e.Shot.Shooter.Unit.Label == shooter);

    [TestMethod]
    public void AnAttackOrderOnAHostileUnitIsAccepted()
    {
        var a = Fighter("a", 1, 0, Weapon());
        var b = Fighter("b", 2, 50);
        Assert.IsTrue(World(null, a, b).Attack(a.Object, b.Object));
    }

    [TestMethod]
    public void AnAttackOrderOnAFriendlyOrNeutralUnitIsRefused()
    {
        var a = Fighter("a", 1, 0, Weapon());
        var friend = Fighter("f", 1, 50);
        var neutral = Fighter("n", 0, 60);
        var world = World(null, a, friend, neutral);
        Assert.IsFalse(world.Attack(a.Object, friend.Object));
        Assert.IsFalse(world.Attack(a.Object, neutral.Object));
    }

    [TestMethod]
    public void AUnitWithoutWeaponsRefusesAnAttackOrder()
    {
        var a = Fighter("a", 1, 0);
        var b = Fighter("b", 2, 50);
        Assert.IsFalse(World(null, a, b).Attack(a.Object, b.Object));
    }

    [TestMethod]
    public void AnAttackingShipClosesToTheLongestRangeLessFiftyThenStopsAndFaces()
    {
        var a = Fighter("a", 1, 0, Weapon(range: 200));
        var b = Fighter("b", 2, 400);
        var world = World(out var poses, null, a, b);
        world.Attack(a.Object, b.Object);
        world.Step(0.02);
        Assert.AreEqual(new MoveIntent(MoveKind.MoveTo, 400, 0), world.State(a.Object)!.Intent);
        // surface distance 150 is within 200 - 50
        poses.Advance(a.Object, MovingTo(a.Object, 250, 0), 1000);
        world.Step(0.02);
        Assert.AreEqual(new MoveIntent(MoveKind.Face, 400, 0), world.State(a.Object)!.Intent);
    }

    static ShipMotion MovingTo(PlacedObject o, float x, float z)
    {
        var motion = new ShipMotion(new PhysicsValues(10, 1, null), o.Position, 0);
        motion.SetTarget(x, z);
        return motion;
    }

    [TestMethod]
    public void TheClosingRangeIsMeasuredSurfaceToSurface()
    {
        var a = new Spec(Placed("a", 1, 0), Profile(new[] { Weapon(range: 200) }), true, 30);
        var b = new Spec(Placed("b", 2, 300), Profile(), true, 20);
        var world = World(null, a, b);
        world.Attack(a.Object, b.Object);
        world.Step(0.02); // surface distance 250 is beyond 150
        Assert.AreEqual(MoveKind.MoveTo, world.State(a.Object)!.Intent.Kind);
        var near = new Spec(Placed("a", 1, 0), Profile(new[] { Weapon(range: 200) }), true, 30);
        var other = new Spec(Placed("b", 2, 200), Profile(), true, 20); // surface distance 150
        var world2 = World(null, near, other);
        world2.Attack(near.Object, other.Object);
        world2.Step(0.02);
        Assert.AreEqual(MoveKind.Face, world2.State(near.Object)!.Intent.Kind);
    }

    [TestMethod]
    public void AStationFiresAtItsTargetAndNeverMoves()
    {
        var station = new Spec(Placed("s", 1, 0), Profile(new[] { Weapon(range: 200) }), false);
        var b = Fighter("b", 2, 100);
        var world = World(null, station, b);
        world.Attack(station.Object, b.Object);
        var events = world.Step(0.1);
        Assert.AreEqual(MoveKind.None, world.State(station.Object)!.Intent.Kind);
        Assert.IsTrue(Fired(events, "s") > 0);
    }

    [TestMethod]
    public void AFiringWeaponFollowsItsReload()
    {
        var a = Fighter("a", 1, 0, Weapon(delay: 1, range: 200));
        var b = Fighter("b", 2, 100);
        var world = World(null, a, b);
        world.Attack(a.Object, b.Object);
        Assert.AreEqual(4, Fired(world.Step(3.5), "a")); // at 0, 1, 2, and 3 seconds
    }

    [TestMethod]
    public void EachHardpointFiresAShot()
    {
        var a = Fighter("a", 1, 0, Weapon(range: 200, hardpoints: 3));
        var b = Fighter("b", 2, 100);
        var world = World(null, a, b);
        world.Attack(a.Object, b.Object);
        Assert.AreEqual(3, Fired(world.Step(0.02), "a"));
    }

    [TestMethod]
    public void ANoCrewFactorWeaponReloadsAtShotDelayAndACrewFactorOneSlowsWithTheCrew()
    {
        var weapon = Weapon(delay: 1, range: 200, crewAffects: true);
        var a = new Spec(Placed("a", 1, 0), Profile(new[] { weapon }, crew: 10, maximumCrew: 100));
        var b = Fighter("b", 2, 100);
        var world = World(null, a, b);
        world.Attack(a.Object, b.Object);
        Assert.AreEqual(2, Fired(world.Step(3.5), "a")); // crew ratio 0.1 is red: delay 3 s, shots at 0 and 3
    }

    [TestMethod]
    public void AnAttackingShipFiresAtTheNearestHostileUnitInRangeWhileItsTargetIsFar()
    {
        var a = Fighter("a", 1, 0, Weapon(range: 100));
        var target = Fighter("t", 2, 1000);
        var far = Fighter("far", 3, 90);
        var near = Fighter("near", 2, 40);
        var world = World(null, a, target, far, near);
        world.Attack(a.Object, target.Object);
        var shots = world.Step(0.02).OfType<ShotFired>().ToList();
        Assert.AreEqual(1, shots.Count);
        Assert.AreEqual("near", shots[0].Shot.Target.Unit.Label);
    }

    [TestMethod]
    public void UnitsOfOneTeamAndTeamZeroUnitsAreNeverFiredAt()
    {
        var a = Fighter("a", 1, 0, Weapon(range: 100));
        var target = Fighter("t", 2, 1000);
        var friend = Fighter("f", 1, 10);
        var neutral = Fighter("n", 0, 20);
        var world = World(null, a, target, friend, neutral);
        world.Attack(a.Object, target.Object);
        Assert.AreEqual(0, Fired(world.Step(2), "a"));
    }

    [TestMethod]
    public void AWeaponlessUnitNeverFires()
    {
        var a = Fighter("a", 1, 0);
        var b = Fighter("b", 2, 10, Weapon());
        var world = World(null, a, b);
        Assert.AreEqual(0, Fired(world.Step(2), "a"));
    }

    [TestMethod]
    public void AShotThatMissesIsReportedAndHitsNothing()
    {
        var a = Fighter("a", 1, 0, Weapon(range: 200, hitChance: 0.5f));
        var b = Fighter("b", 2, 100);
        var world = World(() => 0.9, a, b);
        world.Attack(a.Object, b.Object);
        var events = world.Step(2);
        Assert.IsFalse(events.OfType<ShotFired>().First().Shot.Hits);
        Assert.AreEqual(0, events.OfType<ShotLanded>().Count());
        Assert.AreEqual(100, world.State(b.Object)!.Shields, 1e-6);
    }

    [TestMethod]
    public void TheToHitRollAddsTheStopModifierOfAStoppedShooter()
    {
        var a = Fighter("a", 1, 0, Weapon(range: 200, hitChance: 0.5f, stopModifier: 0.2f));
        var b = Fighter("b", 2, 100);
        var world = World(() => 0.6, a, b);
        world.Attack(a.Object, b.Object);
        Assert.IsTrue(world.Step(0.02).OfType<ShotFired>().First().Shot.Hits);
    }

    [TestMethod]
    public void ABeamLandsItsDamageWhenItsLifeEnds()
    {
        var a = Fighter("a", 1, 0, Weapon(Beam(damage: 10, life: 1, threshold: 99), delay: 100, range: 200));
        var b = Fighter("b", 2, 100);
        var world = World(null, a, b);
        world.Attack(a.Object, b.Object);
        Assert.AreEqual(0, world.Step(0.9).OfType<ShotLanded>().Count());
        Assert.AreEqual(100, world.State(b.Object)!.Shields, 1e-6);
        Assert.AreEqual(1, world.Step(0.2).OfType<ShotLanded>().Count());
        Assert.AreEqual(90, world.State(b.Object)!.Shields, 1e-6);
    }

    [TestMethod]
    public void ABeamDealsNothingWhenItsShooterIsDestroyedFirst()
    {
        var a = Fighter("a", 1, 0, Weapon(Beam(life: 1), delay: 100, range: 200));
        var b = Fighter("b", 2, 100);
        var world = World(null, a, b);
        world.Attack(a.Object, b.Object);
        world.Step(0.5);
        var shooter = world.State(a.Object)!;
        shooter.Destroyed = true; // a stand-in for a hit from a third party
        var events = world.Step(1);
        Assert.AreEqual(0, events.OfType<ShotLanded>().Count());
        Assert.AreEqual(100, world.State(b.Object)!.Shields, 1e-6);
    }

    [TestMethod]
    public void ABeamDealsNothingWhenTheTargetMovesBeyondTwiceTheRange()
    {
        var a = Fighter("a", 1, 0, Weapon(Beam(life: 1), delay: 100, range: 100));
        var b = Fighter("b", 2, 50);
        var world = World(out var poses, null, a, b);
        world.Attack(a.Object, b.Object);
        world.Step(0.5);
        poses.Advance(b.Object, MovingTo(b.Object, 1000, 0), 1000);
        Assert.AreEqual(0, world.Step(1).OfType<ShotLanded>().Count());
    }

    [TestMethod]
    public void ATorpedoFliesAtItsSpeedAndHitsOnContact()
    {
        var torpedo = Beam(label: "photontorpedo", speed: 50, life: 10, threshold: 99);
        var a = Fighter("a", 1, 0, Weapon(torpedo, delay: 100, range: 200));
        var b = Fighter("b", 2, 100);
        var world = World(null, a, b);
        world.Attack(a.Object, b.Object);
        Assert.AreEqual(0, world.Step(1.9).OfType<ShotLanded>().Count()); // 95 of 100 flown
        Assert.AreEqual(1, world.Step(0.2).OfType<ShotLanded>().Count());
        Assert.AreEqual(90, world.State(b.Object)!.Shields, 1e-6);
    }

    [TestMethod]
    public void ATorpedoHitsTheShieldBubbleWhileShieldsAreUp()
    {
        // life 1.55 s at 50 per second flies 77.5 of the 100 to the center: inside the bubble, short of the hull
        var torpedo = Beam(label: "photontorpedo", speed: 50, life: 1.55f, threshold: 99);
        var a = Fighter("a", 1, 0, Weapon(torpedo, delay: 100, range: 200));
        var shielded = new Spec(Placed("b", 2, 100), Profile(maxHealth: 100), true, 20);
        var bare = new Spec(Placed("c", 2, 100), Profile(maxHealth: 0), true, 20);
        var withShields = World(null, a, shielded);
        withShields.Attack(a.Object, shielded.Object);
        Assert.AreEqual(1, withShields.Step(1.6).OfType<ShotLanded>().Count());
        var withoutShields = World(null, a, bare);
        withoutShields.Attack(a.Object, bare.Object);
        Assert.AreEqual(0, withoutShields.Step(1.6).OfType<ShotLanded>().Count());
    }

    [TestMethod]
    public void ATorpedoThatCannotReachItsTargetBeforeItsLifeEndsDealsNoDamage()
    {
        var torpedo = Beam(label: "photontorpedo", speed: 10, life: 2);
        var a = Fighter("a", 1, 0, Weapon(torpedo, delay: 100, range: 200));
        var b = Fighter("b", 2, 100);
        var world = World(null, a, b);
        world.Attack(a.Object, b.Object);
        Assert.AreEqual(0, world.Step(5).OfType<ShotLanded>().Count());
        Assert.AreEqual(100, world.State(b.Object)!.Shields, 1e-6);
        Assert.AreEqual(0, world.Shots.Count);
    }

    [TestMethod]
    public void ATorpedoSteersTowardAMovingTargetWithinItsSeekTime()
    {
        var torpedo = Beam(label: "photontorpedo", speed: 50, life: 10, omega: 3, seek: 10, threshold: 99);
        var a = Fighter("a", 1, 0, Weapon(torpedo, delay: 100, range: 300));
        var b = Fighter("b", 2, 100);
        var world = World(out var poses, null, a, b);
        world.Attack(a.Object, b.Object);
        world.Step(0.02);
        // the target jumps sideways; a torpedo that flew on straight would miss
        poses.Advance(b.Object, MovingTo(b.Object, 100, 60), 1000);
        Assert.AreEqual(1, world.Step(6).OfType<ShotLanded>().Count());
    }

    [TestMethod]
    public void APulseFliesStraightAndMissesATargetThatLeavesItsLine()
    {
        var pulse = Beam(label: "pphaser", speed: 50, life: 10);
        var a = Fighter("a", 1, 0, Weapon(pulse, delay: 100, range: 300));
        var b = Fighter("b", 2, 100);
        var world = World(out var poses, null, a, b);
        world.Attack(a.Object, b.Object);
        world.Step(0.02);
        poses.Advance(b.Object, MovingTo(b.Object, 100, 60), 1000);
        Assert.AreEqual(0, world.Step(6).OfType<ShotLanded>().Count());
    }

    [TestMethod]
    public void ACriticalHitDestroysTheTargetAndTheOrderEnds()
    {
        var a = Fighter("a", 1, 0, Weapon(Beam(damage: 200, life: 0.1f), delay: 100, range: 200));
        var b = new Spec(Placed("b", 2, 100), Profile(maxHealth: 0, hull: 0));
        var world = World(null, a, b);
        world.Attack(a.Object, b.Object);
        var events = world.Step(1);
        Assert.IsTrue(events.OfType<UnitDestroyed>().Any(e => e.Unit == b.Object));
        Assert.IsNull(world.State(b.Object));
        Assert.IsNull(world.State(a.Object)!.Order);
        Assert.AreEqual(MoveKind.Stop, world.State(a.Object)!.Intent.Kind);
    }

    [TestMethod]
    public void ADestroyedUnitIsNoLongerTargetedAndNoShotInFlightHitsIt()
    {
        var torpedo = Beam(label: "photontorpedo", speed: 20, life: 20, damage: 200);
        var a = Fighter("a", 1, 0, Weapon(torpedo, delay: 0.5f, range: 400));
        var killer = Fighter("k", 3, 400, Weapon(Beam(damage: 200, life: 0.1f), delay: 100, range: 400));
        var b = new Spec(Placed("b", 2, 200), Profile(maxHealth: 0, hull: 0));
        var world = World(null, a, b, killer);
        world.Attack(a.Object, b.Object);
        world.Attack(killer.Object, b.Object);
        var events = world.Step(10);
        Assert.AreEqual(1, events.OfType<UnitDestroyed>().Count(e => e.Unit == b.Object));
        Assert.IsFalse(world.Shots.Any(s => s.Target.Unit == b.Object));
        Assert.IsFalse(events.OfType<ShotLanded>().Any(e => e.Shot.Target.Unit == b.Object && e.Hit.Points == 0));
    }

    [TestMethod]
    public void AMoveOrderCancelsAnAttackOrder()
    {
        var a = Fighter("a", 1, 0, Weapon(range: 200));
        var b = Fighter("b", 2, 400);
        var world = World(null, a, b);
        world.Attack(a.Object, b.Object);
        world.Step(0.02);
        world.ClearOrder(a.Object);
        Assert.IsNull(world.State(a.Object)!.Order);
        Assert.AreEqual(MoveKind.None, world.State(a.Object)!.Intent.Kind);
    }

    [TestMethod]
    public void ANewAttackOrderReplacesTheOldOne()
    {
        var a = Fighter("a", 1, 0, Weapon(range: 200));
        var b = Fighter("b", 2, 400);
        var c = Fighter("c", 3, -400);
        var world = World(null, a, b, c);
        world.Attack(a.Object, b.Object);
        world.Attack(a.Object, c.Object);
        world.Step(0.02);
        Assert.AreEqual(new MoveIntent(MoveKind.MoveTo, -400, 0), world.State(a.Object)!.Intent);
    }

    [TestMethod]
    public void ShieldsRegainOverTime()
    {
        var weak = new Spec(Placed("a", 1, 0), Profile(maxHealth: 100, shieldRate: 10));
        var world = World(null, weak);
        world.State(weak.Object)!.Shields = 50;
        world.Step(2);
        Assert.AreEqual(70, world.State(weak.Object)!.Shields, 1e-6);
    }

    [TestMethod]
    public void OneLongStepFiresAndLandsTheSameShotsAsManyShortSteps()
    {
        CombatWorld Build(out PlacedObject target)
        {
            var a = Fighter("a", 1, 0, Weapon(Beam(life: 0.3f, threshold: 99), delay: 0.17f, range: 200));
            var b = Fighter("b", 2, 100, Weapon(Beam(life: 0.3f, threshold: 99), delay: 0.23f, range: 200));
            var torpedo = Beam(label: "photontorpedo", damage: 1, speed: 50, life: 10, threshold: 99);
            var pulse = Beam(label: "pphaser", damage: 1, speed: 50, life: 10, threshold: 99);
            var c = new Spec(Placed("c", 1, 0, 300), Profile(new[] { Weapon(torpedo, delay: 0.7f, range: 200) }));
            var d = new Spec(Placed("d", 2, 100, 300), Profile(new[] { Weapon(pulse, delay: 0.9f, range: 200) }));
            target = b.Object;
            var world = World(null, a, b, c, d);
            world.Attack(a.Object, b.Object);
            world.Attack(b.Object, a.Object);
            world.Attack(c.Object, d.Object);
            world.Attack(d.Object, c.Object);
            return world;
        }
        var longWorld = Build(out var t1);
        var longEvents = longWorld.Step(5);
        var shortWorld = Build(out var t2);
        var shortEvents = new List<CombatEvent>();
        for (var i = 0; i < 500; i++)
            shortEvents.AddRange(shortWorld.Step(0.01));
        Assert.AreEqual(shortEvents.OfType<ShotFired>().Count(), longEvents.OfType<ShotFired>().Count());
        Assert.AreEqual(shortEvents.OfType<ShotLanded>().Count(), longEvents.OfType<ShotLanded>().Count());
        Assert.AreEqual(shortWorld.State(t2)!.Shields, longWorld.State(t1)!.Shields, 1e-6);
        Assert.IsTrue(longEvents.OfType<ShotLanded>().Any());
        Assert.IsTrue(longEvents.OfType<ShotLanded>().Any(e => e.Shot.Kind == ShotKind.Torpedo));
        Assert.IsTrue(longEvents.OfType<ShotLanded>().Any(e => e.Shot.Kind == ShotKind.Pulse));
    }

    [TestMethod]
    public void AToHitOverrideIsFoundThroughTheTargetsDefinitionName()
    {
        var weapon = Weapon(range: 200) with
        {
            HitChance = new TargetValue(0.5f, new Dictionary<string, float> { ["b.odf"] = 1 }),
        };
        var a = new Spec(Placed("a", 1, 0), Profile(new[] { weapon }));
        var b = Fighter("b", 2, 100);
        var c = Fighter("c", 3, -100);
        var world = World(() => 0.9, a, b, c);
        world.Attack(a.Object, b.Object);
        Assert.IsTrue(world.Step(0.02).OfType<ShotFired>().First().Shot.Hits);
        world.Attack(a.Object, c.Object);
        Assert.IsFalse(world.Step(1.1).OfType<ShotFired>().First().Shot.Hits);
    }

    [TestMethod]
    public void AMovingShipIsNotStoppedForTheTicksOfAFrameAfterTheFirstOne()
    {
        // 0.5 to hit, +0.2 when the shooter is stopped, roll 0.6: only a stopped shooter hits
        var a = Fighter("a", 1, 0, Weapon(delay: 0.02f, range: 2000, hitChance: 0.5f, stopModifier: 0.2f));
        var b = Fighter("b", 2, 100);
        var world = World(out var poses, () => 0.6, a, b);
        world.Attack(a.Object, b.Object);
        world.Step(0.02);
        poses.Advance(a.Object, MovingTo(a.Object, 1000, 0), 30);
        Assert.AreNotEqual(Vector3.Zero, poses.Pose(a.Object).Position);
        var shots = world.Step(0.1).OfType<ShotFired>().ToList(); // five ticks of one frame
        Assert.AreEqual(5, shots.Count);
        Assert.IsFalse(shots.Any(s => s.Shot.Hits));
    }

    [TestMethod]
    public void AShipThatMovedOnlyBeforeTheLastStepIsStoppedAgain()
    {
        var a = Fighter("a", 1, 0, Weapon(delay: 0.02f, range: 2000, hitChance: 0.5f, stopModifier: 0.2f));
        var b = Fighter("b", 2, 100);
        var world = World(out var poses, () => 0.6, a, b);
        world.Attack(a.Object, b.Object);
        world.Step(0.02);
        poses.Advance(a.Object, MovingTo(a.Object, 1000, 0), 30);
        world.Step(0.02);
        Assert.IsTrue(world.Step(0.02).OfType<ShotFired>().First().Shot.Hits);
    }

    [TestMethod]
    public void TheFallbackTargetIsPickedWithinTheLongestRangeLessTwentyFive()
    {
        var a = Fighter("a", 1, 0, Weapon(range: 100));
        var target = Fighter("t", 2, 1000);
        var outside = Fighter("out", 2, 90); // inside the range, outside range less 25
        var world = World(null, a, target, outside);
        world.Attack(a.Object, target.Object);
        Assert.AreEqual(0, Fired(world.Step(2), "a"));
        var inside = Fighter("in", 3, 70);
        var world2 = World(null, a, target, outside, inside);
        world2.Attack(a.Object, target.Object);
        Assert.AreEqual("in", world2.Step(0.02).OfType<ShotFired>().Single().Shot.Target.Unit.Label);
    }

    [TestMethod]
    public void TheFallbackTargetIsKeptWhileItStaysValidAndInsideTheRadius()
    {
        var a = Fighter("a", 1, 0, Weapon(delay: 0.02f, range: 100));
        var far = Fighter("far", 2, 1000);
        var kept = Fighter("kept", 2, 60);
        var nearer = Fighter("nearer", 3, -70);
        var world = World(out var poses, null, a, far, kept, nearer);
        world.Attack(a.Object, far.Object);
        Assert.AreEqual("kept", world.Step(0.02).OfType<ShotFired>().First().Shot.Target.Unit.Label);
        poses.Advance(nearer.Object, MovingTo(nearer.Object, -20, 0), 1000);
        Assert.IsTrue(poses.Pose(nearer.Object).Position.X > -40);
        var labels = world.Step(0.1).OfType<ShotFired>().Select(s => s.Shot.Target.Unit.Label).ToList();
        Assert.IsTrue(labels.Count > 0);
        Assert.IsTrue(labels.All(l => l == "kept"));
    }

    [TestMethod]
    public void ABeamDealsNothingWhenItsTargetLeavesBeyondTwiceTheRangeAndComesBack()
    {
        var a = Fighter("a", 1, 0, Weapon(Beam(life: 1), delay: 100, range: 100));
        var b = Fighter("b", 2, 50);
        var world = World(out var poses, null, a, b);
        world.Attack(a.Object, b.Object);
        world.Step(0.5);
        poses.Advance(b.Object, MovingTo(b.Object, 1000, 0), 1000);
        world.Step(0.1);
        poses.Advance(b.Object, MovingTo(b.Object, 50, 0), 0.001);
        Assert.AreEqual(0, world.Step(1).OfType<ShotLanded>().Count());
    }

    [TestMethod]
    public void AWeaponWhoseDelayIsBelowATickFiresEveryShotItsDelayAllows()
    {
        var a = Fighter("a", 1, 0, Weapon(delay: 0.005f, range: 200));
        var b = Fighter("b", 2, 100);
        var world = World(null, a, b);
        world.Attack(a.Object, b.Object);
        Assert.IsTrue(Fired(world.Step(1), "a") >= 200);
        Assert.IsTrue(world.State(a.Object)!.Weapons[0].Cooldown >= -1e-6);
    }
}
