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
    sealed record Spec
    {
        public required PlacedObject Object { get; init; }
        public required CombatProfile Profile { get; init; }
        public bool Mobile { get; init; } = true;
        public float Radius { get; init; } = 0;
    }

    static Spec Fighter(string label, int team, float x, WeaponProfile? weapon = null, float shields = 100) =>
        new()
        {
            Object = Placed(label, team, x),
            Profile = Profile(weapon == null ? null : new[] { weapon }, maxHealth: shields, hull: 100),
        };

    static CombatWorld World(out UnitPoses poses, Func<double>? random = null, params Spec[] specs)
    {
        poses = new UnitPoses(specs.Select(s => s.Object));

        return new CombatWorld(
            poses,
            specs.Select(s => new CombatUnit
            {
                Unit = s.Object,
                Profile = s.Profile,
                DefinitionName = s.Object.Label + ".odf",
                Radius = s.Radius,
                Mobile = s.Mobile,
            }),
            1,
            random ?? (() => 0),
            (_, _, pose) => pose.Position
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
        Assert.AreEqual(
            new MoveIntent
            {
                Kind = MoveKind.MoveTo,
                X = 400,
                Z = 0,
            },
            world.State(a.Object)!.Intent
        );
        // surface distance 150 is within 200 - 50
        poses.Advance(a.Object, MovingTo(a.Object, 250, 0), 1000);
        world.Step(0.02);
        Assert.AreEqual(
            new MoveIntent
            {
                Kind = MoveKind.Face,
                X = 400,
                Z = 0,
            },
            world.State(a.Object)!.Intent
        );
    }

    [TestMethod]
    public void AnAttackingShipKeepsClosingOnALargeTargetUntilAHardpointIsInRangeThenFires()
    {
        var a = Fighter("a", 1, 0, Weapon(range: 200));
        var b = new Spec
        {
            Object = Placed("b", 2, 250),
            Profile = Profile(null, maxHealth: 100, hull: 100),
            Radius = 100,
        };
        var world = World(out var poses, null, a, b);
        world.Attack(a.Object, b.Object);
        // surface distance 150 is within 200 - 50, but the hardpoint is 250 from the center, out of range
        Assert.AreEqual(0, Fired(world.Step(0.02), "a"));
        Assert.AreEqual(MoveKind.MoveTo, world.State(a.Object)!.Intent.Kind);
        poses.Advance(a.Object, MovingTo(a.Object, 60, 0), 1000);
        Assert.AreEqual(1, Fired(world.Step(0.02), "a"));
        Assert.AreEqual(MoveKind.Face, world.State(a.Object)!.Intent.Kind);
    }

    [TestMethod]
    public void ARedAlertIdleShipKeepsClosingOnALargeTargetUntilAHardpointIsInRangeThenFires()
    {
        var a = Fighter("a", 1, 0, Weapon(range: 200));
        var b = new Spec
        {
            Object = Placed("b", 2, 250),
            Profile = Profile(null, maxHealth: 100, hull: 100),
            Radius = 100,
        };
        var world = World(out var poses, null, a, b);
        world.State(a.Object)!.Alert = AlertStance.Red;
        // surface distance 150 is within 200 - 50, but the hardpoint is 250 from the center, out of range
        Assert.AreEqual(0, Fired(world.Step(0.02), "a"));
        Assert.AreEqual(MoveKind.MoveTo, world.State(a.Object)!.Intent.Kind);
        poses.Advance(a.Object, MovingTo(a.Object, 60, 0), 1000);
        Assert.AreEqual(1, Fired(world.Step(0.02), "a"));
        Assert.AreEqual(MoveKind.Face, world.State(a.Object)!.Intent.Kind);
    }

    [TestMethod]
    public void ARearHardpointShipHoldsWithoutFlippingOnceAHardpointIsInRange()
    {
        // The hardpoint trails the center by 80: surface distance allows holding from x = 150, the hardpoint is in
        // range of the target's center from x = 180.
        var a = Fighter("a", 1, 0, OnHardpoints(200, "rear"));
        var b = Fighter("b", 2, 300);
        var poses = new UnitPoses(new[] { a.Object, b.Object });
        var world = new CombatWorld(
            poses,
            new[] { a, b }.Select(s => new CombatUnit
            {
                Unit = s.Object,
                Profile = s.Profile,
                DefinitionName = s.Object.Label + ".odf",
                Radius = s.Radius,
                Mobile = s.Mobile,
            }),
            1,
            () => 0,
            // the hardpoint sits 80 behind the center (the bow is node-local -Z); the placed ships have identity axes,
            // so the pose's orientation is its heading change from there
            hardpointPosition: (_, _, pose) =>
                pose.Position
                + Vector3.Transform(
                    new Vector3(0, 0, 80),
                    MoveGeometry.Orientation(
                        Quaternion.Identity,
                        pose.Heading - MoveGeometry.BowHeading(Vector3.UnitZ),
                        pose.Bank
                    )
                )
        );
        world.Attack(a.Object, b.Object);
        var kinds = new List<MoveKind>();
        var fired = new List<int>();
        foreach (var x in new float[] { 150, 170, 179, 180, 181, 190, 180, 181 })
        {
            poses.Advance(a.Object, MovingTo(a.Object, x, 0), 1000);
            fired.Add(Fired(world.Step(0.02), "a"));
            kinds.Add(world.State(a.Object)!.Intent.Kind);
        }

        CollectionAssert.AreEqual(
            new[]
            {
                MoveKind.MoveTo,
                MoveKind.MoveTo,
                MoveKind.MoveTo,
                MoveKind.Face,
                MoveKind.Face,
                MoveKind.Face,
                MoveKind.Face,
                MoveKind.Face,
            },
            kinds
        );
        Assert.AreEqual(0, fired.Take(3).Sum());
        Assert.AreEqual(1, fired.Skip(3).Sum());
    }

    static ShipMotion MovingTo(PlacedObject o, float x, float z)
    {
        var motion = new ShipMotion(
            new PhysicsValues
            {
                ImpulseSpeed = 10,
                YawRotationRate = 1,
                MaxRoll = null,
            },
            o.Position,
            0
        );
        motion.SetTarget(x, z);

        return motion;
    }

    [TestMethod]
    public void TheClosingRangeIsMeasuredSurfaceToSurface()
    {
        var a = new Spec
        {
            Object = Placed("a", 1, 0),
            Profile = Profile(new[] { Weapon(range: 200) }),
            Mobile = true,
            Radius = 30,
        };
        var b = new Spec
        {
            Object = Placed("b", 2, 300),
            Profile = Profile(),
            Mobile = true,
            Radius = 20,
        };
        var world = World(null, a, b);
        world.Attack(a.Object, b.Object);
        world.Step(0.02); // surface distance 250 is beyond 150
        Assert.AreEqual(MoveKind.MoveTo, world.State(a.Object)!.Intent.Kind);
        var near = new Spec
        {
            Object = Placed("a", 1, 0),
            Profile = Profile(new[] { Weapon(range: 200) }),
            Mobile = true,
            Radius = 30,
        };
        var other = new Spec
        {
            Object = Placed("b", 2, 200),
            Profile = Profile(),
            Mobile = true,
            Radius = 20,
        }; // surface distance 150
        var world2 = World(null, near, other);
        world2.Attack(near.Object, other.Object);
        world2.Step(0.02);
        Assert.AreEqual(MoveKind.Face, world2.State(near.Object)!.Intent.Kind);
    }

    [TestMethod]
    public void AStationFiresAtItsTargetAndNeverMoves()
    {
        var station = new Spec
        {
            Object = Placed("s", 1, 0),
            Profile = Profile(new[] { Weapon(range: 200) }),
            Mobile = false,
        };
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
    public void AWeaponWithThreeHardpointsFiresOneShotPerReload()
    {
        var a = Fighter("a", 1, 0, Weapon(range: 200, hardpoints: 3));
        var b = Fighter("b", 2, 100);
        var world = World(null, a, b);
        world.Attack(a.Object, b.Object);
        Assert.AreEqual(1, Fired(world.Step(0.02), "a"));
    }

    // A world whose hardpoints stand at the given x offsets from their unit's center, by name.
    static CombatWorld WorldWithHardpoints(Dictionary<string, float> offsets, params Spec[] specs)
    {
        var poses = new UnitPoses(specs.Select(s => s.Object));

        return new CombatWorld(
            poses,
            specs.Select(s => new CombatUnit
            {
                Unit = s.Object,
                Profile = s.Profile,
                DefinitionName = s.Object.Label + ".odf",
                Radius = s.Radius,
                Mobile = s.Mobile,
            }),
            1,
            () => 0,
            hardpointPosition: (_, name, pose) => pose.Position + new Vector3(offsets.GetValueOrDefault(name), 0, 0)
        );
    }

    static string[] Origins(CombatWorld world, double seconds) =>
        world.Step(seconds).OfType<ShotFired>().Select(e => e.Shot.Hardpoint).ToArray();

    static WeaponProfile OnHardpoints(float range, params string[] names) =>
        Weapon(range: range) with
        {
            Hardpoints = names,
        };

    [TestMethod]
    public void OfTwoHardpointsInRangeTheNearerFires()
    {
        var a = Fighter("a", 1, 0, OnHardpoints(50, "far", "near"));
        var b = Fighter("b", 2, 40);
        var world = WorldWithHardpoints(new() { ["far"] = -5, ["near"] = 20 }, a, b);
        world.Attack(a.Object, b.Object);
        CollectionAssert.AreEqual(new[] { "near" }, Origins(world, 0.02));
    }

    [TestMethod]
    public void AShotStartsAtTheChosenHardpointsWorldPosition()
    {
        var a = Fighter("a", 1, 0, OnHardpoints(50, "far", "near"));
        var b = Fighter("b", 2, 40);
        var world = WorldWithHardpoints(new() { ["far"] = -5, ["near"] = 20 }, a, b);
        world.Attack(a.Object, b.Object);
        var shot = world.Step(0.02).OfType<ShotFired>().Single().Shot;
        Assert.AreEqual(new System.Numerics.Vector3(20, 0, 0), shot.Position);
    }

    [TestMethod]
    public void OnEqualDistanceTheFirstListedHardpointFires()
    {
        var a = Fighter("a", 1, 0, OnHardpoints(50, "listedFirst", "listedSecond"));
        var b = Fighter("b", 2, 40);
        var world = WorldWithHardpoints(new() { ["listedFirst"] = 10, ["listedSecond"] = 70 }, a, b);
        world.Attack(a.Object, b.Object);
        CollectionAssert.AreEqual(new[] { "listedFirst" }, Origins(world, 0.02));
    }

    [TestMethod]
    public void AHardpointJustOutOfRangeNeverFires()
    {
        var a = Fighter("a", 1, 0, OnHardpoints(50, "hp"));
        var b = Fighter("b", 2, 40);
        var world = WorldWithHardpoints(new() { ["hp"] = -10.1f }, a, b);
        world.Attack(a.Object, b.Object);
        Assert.AreEqual(0, Origins(world, 1).Length);
    }

    [TestMethod]
    public void AWeaponIsJudgedByItsOwnRangeSoOnlyTheLongOneFires()
    {
        var shortGun = Weapon(range: 40);
        var longGun = Weapon(range: 200) with { Slot = 2, Hardpoints = new[] { "hp1" } };
        var a = new Spec
        {
            Object = Placed("a", 1, 0),
            Profile = Profile(new[] { shortGun, longGun }, maxHealth: 100, hull: 100),
        };
        var b = Fighter("b", 2, 100);
        var world = World(null, a, b);
        world.Attack(a.Object, b.Object);
        var fired = world.Step(0.02).OfType<ShotFired>().Select(e => e.Shot.Weapon.Slot).ToArray();
        CollectionAssert.AreEqual(new[] { 2 }, fired);
    }

    [TestMethod]
    public void ARangeIsMeasuredToTheTargetsCenterNotItsSurface()
    {
        var a = Fighter("a", 1, 0, Weapon(range: 90));
        var b = new Spec
        {
            Object = Placed("b", 2, 100),
            Profile = Profile(null, maxHealth: 100, hull: 100),
            Radius = 30,
        };
        var world = World(null, a, b);
        world.Attack(a.Object, b.Object);
        Assert.AreEqual(0, Fired(world.Step(1), "a"));
    }

    [TestMethod]
    public void AWeaponOutOfRangeStaysReadyAndFiresOnTheFirstTickAHardpointComesInRange()
    {
        var a = Fighter("a", 1, 0, Weapon(range: 50)) with { Mobile = false };
        var b = Fighter("b", 2, 100);
        var world = World(out var poses, null, a, b);
        world.Attack(a.Object, b.Object);
        Assert.AreEqual(0, Fired(world.Step(1), "a"));
        poses.Advance(b.Object, MovingTo(b.Object, 40, 0), 1000);
        Assert.AreEqual(1, Fired(world.Step(0.02), "a"));
    }

    [TestMethod]
    public void ARoundOutOfRangeFiresNothingWhenSeveralRoundsFallInOneTick()
    {
        var a = Fighter("a", 1, 0, Weapon(delay: 0.001f, range: 50));
        var b = Fighter("b", 2, 100);
        var world = World(null, a, b);
        world.Attack(a.Object, b.Object);
        Assert.AreEqual(0, Fired(world.Step(0.02), "a"));
    }

    [TestMethod]
    public void AWeaponWithoutHardpointsFiresNoShot()
    {
        var a = Fighter("a", 1, 0, Weapon(range: 200, hardpoints: 0));
        var b = Fighter("b", 2, 100);
        var world = World(null, a, b);
        world.Attack(a.Object, b.Object);
        Assert.AreEqual(0, Fired(world.Step(1), "a"));
    }

    [TestMethod]
    public void TheShotNamesTheHardpointItLeavesFrom()
    {
        var weapon = Weapon(range: 200) with { Hardpoints = new[] { "hp01", "hp02" } };
        var a = Fighter("a", 1, 0, weapon);
        var b = Fighter("b", 2, 100);
        var world = World(null, a, b); // both hardpoints at the center: a tie, the first listed fires
        world.Attack(a.Object, b.Object);
        CollectionAssert.AreEqual(new[] { "hp01" }, Origins(world, 0.02));
    }

    [TestMethod]
    public void ANoCrewFactorWeaponReloadsAtShotDelayAndACrewFactorOneSlowsWithTheCrew()
    {
        var weapon = Weapon(delay: 1, range: 200, crewAffects: true);
        var a = new Spec
        {
            Object = Placed("a", 1, 0),
            Profile = Profile(new[] { weapon }, crew: 10, maximumCrew: 100),
        };
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
    public void ATorpedoHitsAZeroRadiusTargetItPassesClosely()
    {
        var torpedo = Beam(label: "photontorpedo", speed: 50, life: 10, omega: 0, threshold: 99);
        var a = Fighter("a", 1, 0, Weapon(torpedo, delay: 100, range: 200));
        var b = Fighter("b", 2, 100, shields: 0); // radius 0, as a unit whose model has no meshes
        var world = World(out var poses, null, a, b);
        world.Attack(a.Object, b.Object);
        world.Step(0.02);
        // the target moves one unit aside; the torpedo flies on straight and passes one unit from its center
        poses.Advance(b.Object, MovingTo(b.Object, 100, 1), 1000);
        Assert.AreEqual(1, world.Step(3).OfType<ShotLanded>().Count());
    }

    [TestMethod]
    public void ATorpedoMissesASmallTargetItPassesBeyondItsRadius()
    {
        var torpedo = Beam(label: "photontorpedo", speed: 50, life: 10, omega: 0, threshold: 99);
        var a = Fighter("a", 1, 0, Weapon(torpedo, delay: 100, range: 200));
        var b = new Spec
        {
            Object = Placed("b", 2, 100),
            Profile = Profile(maxHealth: 0, hull: 100),
            Mobile = true,
            Radius = 2,
        };
        var world = World(out var poses, null, a, b);
        world.Attack(a.Object, b.Object);
        world.Step(0.02);
        // the target moves three units aside; the torpedo flies on straight and passes between its radius and 5
        poses.Advance(b.Object, MovingTo(b.Object, 100, 3), 1000);
        Assert.AreEqual(0, world.Step(3).OfType<ShotLanded>().Count());
    }

    [TestMethod]
    public void ATorpedoHitsTheShieldBubbleWhileShieldsAreUp()
    {
        // life 1.55 s at 50 per second flies 77.5 of the 100 to the center: inside the bubble, short of the hull
        var torpedo = Beam(label: "photontorpedo", speed: 50, life: 1.55f, threshold: 99);
        var a = Fighter("a", 1, 0, Weapon(torpedo, delay: 100, range: 200));
        var shielded = new Spec
        {
            Object = Placed("b", 2, 100),
            Profile = Profile(maxHealth: 100),
            Mobile = true,
            Radius = 20,
        };
        var bare = new Spec
        {
            Object = Placed("c", 2, 100),
            Profile = Profile(maxHealth: 0),
            Mobile = true,
            Radius = 20,
        };
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
        var b = new Spec { Object = Placed("b", 2, 100), Profile = Profile(maxHealth: 0, hull: 0) };
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
        var b = new Spec { Object = Placed("b", 2, 200), Profile = Profile(maxHealth: 0, hull: 0) };
        var world = World(null, a, b, killer);
        world.Attack(a.Object, b.Object);
        world.Attack(killer.Object, b.Object);
        var events = world.Step(10);
        Assert.AreEqual(1, events.OfType<UnitDestroyed>().Count(e => e.Unit == b.Object));
        Assert.IsFalse(world.Shots.Any(s => s.Target.Unit == b.Object));
        Assert.IsFalse(events.OfType<ShotLanded>().Any(e => e.Shot.Target.Unit == b.Object && e.Hit.Points == 0));
    }

    // Two units of identical fields on one spot, on a team hostile to `a`. Tell them apart by reference only:
    // they share a label.
    static (Spec One, Spec Two) Twins(CombatProfile profile) =>
        (
            new Spec { Object = Placed("t", 2, 100), Profile = profile },
            new Spec { Object = Placed("t", 2, 100), Profile = profile }
        );

    [TestMethod]
    public void TwoIdenticalUnitsAreTwoUnitsOfTheWorld()
    {
        var (one, two) = Twins(Profile());
        var world = World(null, Fighter("a", 1, 0, Weapon()), one, two);
        Assert.AreEqual(3, world.Units.Count());
        Assert.AreNotSame(world.State(one.Object), world.State(two.Object));
        Assert.IsTrue(world.Units.Any(u => ReferenceEquals(u.Unit, two.Object)));
    }

    [TestMethod]
    public void DamagingOneOfTwoIdenticalUnitsLeavesTheOtherUntouched()
    {
        var (one, two) = Twins(Profile(maxHealth: 100, systems: new[] { Sys("Engines") }));
        var a = Fighter("a", 1, 0, Weapon(Beam(damage: 10, life: 0.1f), delay: 100, range: 200));
        var world = World(null, a, one, two);
        world.Attack(a.Object, one.Object);
        world.Step(1);
        Assert.AreEqual(90, world.State(one.Object)!.Shields, 1e-6);
        var other = world.State(two.Object)!;
        Assert.AreEqual(100, other.Shields, 1e-6);
        Assert.AreEqual(100, other.Crew, 1e-6);
        Assert.AreEqual(other.Systems["Engines"].Max, other.Systems["Engines"].HitPoints, 1e-6);
    }

    [TestMethod]
    public void DestroyingOneOfTwoIdenticalUnitsLeavesTheOtherInTheFight()
    {
        var (one, two) = Twins(Profile(maxHealth: 0, hull: 0));
        var a = Fighter("a", 1, 0, Weapon(Beam(damage: 200, life: 0.1f), delay: 1, range: 200));
        var world = World(null, a, one, two);
        world.Attack(a.Object, one.Object);
        var destroyed = world.Step(0.5).OfType<UnitDestroyed>().Single();
        Assert.AreSame(one.Object, destroyed.Unit);
        Assert.IsNull(world.State(one.Object));
        Assert.IsNotNull(world.State(two.Object));
        Assert.IsTrue(world.Units.Any(u => ReferenceEquals(u.Unit, two.Object)));
        Assert.IsTrue(world.Attack(a.Object, two.Object));
        Assert.IsTrue(world.Step(2).OfType<ShotLanded>().Any(e => ReferenceEquals(e.Shot.Target.Unit, two.Object)));
    }

    [TestMethod]
    public void AMoveOrderCancelsAnAttackOrder()
    {
        var a = Fighter("a", 1, 0, Weapon(range: 200));
        var b = Fighter("b", 2, 400);
        var world = World(null, a, b);
        world.Attack(a.Object, b.Object);
        world.Step(0.02);
        world.Move(a.Object, -500, 0);
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
        Assert.AreEqual(
            new MoveIntent
            {
                Kind = MoveKind.MoveTo,
                X = -400,
                Z = 0,
            },
            world.State(a.Object)!.Intent
        );
    }

    [TestMethod]
    public void ShieldsRegainOverTime()
    {
        var weak = new Spec { Object = Placed("a", 1, 0), Profile = Profile(maxHealth: 100, shieldRate: 10) };
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
            var c = new Spec
            {
                Object = Placed("c", 1, 0, 300),
                Profile = Profile(new[] { Weapon(torpedo, delay: 0.7f, range: 200) }),
            };
            var d = new Spec
            {
                Object = Placed("d", 2, 100, 300),
                Profile = Profile(new[] { Weapon(pulse, delay: 0.9f, range: 200) }),
            };
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
        {
            shortEvents.AddRange(shortWorld.Step(0.01));
        }

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
            HitChance = new TargetValue
            {
                Default = 0.5f,
                Overrides = new Dictionary<string, float> { ["b.odf"] = 1 },
            },
        };
        var a = new Spec { Object = Placed("a", 1, 0), Profile = Profile(new[] { weapon }) };
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
        Assert.AreEqual(201, Fired(world.Step(1), "a")); // shots at 0, d, 2d ... for d = 0.005 over 1 s
        Assert.IsTrue(world.State(a.Object)!.Weapons[0].Cooldown >= -1e-6);
    }

    static Spec WithSystems(string label, int team, float x, WeaponProfile? weapon, params SystemProfile[] systems) =>
        new()
        {
            Object = Placed(label, team, x),
            Profile = Profile(weapon == null ? null : new[] { weapon }, maxHealth: 100, hull: 100, systems: systems),
        };

    [TestMethod]
    public void AShipWithDestroyedEnginesStopsUnderItsAttackOrderAndEveryShotAtItHits()
    {
        var a = WithSystems("a", 1, 0, Weapon(range: 500, hitChance: 0.1f), Sys("Engines"));
        var b = WithSystems("b", 2, 400, Weapon(range: 500, hitChance: 0.1f), Sys("Engines"));
        var world = World(() => 0.9, a, b);
        world.Attack(a.Object, b.Object);
        world.State(a.Object)!.Systems["Engines"].Destroyed = true;
        world.State(b.Object)!.Systems["Engines"].Destroyed = true;
        var events = world.Step(0.02);
        Assert.AreEqual(MoveKind.Stop, world.State(a.Object)!.Intent.Kind);
        Assert.IsTrue(events.OfType<ShotFired>().Where(e => e.Shot.Target.Unit == b.Object).All(e => e.Shot.Hits));
        Assert.IsTrue(events.OfType<ShotFired>().Any(e => e.Shot.Target.Unit == b.Object));
    }

    [TestMethod]
    public void AShipWithWorkingEnginesIsMissedByALowRoll()
    {
        var a = WithSystems("a", 1, 0, Weapon(range: 500, hitChance: 0.1f));
        var b = WithSystems("b", 2, 400, null, Sys("Engines"));
        var world = World(() => 0.9, a, b);
        world.Attack(a.Object, b.Object);
        Assert.IsFalse(world.Step(0.02).OfType<ShotFired>().First().Shot.Hits);
    }

    [TestMethod]
    public void AShipWithDestroyedWeaponsFiresNothing()
    {
        var a = WithSystems("a", 1, 0, Weapon(range: 500), Sys("Weapons"));
        var b = WithSystems("b", 2, 100, null);
        var world = World(null, a, b);
        world.Attack(a.Object, b.Object);
        world.State(a.Object)!.Systems["Weapons"].Destroyed = true;
        Assert.AreEqual(0, Fired(world.Step(3), "a"));
    }

    [TestMethod]
    public void AShipWhoseWeaponsAreRepairedFiresAgain()
    {
        var a = WithSystems("a", 1, 0, Weapon(range: 500, delay: 0.5f), Sys("Weapons", hitPoints: 2, repairTime: 2));
        var b = WithSystems("b", 2, 100, null);
        var world = World(null, a, b);
        world.Attack(a.Object, b.Object);
        var weapons = world.State(a.Object)!.Systems["Weapons"];
        weapons.Destroyed = true;
        weapons.HitPoints = 0;
        Assert.AreEqual(0, Fired(world.Step(1), "a"));
        Assert.IsTrue(Fired(world.Step(3), "a") > 0); // 1 s per point at crew ratio 1: whole again after 2 s
    }

    static void MakeDerelict(CombatWorld world, Spec spec)
    {
        var state = world.State(spec.Object)!;
        state.Derelict = true;
        state.Crew = 0;
    }

    [TestMethod]
    public void ADerelictShipNeitherMovesNorFiresAndStaysOnTheMap()
    {
        var a = WithSystems("a", 1, 0, Weapon(range: 500));
        var b = WithSystems("b", 2, 100, null);
        var world = World(null, a, b);
        world.Attack(a.Object, b.Object);
        MakeDerelict(world, a);
        var events = world.Step(3);
        Assert.AreEqual(0, Fired(events, "a"));
        Assert.AreEqual(MoveKind.Stop, world.State(a.Object)!.Intent.Kind);
        Assert.IsNotNull(world.State(a.Object));
    }

    [TestMethod]
    public void AHitThatDestroysASystemAndTurnsTheUnitDerelictBringsItsShieldsDown()
    {
        var a = WithSystems("a", 1, 0, Weapon(Beam(damage: 4), range: 500));
        var b = WithSystems("b", 2, 100, null, Sys("Weapons", hitPoints: 1, crewLoss: 100));
        var poses = new UnitPoses(new[] { a.Object, b.Object });
        // shield damage protection 0.5: half the damage reaches the systems while the shields stay up
        var world = new CombatWorld(
            poses,
            new[] { a, b }.Select(s => new CombatUnit
            {
                Unit = s.Object,
                Profile = s.Profile,
                DefinitionName = s.Object.Label + ".odf",
                Radius = 0,
                Mobile = true,
            }),
            0.5,
            () => 0,
            (_, _, pose) => pose.Position
        );
        var state = world.State(b.Object)!;
        state.Crew = 1;
        state.Shields = 40;
        world.Attack(a.Object, b.Object);
        world.Step(1.5);
        Assert.IsTrue(state.Derelict);
        Assert.AreEqual(0, state.Shields, 1e-6);
    }

    [TestMethod]
    public void AShipWithDestroyedEnginesIgnoresAMoveIntentFromAnAttackOrder()
    {
        var a = WithSystems("a", 1, 0, Weapon(range: 100), Sys("Engines"));
        var b = WithSystems("b", 2, 400, null);
        var world = World(null, a, b);
        world.Attack(a.Object, b.Object);
        world.State(a.Object)!.Systems["Engines"].Destroyed = true;
        world.Step(1);
        Assert.AreEqual(MoveKind.Stop, world.State(a.Object)!.Intent.Kind);
    }

    [TestMethod]
    public void EveryShotAtADerelictShipHits()
    {
        var a = WithSystems("a", 1, 0, Weapon(range: 500, hitChance: 0));
        var b = WithSystems("b", 2, 100, null);
        var world = World(() => 0.9, a, b);
        world.Attack(a.Object, b.Object);
        MakeDerelict(world, b);
        Assert.IsTrue(world.Step(0.02).OfType<ShotFired>().First().Shot.Hits);
    }

    [TestMethod]
    public void AnAttackOrderOnADerelictShipStaysUntilACriticalHitDestroysIt()
    {
        var a = new Spec
        {
            Object = Placed("a", 1, 0),
            Profile = Profile(new[] { Weapon(Beam(damage: 1, life: 0.1f), delay: 0.5f, range: 500) }),
        };
        var b = new Spec { Object = Placed("b", 2, 100), Profile = Profile(hull: 0, maxHealth: 0) }; // every roll is a critical hit
        var world = World(null, a, b);
        world.Attack(a.Object, b.Object);
        MakeDerelict(world, b);
        var events = world.Step(1);
        Assert.IsTrue(events.OfType<UnitDestroyed>().Any(e => e.Unit == b.Object));
        Assert.IsNull(world.State(b.Object));
        Assert.IsNull(world.State(a.Object)!.Order);
    }

    [TestMethod]
    public void ADerelictShipSurvivesHitsThatAreNotCritical()
    {
        var a = new Spec
        {
            Object = Placed("a", 1, 0),
            Profile = Profile(new[] { Weapon(Beam(damage: 1, life: 0.1f), delay: 0.5f, range: 500) }),
        };
        var b = new Spec { Object = Placed("b", 2, 100), Profile = Profile(hull: 100, maxHealth: 0) };
        var world = World(null, a, b);
        world.Attack(a.Object, b.Object);
        MakeDerelict(world, b);
        world.Step(3);
        Assert.IsNotNull(world.State(b.Object));
        Assert.AreSame(world.State(b.Object), world.State(a.Object)!.Order);
    }

    [TestMethod]
    public void ADestroyedShieldGeneratorKeepsTheShieldsWhereTheyAre()
    {
        var a = new Spec
        {
            Object = Placed("a", 1, 0),
            Profile = Profile(maxHealth: 100, shieldRate: 10, systems: new[] { Sys("ShieldGenerator") }),
        };
        var world = World(null, a);
        var state = world.State(a.Object)!;
        state.Shields = 40;
        state.Systems["ShieldGenerator"].Destroyed = true;
        world.Step(2);
        Assert.AreEqual(40, state.Shields, 1e-6);
    }

    static (CombatWorld World, UnitCombatState State) Ailing()
    {
        var a = new Spec
        {
            Object = Placed("a", 1, 0),
            Profile = Profile(
                crew: 100,
                maximumCrew: 100,
                lifeSupportLoss: 5,
                systems: new[] { Sys("LifeSupport", 4, repairTime: 40), Sys("Sensors", 4, repairTime: 8) }
            ),
        };
        var world = World(null, a);
        var state = world.State(a.Object)!;
        state.Systems["LifeSupport"].Destroyed = true;
        state.Systems["LifeSupport"].HitPoints = 0;
        state.Systems["Sensors"].HitPoints = 1;

        return (world, state);
    }

    [TestMethod]
    public void DestroyedLifeSupportCostsCrewEachSecondUntilTheUnitIsDerelict()
    {
        var (world, state) = Ailing();
        world.Step(1);
        Assert.IsTrue(state.Crew < 100 && state.Crew > 80);
        world.Step(300);
        Assert.IsTrue(state.Derelict);
        Assert.AreEqual(0, state.Crew, 1e-9);
        Assert.IsNotNull(world.State(state.Unit));
    }

    [TestMethod]
    public void SteppingOnceOverASpanRepairsAndLosesCrewAsSteppingManyTimesDoes()
    {
        var (once, onceState) = Ailing();
        var (many, manyState) = Ailing();
        once.Step(6);
        for (var i = 0; i < 300; i++)
        {
            many.Step(0.02);
        }

        Assert.AreEqual(onceState.Crew, manyState.Crew, 1e-6);
        Assert.AreEqual(onceState.Systems["Sensors"].HitPoints, manyState.Systems["Sensors"].HitPoints, 1e-6);
        Assert.AreEqual(onceState.Systems["LifeSupport"].HitPoints, manyState.Systems["LifeSupport"].HitPoints, 1e-6);
        Assert.IsTrue(onceState.Systems["Sensors"].HitPoints > 1);
    }

    [TestMethod]
    public void TheSystemHitPointsBonusReachesEveryUnitsSystems()
    {
        var a = new Spec { Object = Placed("a", 1, 0), Profile = Profile(systems: new[] { Sys("Engines", 3) }) };
        var world = new CombatWorld(
            new UnitPoses(new[] { a.Object }),
            new[]
            {
                new CombatUnit
                {
                    Unit = a.Object,
                    Profile = a.Profile,
                    DefinitionName = "a.odf",
                    Radius = 0,
                    Mobile = true,
                },
            },
            1,
            () => 0,
            (_, _, pose) => pose.Position,
            2
        );
        Assert.AreEqual(5, world.State(a.Object)!.Systems["Engines"].Max, 1e-6);
    }
}
