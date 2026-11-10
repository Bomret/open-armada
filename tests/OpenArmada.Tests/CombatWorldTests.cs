using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Assets;
using OpenArmada.Formats;
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
        public FlightModel? Motion { get; init; }
        public float Radius { get; init; } = 0;
        public float ModelRadius { get; init; } = 0;
        public ShieldEllipsoid? Shield { get; init; }
    }

    static Spec Fighter(string label, int team, float x, WeaponProfile? weapon = null, float shields = 100) =>
        new()
        {
            Object = Placed(label, team, x),
            Profile = Profile(weapon == null ? null : new[] { weapon }, maxHealth: shields, hull: 100),
        };

    static CombatWorld World(out UnitPoses poses, Func<double>? random = null, params Spec[] specs) =>
        World(out poses, (_, _, pose) => pose.Position, random, specs);

    static CombatWorld World(
        out UnitPoses poses,
        Func<PlacedObject, string, ShipPose, Vector3> hardpoints,
        Func<double>? random,
        params Spec[] specs
    )
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
                ModelRadius = s.ModelRadius,
                Mobile = s.Mobile,
                Motion = s.Motion,
                Shield = s.Shield,
            }),
            1,
            random ?? (() => 0),
            hardpoints
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
    public void AnAttackingShipClosesToTheLongestRangeLessFiftyThenFightsTheDogfight()
    {
        var a = Fighter("a", 1, 0, Weapon(range: 200));
        var b = Fighter("b", 2, 400);
        var world = World(out var poses, null, a, b);
        world.Attack(a.Object, b.Object);
        world.Step(0.02);
        Assert.AreEqual(
            new MoveIntent
            {
                Kind = MoveKind.MoveTo, // 400 is not beyond the replan distance: straight at the target's origin
                X = 400,
                Y = 0,
                Z = 0,
            },
            world.State(a.Object)!.Intent
        );
        // the gap of 150 is within 200 - 50
        Teleport(poses, a.Object, 250, 0);
        world.Step(0.02);
        Assert.AreEqual(
            new MoveIntent
            {
                Kind = MoveKind.Dogfight,
                X = 400,
                Y = 0,
                Z = 0,
            },
            world.State(a.Object)!.Intent
        );
    }

    [TestMethod]
    public void AnAttackingShipClosesByPathOnATargetBeyondTheReplanDistance()
    {
        var a = Fighter("a", 1, 0, Weapon(range: 200));
        var b = Fighter("b", 2, 401);
        var world = World(null, a, b);
        world.Attack(a.Object, b.Object);
        world.Step(0.02);
        Assert.AreEqual(MoveKind.Path, world.State(a.Object)!.Intent.Kind);
    }

    [TestMethod]
    public void AnAttackingShipWithALargeTargetFightsTheDogfightOnceTheGapIsWithinTheBandThenFires()
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
        // the gap of 200 (250 less the target's half radius) is beyond 200 - 50, and the hardpoint is 250 from the center
        Assert.AreEqual(0, Fired(world.Step(0.02), "a"));
        Assert.AreEqual(MoveKind.MoveTo, world.State(a.Object)!.Intent.Kind);
        Teleport(poses, a.Object, 60, 0);
        Assert.AreEqual(1, Fired(world.Step(0.02), "a"));
        Assert.AreEqual(MoveKind.Dogfight, world.State(a.Object)!.Intent.Kind);
    }

    [TestMethod]
    public void ARedAlertIdleShipFightsTheDogfightOnALargePickAndFiresOnceAHardpointIsInRange()
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
        // an idle ship fights the dogfight at its pick at once; the hardpoint is 250 from the center, out of range
        Assert.AreEqual(0, Fired(world.Step(0.02), "a"));
        Assert.AreEqual(MoveKind.Dogfight, world.State(a.Object)!.Intent.Kind);
        Teleport(poses, a.Object, 60, 0);
        Assert.AreEqual(1, Fired(world.Step(0.02), "a"));
        Assert.AreEqual(MoveKind.Dogfight, world.State(a.Object)!.Intent.Kind);
    }

    [TestMethod]
    public void AUnitThatDoesNotCloseHoldsALargePickNoHardpointReachesAndFiresNothing()
    {
        foreach (
            var (name, mobile, alert) in new[]
            {
                ("yellow ship", true, AlertStance.Yellow),
                ("station", false, AlertStance.Red),
            }
        )
        {
            var a = Fighter("a", 1, 0, Weapon(range: 200)) with { Mobile = mobile };
            var b = new Spec
            {
                Object = Placed("b", 2, 250),
                Profile = Profile(null, maxHealth: 100, hull: 100),
                Radius = 100,
            };
            var world = World(null, a, b);
            world.State(a.Object)!.Alert = alert;
            // surface distance 150 is within the search radius 200 - 25; the hardpoint is 250 from the center
            Assert.AreEqual(0, Fired(world.Step(2), "a"), name);
            Assert.AreEqual("b", world.State(a.Object)!.Target!.Unit.Label, name);
            Assert.AreEqual(MoveKind.None, world.State(a.Object)!.Intent.Kind, name);
        }
    }

    // The unit's pose driven from outside to (x, z) at its own height, its bow toward the point it came from the placed
    // position to: where a ship that has flown there leaves it.
    static void Teleport(UnitPoses poses, PlacedObject o, float x, float z)
    {
        var offset = new Vector2(x - o.Position.X, z - o.Position.Z);
        var heading = offset == Vector2.Zero ? 0 : MathF.Atan2(offset.X, offset.Y);
        poses.Record(o, FlightFixtures.Cruiser(new Vector3(x, o.Position.Y, z), heading));
    }

    // A ship of impulse speed 10 at the placed object's position, ordered to (x, z) at its own height.
    static FlightModel MovingTo(PlacedObject o, float x, float z)
    {
        var motion = FlightFixtures.Cruiser(o.Position);
        motion.StartMove([o.Position, new Vector3(x, o.Position.Y, z)]);

        return motion;
    }

    // A ship flies to a goal ahead under the world's own flight and loses its engines after 0.5667 s; the
    // frames at `fps` run for 2 s.
    static float CoastingShipZ(int fps)
    {
        var a = WithSystems("a", 1, 0, null, Sys("Engines")) with { Motion = MovingTo(Placed("a", 1, 0), 0, 1000) };
        var world = World(out var poses, null, a);
        for (var frame = 0; frame < 2 * fps; frame++)
        {
            if (frame == 17 * fps / 30) // at 0.5667 s: the world tick that takes the thrust (0.58 s) falls inside a frame
                world.State(a.Object)!.DestroySystem("Engines");
            world.Step(1.0 / fps);
        }

        return poses.Pose(a.Object).Position.Z;
    }

    [TestMethod]
    public void AShipThatLosesThrustMidFrameCoastsFromTheWorldTickAtAnyFrameRate()
    {
        var thirty = CoastingShipZ(30);
        Assert.AreEqual(thirty, CoastingShipZ(60), 1e-3);
        Assert.IsTrue(thirty > 3 && thirty < 19.9, $"z {thirty}");
    }

    [TestMethod]
    public void TheBandsGapTakesTheShipsRadiusAndHalfTheTargetsOffTheOriginDistance()
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
        world.Step(0.02); // the gap 300 - 10 - 30 = 260 is beyond 200 - 50
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
            Object = Placed("b", 2, 190),
            Profile = Profile(),
            Mobile = true,
            Radius = 20,
        }; // the gap 190 - 10 - 30 = 150
        var world2 = World(null, near, other);
        world2.Attack(near.Object, other.Object);
        world2.Step(0.02);
        Assert.AreEqual(MoveKind.Dogfight, world2.State(near.Object)!.Intent.Kind);
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
        Teleport(poses, b.Object, 40, 0);
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
        Teleport(poses, b.Object, 1000, 0);
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
    public void ATorpedoHitsATargetWithoutShieldsItPassesWithinOneStep()
    {
        // 50 per second flies one unit per tick (0x4ec2e2, 0x4ed4c0)
        var torpedo = Beam(label: "photontorpedo", speed: 50, life: 10, omega: 0, threshold: 99);
        var a = Fighter("a", 1, 0, Weapon(torpedo, delay: 100, range: 200));
        var b = Fighter("b", 2, 100, shields: 0);
        var world = World(out var poses, null, a, b);
        world.Attack(a.Object, b.Object);
        world.Step(0.02);
        // the target moves half a unit aside; the torpedo flies on straight and passes half a unit from its center
        Teleport(poses, b.Object, 100, 0.5f);
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
        // the target moves three units aside; the torpedo flies on straight and passes it farther than one step
        Teleport(poses, b.Object, 100, 3);
        Assert.AreEqual(0, world.Step(3).OfType<ShotLanded>().Count());
    }

    [TestMethod]
    public void ATorpedoHitsTheShieldEllipsoidWhileShieldsAreUp()
    {
        // life 1.55 s at 50 per second flies 77.5 of the 100 to the center: into a shield of radius 25, short of
        // the center (0x4ec27a, 0x4ed427)
        var torpedo = Beam(label: "photontorpedo", speed: 50, life: 1.55f, threshold: 99);
        var a = Fighter("a", 1, 0, Weapon(torpedo, delay: 100, range: 200));
        var shield = new ShieldEllipsoid(Vector3.Zero, new Vector3(25), Vector3.Zero);
        var shielded = new Spec
        {
            Object = Placed("b", 2, 100),
            Profile = Profile(maxHealth: 100),
            Mobile = true,
            Radius = 20,
            Shield = shield,
        };
        var bare = new Spec
        {
            Object = Placed("c", 2, 100),
            Profile = Profile(maxHealth: 0),
            Mobile = true,
            Radius = 20,
            Shield = shield,
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
        Teleport(poses, b.Object, 100, 60);
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
        Teleport(poses, b.Object, 100, 60);
        Assert.AreEqual(0, world.Step(6).OfType<ShotLanded>().Count());
    }

    [TestMethod]
    public void ACriticalHitDestroysTheTargetAfterItsDyingTimeAndTheOrderEnds()
    {
        var a = Fighter("a", 1, 0, Weapon(Beam(damage: 200, life: 0.1f), delay: 100, range: 200));
        var b = new Spec { Object = Placed("b", 2, 100), Profile = Profile(maxHealth: 0, hull: 0) };
        var world = World(null, a, b);
        world.Attack(a.Object, b.Object);
        var events = world.Step(4);
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
        var a = Fighter("a", 1, 0, Weapon(Beam(damage: 200, life: 0.1f), delay: 4, range: 200));
        var world = World(null, a, one, two);
        world.Attack(a.Object, one.Object);
        var destroyed = world.Step(3.5).OfType<UnitDestroyed>().Single();
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
                Y = 0,
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
    public void ASpentSpecialEnergyIsBackAtTheMaximumAfterTenSecondsAndNeverAbove()
    {
        var cube = new Spec
        {
            Object = Placed("cube", 1, 0),
            Profile = Profile(maxSpecialEnergy: 1000, specialEnergyRate: 10),
        };
        var world = World(null, cube);
        var state = world.State(cube.Object)!;
        state.SpecialEnergy -= 100;
        world.Step(5);
        Assert.AreEqual(950, state.SpecialEnergy, 1e-6);
        world.Step(5);
        Assert.AreEqual(1000, state.SpecialEnergy, 1e-6);
        world.Step(5);
        Assert.AreEqual(1000, state.SpecialEnergy, 1e-6);
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
        Teleport(poses, a.Object, 300, 0); // 30 seconds at 10 units per second
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
        Teleport(poses, a.Object, 300, 0); // 30 seconds at 10 units per second
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
    public void ASpecialWeaponsLongerRangeDoesNotWidenTheFallbackPick()
    {
        // the pick radius is the longest non-special range less 25 (75); the special weapon reaches 500
        var a = Fighter("a", 1, 0, Weapon(range: 100));
        var withSpecial = a with { Profile = Profile([Weapon(range: 100), Weapon(range: 500, special: true)]) };
        var target = Fighter("t", 2, 1000);
        var outside = Fighter("out", 2, 90);
        var world = World(null, withSpecial, target, outside);
        world.Attack(withSpecial.Object, target.Object);
        world.Step(0.02);
        Assert.IsNull(world.State(withSpecial.Object)!.Target);
        var inside = Fighter("in", 3, 70);
        var world2 = World(null, withSpecial, target, outside, inside);
        world2.Attack(withSpecial.Object, target.Object);
        world2.Step(0.02);
        Assert.AreEqual(inside.Object, world2.State(withSpecial.Object)!.Target!.Unit);
    }

    [TestMethod]
    public void AnOrderedTargetAtExactlyTheRangeIsNotTheWeaponsTarget()
    {
        // the game's test is strict: a surface distance under R, so at R the weapons do not take the ordered unit
        var a = Fighter("a", 1, 0, Weapon(range: 100));
        var target = Fighter("t", 2, 100);
        var world = World(null, a, target);
        world.Attack(a.Object, target.Object);
        world.Step(0.02);
        Assert.IsNull(world.State(a.Object)!.Target);
        var nearer = Fighter("t", 2, 99);
        var world2 = World(null, a, nearer);
        world2.Attack(a.Object, nearer.Object);
        world2.Step(0.02);
        Assert.AreEqual(nearer.Object, world2.State(a.Object)!.Target!.Unit);
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
        Teleport(poses, nearer.Object, -20, 0);
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
        Teleport(poses, b.Object, 1000, 0);
        world.Step(0.1);
        Teleport(poses, b.Object, 50, 0);
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
    public void AShipThatHasEnginesBackIsNoLongerToldToCoast()
    {
        var a = WithSystems("a", 1, 0, null, Sys("Engines"));
        var world = World(null, a);
        world.State(a.Object)!.DestroySystem("Engines");
        world.Step(0.02);
        Assert.IsTrue(world.State(a.Object)!.Coasting);
        world.State(a.Object)!.RestoreSystem("Engines");
        world.Step(0.02);
        Assert.IsFalse(world.State(a.Object)!.Coasting);
        Assert.AreEqual(MoveKind.Stop, world.State(a.Object)!.Intent.Kind);
    }

    [TestMethod]
    public void AStationDoesNotCoastWhenDerelict()
    {
        var a = new Spec
        {
            Object = Placed("a", 1, 0),
            Profile = Profile(),
            Mobile = false,
        };
        var world = World(null, a);
        MakeDerelict(world, a);
        world.Step(0.02);
        Assert.IsFalse(world.State(a.Object)!.Coasting);
    }

    [TestMethod]
    public void AShipWithDestroyedEnginesStopsUnderItsAttackOrderAndEveryShotAtItHits()
    {
        var a = WithSystems("a", 1, 0, Weapon(range: 500, hitChance: 0.1f), Sys("Engines"));
        var b = WithSystems("b", 2, 400, Weapon(range: 500, hitChance: 0.1f), Sys("Engines"));
        var world = World(() => 0.9, a, b);
        world.Attack(a.Object, b.Object);
        world.State(a.Object)!.DestroySystem("Engines");
        world.State(b.Object)!.DestroySystem("Engines");
        var events = world.Step(0.02);
        Assert.IsTrue(world.State(a.Object)!.Coasting);
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
        world.State(a.Object)!.DestroySystem("Weapons");
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
        world.State(a.Object)!.DestroySystem("Weapons");
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
        Assert.IsTrue(world.State(a.Object)!.Coasting);
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
        world.State(a.Object)!.DestroySystem("Engines");
        world.Step(1);
        Assert.IsTrue(world.State(a.Object)!.Coasting);
        Assert.AreEqual(MoveKind.Stop, world.State(a.Object)!.Intent.Kind);
    }

    [TestMethod]
    public void EveryShotAtADerelictShipHits()
    {
        var a = WithSystems("a", 1, 0, Weapon(range: 500, hitChance: 0));
        var b = WithSystems("b", 2, 100, null);
        var world = World(() => 0.9, a, b);
        MakeDerelict(world, b); // the order is given on a derelict, so it keeps its target
        world.Attack(a.Object, b.Object);
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
        MakeDerelict(world, b); // the order is given on a derelict, so it keeps its target
        world.Attack(a.Object, b.Object);
        var events = world.Step(4);
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
        MakeDerelict(world, b); // the order is given on a derelict, so it keeps its target
        world.Attack(a.Object, b.Object);
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
        state.DestroySystem("ShieldGenerator");
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
        state.DestroySystem("LifeSupport");
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

    // The dying phase: an attacker whose one beam is a critical hit on b (b has no systems, no hull, no shields).
    static (CombatWorld World, Spec Attacker, Spec Doomed) CriticalHitWorld(
        Func<double>? random = null,
        float bX = 100,
        bool hero = false,
        float radius = 0
    ) => CriticalHitWorld(out _, random, bX, hero, radius);

    static (CombatWorld World, Spec Attacker, Spec Doomed) CriticalHitWorld(
        out UnitPoses poses,
        Func<double>? random = null,
        float bX = 100,
        bool hero = false,
        float radius = 0
    )
    {
        var a = new Spec
        {
            Object = Placed("a", 1, 0),
            Profile = Profile(new[] { Weapon(Beam(damage: 200, life: 0.1f), delay: 100, range: 300) }),
        };
        var b = new Spec
        {
            Object = Placed("b", 2, bX),
            Profile = Profile(maxHealth: 0, hull: 0) with { IsHero = hero },
            Radius = radius,
        };
        var world = World(out poses, random, a, b);
        world.Attack(a.Object, b.Object);

        return (world, a, b);
    }

    // Steps one world tick at a time until `until` holds for the events of a tick; returns those events.
    static List<CombatEvent> TickUntil(CombatWorld world, Func<List<CombatEvent>, bool> until, int most = 5000)
    {
        for (var i = 0; i < most; i++)
        {
            var events = world.Step(CombatWorld.Tick).ToList();
            if (until(events))
                return events;
        }

        throw new AssertFailedException("the awaited event did not come");
    }

    static int Ticks(CombatWorld world, int count, List<CombatEvent>? into = null)
    {
        for (var i = 0; i < count; i++)
        {
            into?.AddRange(world.Step(CombatWorld.Tick));
            if (into == null)
                world.Step(CombatWorld.Tick);
        }

        return count;
    }

    [TestMethod]
    public void ACriticalHitLeavesTheUnitInTheWorldAndItLeavesThreeSecondsLaterWithOneUnitDestroyed()
    {
        var (world, _, b) = CriticalHitWorld();
        TickUntil(world, e => e.OfType<ShotLanded>().Any());
        var state = world.State(b.Object)!;
        Assert.IsTrue(state.Dying);
        Assert.IsFalse(state.Destroyed);
        var events = new List<CombatEvent>();
        Ticks(world, 149, events);
        Assert.IsNotNull(world.State(b.Object));
        Assert.IsFalse(state.Destroyed);
        Assert.AreEqual(0, events.OfType<UnitDestroyed>().Count());
        Ticks(world, 1, events);
        Assert.IsNull(world.State(b.Object));
        Assert.IsTrue(state.Destroyed);
        Assert.IsFalse(state.Dying);
        Ticks(world, 50, events);
        Assert.AreEqual(1, events.OfType<UnitDestroyed>().Count(e => e.Unit == b.Object));
    }

    // A doomed unit whose engines and shield generator are destroyed shows the shield-down overlay and its engines
    // damage node while it dies. Once it leaves, it is out of the units the views read their effects from, and the play
    // state reports it destroyed. The views freeing their nodes is covered only by a play run.
    [TestMethod]
    public void AUnitThatLeavesAfterItsDyingTimeIsOutOfEveryEffectAndReportedDestroyed()
    {
        var a = new Spec
        {
            Object = Placed("a", 1, 0),
            Profile = Profile(new[] { Weapon(Beam(damage: 200, life: 0.1f), delay: 100, range: 300) }),
        };
        var b = new Spec
        {
            Object = Placed("b", 2, 100),
            Profile = Profile(
                maxHealth: 0,
                hull: 0,
                systems: new[] { Sys(SystemNames.Engines, percent: 0), Sys(SystemNames.ShieldGenerator, percent: 0) }
            ),
        };
        var world = World(out var poses, null, a, b);
        var state = world.State(b.Object)!;
        state.Systems[SystemNames.Engines].Destroyed = true;
        state.Systems[SystemNames.ShieldGenerator].Destroyed = true;
        var model = new Sod
        {
            Version = 1.8f,
            Nodes =
            [
                new Sod.Node { Name = "root" },
                new Sod.Node { Name = "damage", Parent = "root" },
                new Sod.Node { Name = "engines", Parent = "damage" },
            ],
        };
        var play = new PlayState(new UnitNames([a.Object, b.Object]), poses.Pose, world.Units, _ => model);
        world.Attack(a.Object, b.Object);
        TickUntil(world, e => e.OfType<ShotLanded>().Any());
        Assert.IsTrue(state.Dying && state.ShieldDownOverlay && DamageNodes.ShowingNodes(model, state).Count > 0);
        Ticks(world, 150);
        var expect = (ExpectStep)PlayScript.Parse("expect b destroyed = true", "x.play").Steps.Single();
        Assert.IsNull(world.State(b.Object), "the world still holds a state for the unit that left");
        Assert.IsTrue(world.Units.All(u => u.Unit != b.Object), "the unit that left is still among the world's units");
        Assert.IsNull(play.Expect(expect), "expect b destroyed = true failed");
    }

    // Three attackers on one unit that no system or hull covers: their beams land in one tick, the first a critical
    // hit, the others further hits.
    static (CombatWorld World, Spec Doomed) ThreeBeamsOnOne(float damage = 200)
    {
        var specs = Enumerable
            .Range(1, 3)
            .Select(i => new Spec
            {
                Object = Placed("a" + i, 1, 0),
                Profile = Profile(new[] { Weapon(Beam(damage: damage, life: 0.1f), delay: 100, range: 300) }),
            })
            .ToList();
        var b = new Spec { Object = Placed("b", 2, 100), Profile = Profile(maxHealth: 0, hull: 0) };
        var world = World(null, specs.Append(b).ToArray());
        foreach (var a in specs)
        {
            world.Attack(a.Object, b.Object);
        }

        return (world, b);
    }

    [TestMethod]
    public void TwoFurtherDamagingHitsOnADyingUnitMakeItLeaveOneSecondEarlierWithoutDamageOrASecondUnitDestroyed()
    {
        var (world, b) = ThreeBeamsOnOne();
        var landed = TickUntil(world, e => e.OfType<ShotLanded>().Any()).OfType<ShotLanded>().ToList();
        Assert.AreEqual(3, landed.Count);
        Assert.IsTrue(landed[0].Hit.Destroyed);
        Assert.IsTrue(landed.Skip(1).All(l => l.Hit.Damage == 0 && l.Hit.Points == 0 && !l.Hit.Destroyed));
        var events = new List<CombatEvent>();
        Ticks(world, 99, events);
        Assert.IsNotNull(world.State(b.Object));
        Ticks(world, 1, events);
        Assert.IsNull(world.State(b.Object));
        Ticks(world, 200, events);
        Assert.AreEqual(1, events.OfType<UnitDestroyed>().Count());
    }

    // a (team 1) dooms b (team 2) with one critical beam; c (team 1) watches with a weapon of its own and fires its
    // one round at b at once. All shoot from x = 0.
    static (CombatWorld World, Spec Doomed, Spec Watcher) DoomedWithWatcher(Spec? bOverride = null)
    {
        var a = new Spec
        {
            Object = Placed("a", 1, 0),
            Profile = Profile(new[] { Weapon(Beam(damage: 200, life: 0.1f), delay: 100, range: 300) }),
        };
        var c = new Spec
        {
            Object = Placed("c", 1, 0),
            Profile = Profile(new[] { Weapon(Beam(damage: 0, life: 0.1f), delay: 100, range: 300) }),
        };
        var b = bOverride ?? new Spec { Object = Placed("b", 2, 100), Profile = Profile(maxHealth: 0, hull: 0) };
        var world = World(null, a, c, b);
        world.Attack(a.Object, b.Object);

        return (world, b, c);
    }

    [TestMethod]
    public void ADyingUnitIsPickedByNoAutoTargetChoiceAndItsAttackersGoIdle()
    {
        var (world, b, c) = DoomedWithWatcher();
        world.Step(CombatWorld.Tick);
        Assert.AreSame(world.State(b.Object), world.State(c.Object)!.Target); // c picks b while b lives
        TickUntil(world, e => e.OfType<ShotLanded>().Any());
        world.Step(CombatWorld.Tick);
        Assert.IsTrue(world.State(b.Object)!.Dying);
        Assert.IsNull(world.State(c.Object)!.Target);
        Assert.IsNull(world.Units.Single(u => u.Unit.Label == "a").Order);
        Assert.AreEqual(MoveKind.Stop, world.Units.Single(u => u.Unit.Label == "a").Intent.Kind);
    }

    [TestMethod]
    public void AnAttackOrderOnADyingUnitIsRefused()
    {
        var (world, b, c) = DoomedWithWatcher();
        TickUntil(world, e => e.OfType<ShotLanded>().Any());
        Assert.IsFalse(world.Attack(c.Object, b.Object));
    }

    [TestMethod]
    public void ADyingUnitFiresNothingAndItsBeamInFlightEndsWithoutDamage()
    {
        // b fires a one-second beam at a at once, and a's critical beam dooms b 0.1 s later
        var a = new Spec
        {
            Object = Placed("a", 1, 0),
            Profile = Profile(new[] { Weapon(Beam(damage: 200, life: 0.1f), delay: 100, range: 300) }, maxHealth: 100),
        };
        var b = new Spec
        {
            Object = Placed("b", 2, 100),
            Profile = Profile(
                new[] { Weapon(Beam(damage: 10, life: 1), delay: 0.2f, range: 300) },
                maxHealth: 0,
                hull: 0
            ),
        };
        var world = World(null, a, b);
        world.Attack(a.Object, b.Object);
        var events = new List<CombatEvent>();
        events.AddRange(world.Step(0.02));
        Assert.AreEqual(1, Fired(events, "b"));
        TickUntil(world, e => e.OfType<ShotLanded>().Any(l => l.Shot.Target.Unit == b.Object));
        events.Clear();
        Ticks(world, 120, events);
        Assert.AreEqual(0, Fired(events, "b"));
        Assert.AreEqual(0, events.OfType<ShotLanded>().Count(l => l.Shot.Shooter.Unit == b.Object));
        Assert.AreEqual(100, world.State(a.Object)!.Shields, 1e-6);
    }

    [TestMethod]
    public void ATorpedoOfADyingUnitInFlightStillLandsWithItsBaseDamage()
    {
        // 100 units at speed 50 take 2 s, less than the 3 s of the dying phase of a unit at rest.
        var torpedo = Beam(label: "photontorpedo", speed: 50, life: 20, damage: 10);
        var a = new Spec
        {
            Object = Placed("a", 1, 0),
            Profile = Profile(new[] { Weapon(Beam(damage: 200, life: 0.1f), delay: 100, range: 300) }, maxHealth: 100),
        };
        var b = new Spec
        {
            Object = Placed("b", 2, 100),
            Profile = Profile(new[] { Weapon(torpedo, delay: 100, range: 300) }, maxHealth: 0, hull: 0),
        };
        var world = World(null, a, b);
        world.Attack(a.Object, b.Object);
        var landed = new List<ShotLanded>();
        var shooterDyingAtLanding = false;
        for (var i = 0; i < 400; i++)
        {
            var lands = world.Step(CombatWorld.Tick).OfType<ShotLanded>().ToList();
            if (lands.Any(l => l.Shot.Shooter.Unit == b.Object))
            {
                shooterDyingAtLanding = world.State(b.Object)!.Dying;
            }

            landed.AddRange(lands);
        }

        var fromB = landed.Single(l => l.Shot.Shooter.Unit == b.Object);
        Assert.AreEqual(10, fromB.Hit.Damage, 1e-6);
        Assert.IsTrue(shooterDyingAtLanding);
    }

    [TestMethod]
    public void ADyingUnitRegainsNoShieldsRepairsNothingAndLosesNoCrew()
    {
        var b = new Spec
        {
            Object = Placed("b", 2, 100),
            Profile = Profile(
                maxHealth: 50,
                shieldRate: 10,
                hull: 0,
                lifeSupportLoss: 10,
                systems: new[] { Sys("Weapons", percent: 0), Sys("LifeSupport", percent: 0) }
            ),
        };
        var (world, _, _) = DoomedWithWatcher(b);
        TickUntil(world, e => e.OfType<ShotLanded>().Any());
        var state = world.State(b.Object)!;
        state.Systems["Weapons"].HitPoints = 1;
        state.DestroySystem("LifeSupport");
        var crew = state.Crew;
        var shields = state.Shields;
        Ticks(world, 100);
        Assert.AreEqual(shields, state.Shields, 1e-9);
        Assert.AreEqual(1, state.Systems["Weapons"].HitPoints, 1e-9);
        Assert.AreEqual(crew, state.Crew, 1e-9);
    }

    [TestMethod]
    public void ADyingShipCoastsFromTheTickAfterTheHit()
    {
        var (world, _, b) = CriticalHitWorld();
        world.Move(b.Object, 500, 0);
        TickUntil(world, e => e.OfType<ShotLanded>().Any());
        world.Step(CombatWorld.Tick);
        var state = world.State(b.Object)!;
        Assert.IsTrue(state.Immobile);
        Assert.IsTrue(state.Coasting);
        Assert.IsNull(state.PlayerMove);
        Assert.AreEqual(MoveKind.Stop, state.Intent.Kind);
    }

    [TestMethod]
    public void AUnitThatMovedAtTheCriticalHitDiesAfterFiveSeconds()
    {
        var (world, _, b) = CriticalHitWorld(out var poses);
        // the world reads the unit's speed from its position change per call, so the pose advances with every tick:
        // 10 units per second, above 1
        var x = b.Object.Position.X;
        world.Step(CombatWorld.Tick);
        List<CombatEvent> events;
        do
        {
            Teleport(poses, b.Object, x += 10 * (float)CombatWorld.Tick, 0);
            events = world.Step(CombatWorld.Tick).ToList();
        } while (!events.OfType<ShotLanded>().Any());
        Ticks(world, 249);
        Assert.IsNotNull(world.State(b.Object));
        Ticks(world, 1);
        Assert.IsNull(world.State(b.Object));
    }

    static List<SmallExplosion> SmallExplosionsOver(double frame, Func<double>? random = null, float radius = 0)
    {
        var (world, _, b) = CriticalHitWorld(random, radius: radius);
        TickUntil(world, e => e.OfType<ShotLanded>().Any());
        var found = new List<SmallExplosion>();
        for (var t = 0.0; world.State(b.Object) != null && t < 10; t += frame)
        {
            found.AddRange(world.Step(frame).OfType<SmallExplosion>());
        }

        return found;
    }

    [TestMethod]
    public void ADyingUnitHasASmallExplosionEveryFiftyMillisecondsAtAnyFrameRate()
    {
        Assert.AreEqual(60, SmallExplosionsOver(1.0 / 30).Count);
        Assert.AreEqual(60, SmallExplosionsOver(1.0 / 60).Count);
        Assert.AreEqual(60, SmallExplosionsOver(0.5).Count);
    }

    [TestMethod]
    public void ASmallExplosionIsOneOfSevenPickedAtRandomAtAPointOnTheUnit()
    {
        var low = SmallExplosionsOver(0.02, () => 0, radius: 10)[0];
        Assert.AreEqual("systemexpl1", low.Name);
        Assert.AreEqual(new Vector3(-10, -10, -10), low.Offset);
        var high = SmallExplosionsOver(0.02, () => 0.99, radius: 10)[0];
        Assert.AreEqual("systemexpl7", high.Name);
        Assert.AreEqual(new Vector3(9.8f, 9.8f, 9.8f), high.Offset);
    }

    [TestMethod]
    public void AHeroUnitDiesAfterFiveSecondsEvenWhenStopped()
    {
        var (world, _, b) = CriticalHitWorld(hero: true);
        TickUntil(world, e => e.OfType<ShotLanded>().Any());
        Ticks(world, 249);
        Assert.IsNotNull(world.State(b.Object));
        Ticks(world, 1);
        Assert.IsNull(world.State(b.Object));
    }

    [TestMethod]
    public void ADyingTimeCutBelowZeroEndsThePhaseOnTheNextTick()
    {
        var (world, _, b) = CriticalHitWorld();
        TickUntil(world, e => e.OfType<ShotLanded>().Any());
        world.State(b.Object)!.DyingRemaining = -0.3;
        Assert.IsNotNull(world.State(b.Object));
        Assert.AreEqual(1, world.Step(CombatWorld.Tick).OfType<UnitDestroyed>().Count());
        Assert.IsNull(world.State(b.Object));
    }

    // A unit whose engines are damaged by one hit point and repair too slowly to be whole within the test.
    static (CombatWorld World, Spec Unit) Sparking(
        Func<double>? random = null,
        float sparkTime = 0.5f,
        params string[] hardpoints
    )
    {
        var a = WithSystems(
            "a",
            1,
            0,
            null,
            Sys("Engines", hitPoints: 5, repairTime: 1e6f, sparkTime: sparkTime, targetHardpoints: hardpoints)
        ) with
        {
            ModelRadius = 50,
        };
        var world = World(random, a);
        world.State(a.Object)!.Systems["Engines"].HitPoints = 4;

        return (world, a);
    }

    static List<RepairSpark> SparksOver(CombatWorld world, double seconds, double frame)
    {
        var found = new List<RepairSpark>();
        var steps = (int)Math.Round(seconds / frame);
        for (var i = 0; i < steps; i++)
        {
            found.AddRange(world.Step(frame).OfType<RepairSpark>());
        }

        return found;
    }

    [TestMethod]
    public void ADamagedSystemThrowsASparkOnceItsClockPassesTheSparkTimeAtAnyFrameRate()
    {
        // the clock must pass the spark time, not reach it (0x4683d0): the first spark comes at 0.52 s, then one
        // every 0.5 s, so 2.02 s hold four
        foreach (var frame in new[] { 0.02, 1.0 / 50, 0.101, 1.01 })
        {
            var (world, _) = Sparking(null, 0.5f, "hp1");
            Assert.AreEqual(4, SparksOver(world, 2.02, frame).Count, $"frame {frame}");
        }

        var (exact, _) = Sparking(null, 0.5f, "hp1");
        Assert.AreEqual(0, SparksOver(exact, 0.5, 0.02).Count);
        Assert.AreEqual(1, SparksOver(exact, 0.02, 0.02).Count);
    }

    [TestMethod]
    public void ASparkIsAtOneOfTheSystemsTargetHardpointsPickedAtRandomAndSizedByTheModelRadius()
    {
        var (low, a) = Sparking(() => 0, 0.5f, "hp1", "hp2");
        var first = SparksOver(low, 0.52, 0.02).Single();
        Assert.AreSame(a.Object, first.Unit);
        Assert.AreEqual("Engines", first.System);
        Assert.AreEqual("hp1", first.Hardpoint);
        Assert.AreEqual(7f, first.Size, 1e-5f); // 0.14 of the model radius of 50 (0x600794)
        var (high, _) = Sparking(() => 0.99, 0.5f, "hp1", "hp2");
        Assert.AreEqual("hp2", SparksOver(high, 0.52, 0.02).Single().Hardpoint);
    }

    [TestMethod]
    public void ASystemWithoutTargetHardpointsOrAtFullHitPointsThrowsNoSpark()
    {
        var (bare, _) = Sparking();
        Assert.AreEqual(0, SparksOver(bare, 2, 0.02).Count);
        var (whole, a) = Sparking(null, 0.5f, "hp1");
        whole.State(a.Object)!.Systems["Engines"].HitPoints = 5;
        Assert.AreEqual(0, SparksOver(whole, 2, 0.02).Count);
    }

    [TestMethod]
    public void ADestroyedSystemSparksWhileItRepairs()
    {
        var (world, a) = Sparking(null, 0.5f, "hp1");
        world.State(a.Object)!.Systems["Engines"].HitPoints = 0;
        world.State(a.Object)!.DestroySystem("Engines");
        Assert.AreEqual(4, SparksOver(world, 2.02, 0.02).Count);
    }

    [TestMethod]
    public void ADerelictOrDyingUnitThrowsNoSpark()
    {
        var (derelict, a) = Sparking(null, 0.5f, "hp1");
        MakeDerelict(derelict, a);
        Assert.AreEqual(0, SparksOver(derelict, 2, 0.02).Count);
        var (dying, b) = Sparking(null, 0.5f, "hp1");
        dying.State(b.Object)!.Dying = true;
        dying.State(b.Object)!.DyingRemaining = 100;
        Assert.AreEqual(0, SparksOver(dying, 2, 0.02).Count);
    }

    [TestMethod]
    public void ASparkTimeOfZeroSparksOncePerTick()
    {
        var (world, _) = Sparking(null, 0, "hp1");
        Assert.AreEqual(10, SparksOver(world, 0.2, 0.02).Count);
    }

    // A beam of 10 from a at x 0 on b at x 100. b has no shields unless `shields` says so, and its engines take every
    // roll unless `hull` gives the roll to the hull. b's engine target hardpoint t1 sits 30 units beyond its center,
    // t2 30 units short of it, toward a.
    static (CombatWorld World, Spec Target) Hitting(
        Func<double>? random = null,
        float shields = 0,
        bool hull = false,
        float modelRadius = 50
    )
    {
        var a = new Spec
        {
            Object = Placed("a", 1, 0),
            Profile = Profile(new[] { Weapon(Beam(damage: 10, life: 0.1f), delay: 100, range: 300) }),
        };
        var engines = Sys("Engines", hitPoints: 50, percent: hull ? 0 : 100, targetHardpoints: ["t1", "t2"]);
        var b = new Spec
        {
            Object = Placed("b", 2, 100),
            Profile = Profile(maxHealth: shields, hull: hull ? 100 : 0, systems: [engines]),
            ModelRadius = modelRadius,
        };
        var world = World(
            out _,
            (_, name, pose) =>
                name switch
                {
                    "t1" => pose.Position + new Vector3(30, 0, 0),
                    "t2" => pose.Position - new Vector3(30, 0, 0),
                    _ => pose.Position,
                },
            random,
            a,
            b
        );
        world.Attack(a.Object, b.Object);

        return (world, b);
    }

    static List<HitExplosion> ExplosionsOfFirstHit(CombatWorld world) =>
        TickUntil(world, e => e.OfType<ShotLanded>().Any()).OfType<HitExplosion>().ToList();

    [TestMethod]
    public void AHitOnASystemShowsAnExplosionAtItsTargetHardpointNearestTheFiringHardpoint()
    {
        var (world, b) = Hitting();
        var explosion = ExplosionsOfFirstHit(world).Single();
        Assert.AreSame(b.Object, explosion.Unit);
        Assert.AreEqual("systemexpl1", explosion.Name);
        Assert.AreEqual("t2", explosion.Hardpoint);
        Assert.AreEqual(7f, explosion.Size, 1e-5f); // 0.14 of the model radius of 50 (0x60076c)
    }

    [TestMethod]
    public void AHitExplosionIsOneOfSevenPickedAtRandomAndAtLeastFourInSize()
    {
        // 0.99 rolls the engines too (a roll of 99 is still below their 100 percent)
        var (world, _) = Hitting(() => 0.99, modelRadius: 10);
        var explosion = ExplosionsOfFirstHit(world).Single();
        Assert.AreEqual("systemexpl7", explosion.Name);
        Assert.AreEqual(4f, explosion.Size, 1e-5f); // 1.4 is raised to the least size of 4 (0x600770)
    }

    [TestMethod]
    public void AHullHitShowsItsExplosionAtTheModelRoot()
    {
        var (world, _) = Hitting(hull: true);
        Assert.IsNull(ExplosionsOfFirstHit(world).Single().Hardpoint);
    }

    [TestMethod]
    public void AHitTheShieldsTakeWholeShowsNoExplosion()
    {
        // the world's SHIELD_DAMAGE_PROTECTION is 1, so a hit on raised shields passes nothing to the systems
        var (world, _) = Hitting(shields: 100);
        Assert.AreEqual(0, ExplosionsOfFirstHit(world).Count);
    }

    [TestMethod]
    public void ACriticalHitThatDoomsTheUnitShowsNoExplosion()
    {
        var (world, _, _) = CriticalHitWorld();
        Assert.AreEqual(0, ExplosionsOfFirstHit(world).Count);
    }

    // The world's own flight: a ship at rest facing +Z with an impulse speed of 10, at the placed object's position. The
    // placed pose starts the unit facing the other way; the flight's own heading (0) is the one it moves by.
    static FlightModel Owned(PlacedObject o) => FlightFixtures.Cruiser(o.Position);

    static PlacedObject Starting(float x, StartingState start) => Placed("b", 2, x) with { Start = start };

    // A ship with working engines owned by the world, with a shooter `a` that has an order on it.
    static (CombatWorld World, UnitPoses Poses, Spec Ship, FlightModel Motion) OwnedShip(float x = 0)
    {
        var ship = WithSystems("s", 1, x, null, Sys("Engines"));
        var motion = Owned(ship.Object);
        ship = ship with { Motion = motion };
        var world = World(out var poses, null, ship);

        return (world, poses, ship, motion);
    }

    [TestMethod]
    public void AShipThatWasNeverOrderedHasMotionFromTheStartAndAPushMovesItKeepingItsAnglesAndItHoldsAtRest()
    {
        var (world, poses, ship, motion) = OwnedShip();
        var start = poses.Pose(ship.Object);
        motion.Push(new Vector3(3, 2, 4));
        world.Step(CombatWorld.Tick);
        var pose = poses.Pose(ship.Object);
        Assert.AreEqual(start.Position + new Vector3(3, 2, 4), pose.Position);
        // the push writes no angle, and the hold at rest (0x428081) steers nothing and brakes nothing
        Assert.AreEqual(motion.Pose.Yaw, pose.Yaw);
        Assert.AreEqual(0f, pose.Roll);
        Assert.AreEqual(Vector3.Zero, world.State(ship.Object)!.Velocity);
    }

    [TestMethod]
    public void APushOnACoastingShipKeepsItsCoastVelocityAndAVelocityChangeTurnsItsPath()
    {
        var ship = Starting(0, new StartingState { DestroyedSystems = ["Engines"], Velocity = new Vector3(0, 0, 20) });
        var spec = new Spec
        {
            Object = ship,
            Profile = Profile(null, systems: [Sys("Engines")]),
            Motion = Owned(ship),
        };
        var world = World(out var poses, null, spec);
        world.Step(1);
        var state = world.State(ship)!;
        Assert.AreEqual(18, state.Speed, 0.05); // braking at 2.0 per second per second
        spec.Motion!.Push(new Vector3(100, 0, 0));
        world.Step(CombatWorld.Tick);
        Assert.AreEqual(18, state.Speed, 0.1); // 18 - 0.04, the push changed no speed
        spec.Motion.SetVelocity(new Vector3(6, 0, 0));
        world.Step(CombatWorld.Tick);
        Assert.AreEqual(6 - 2 * CombatWorld.Tick, state.Velocity.X, 1e-3);
        Assert.AreEqual(0, state.Velocity.Z, 1e-6);
        Assert.IsTrue(poses.Pose(ship).Position.X > 100);
    }

    [TestMethod]
    public void AMoveOrderEndsInTheTickThatFindsTheShipNearItsGoalAndSlowAndALaterPushNeverReopensIt()
    {
        // the goal lies 38.5 above the ship at the middle of the band: inside tooCloseToTurn 40, and the ship is at rest
        var (world, poses, ship, motion) = OwnedShip();
        world.Move(ship.Object, 0, 0.1f);
        Assert.IsNotNull(world.State(ship.Object)!.PlayerMove);
        world.Step(CombatWorld.Tick);
        Assert.IsNull(world.State(ship.Object)!.PlayerMove);
        Assert.IsNull(motion.Order);
        motion.Push(new Vector3(0, 0, 50));
        world.Step(CombatWorld.Tick);
        Assert.IsNull(world.State(ship.Object)!.PlayerMove);
        Assert.AreEqual(50, poses.Pose(ship.Object).Position.Z, 0.5);
        Assert.AreEqual(MoveKind.MoveTo, world.State(ship.Object)!.Intent.Kind); // the hold drives it back, no move order
    }

    [TestMethod]
    public void AMoveOrderGivenToAnOwnedShipDrivesItWithoutAnyOtherCaller()
    {
        var (world, poses, ship, _) = OwnedShip();
        world.Move(ship.Object, 0, 500);
        world.Step(1);
        var z = poses.Pose(ship.Object).Position.Z;
        Assert.IsTrue(z > 1 && z < 10, $"z {z}");
        Assert.IsTrue(world.State(ship.Object)!.Speed > 3);
        Assert.IsNotNull(world.State(ship.Object)!.PlayerMove);
    }

    // A world with one ship owned by the world on `motion`, and a random source that gives `draws` in turn and counts them.
    static (CombatWorld World, UnitPoses Poses, Spec Ship, Func<int> Drawn) DrawingShip(
        FlightModel? motion,
        params double[] draws
    )
    {
        var ship = WithSystems("s", 1, 0, null, Sys("Engines"));
        ship = ship with { Motion = motion };
        var drawn = 0;
        var world = World(out var poses, () => draws[drawn++ % draws.Length], ship);
        var before = drawn;

        return (world, poses, ship, () => drawn - before);
    }

    [TestMethod]
    public void EachAcceptedMoveOrderDrawsOneFacingOffsetOfAQuarterTurnTimesZeroToThreeFromTheSeededSource()
    {
        var motion = FlightFixtures.Cruiser(Vector3.Zero);
        var (world, _, ship, drawn) = DrawingShip(motion, 0.0, 0.3, 0.55, 0.99);
        var offsets = new List<float>();
        for (var i = 0; i < 4; i++)
        {
            world.Move(ship.Object, 0, 500);
            offsets.Add(motion.FacingOffset);
            Assert.AreEqual(i + 1, drawn());
        }

        CollectionAssert.AreEqual(
            new[] { 0f, MathF.PI / 2, MathF.PI, 3 * MathF.PI / 2 },
            offsets,
            "the draw is a multiple of 90 degrees, one per order"
        );
    }

    [TestMethod]
    public void AShipWithoutBorgPhysicsDrawsTheFacingOffsetToo()
    {
        // the order handler draws for every ship (0x426ad0 tests no class), and the draw shifts the rolls after it
        var motion = FlightFixtures.Cruiser(Vector3.Zero);
        Assert.IsFalse(motion.BorgPhysics);
        var (world, _, ship, drawn) = DrawingShip(motion, 0.3);
        world.Move(ship.Object, 0, 500);
        Assert.AreEqual(1, drawn());
        Assert.AreEqual(MathF.PI / 2, motion.FacingOffset);
    }

    [TestMethod]
    public void AnAttackOrderAndAUnitWithoutMotionDrawNoFacingOffset()
    {
        var motion = FlightFixtures.Cruiser(Vector3.Zero);
        var shooter = Fighter("a", 1, 0, Weapon()) with { Motion = motion };
        var target = Fighter("b", 2, 50);
        var count = 0;
        var world = World(out _, () => count++ * 0, shooter, target);
        Assert.IsTrue(world.Attack(shooter.Object, target.Object));
        Assert.AreEqual(0, count);
        Assert.AreEqual(0f, motion.FacingOffset);
        world.Move(target.Object, 0, 500); // the target owns no flight: nothing takes the order
        Assert.AreEqual(0, count);
    }

    [TestMethod]
    public void ABorgShipMovedByTheWorldTurnsTowardTheBearingPlusItsOffsetWithoutPitchOrRoll()
    {
        // the draw 0.3 is 90 degrees; the goal lies dead ahead of the ship's game yaw 0, so the yaw turns up
        var borg = FlightFixtures.Values(FlightFixtures.Fast, FlightFixtures.Fast, 50) with
        {
            BorgPhysics = true,
            TooSlowToTurn = 5,
            RotationAcceleration = 2,
            MaxRotationSpeed = 0.75,
            RotationStiffness = 1,
        };
        var motion = new FlightModel(
            borg,
            new ShipPose
            {
                Position = new Vector3(0, 38.5f, 0),
                Pitch = 0,
                Yaw = 0,
                Roll = 0,
            }
        );
        var (world, poses, ship, _) = DrawingShip(motion, 0.3);
        world.Move(ship.Object, 0, -500);
        world.Step(2);
        var pose = poses.Pose(ship.Object);
        Assert.IsTrue(pose.Yaw > 0.5, $"yaw {pose.Yaw}");
        Assert.AreEqual(0f, pose.Pitch);
        Assert.AreEqual(0f, pose.Roll);
        Assert.IsTrue(world.State(ship.Object)!.Speed > 5);
    }

    [TestMethod]
    public void AMoveOrderRisesToTheMiddleOfTheCombatBand()
    {
        var (world, poses, ship, _) = OwnedShip();
        world.Move(ship.Object, 0, 500);
        world.Step(30);
        var y = poses.Pose(ship.Object).Position.Y;
        Assert.IsTrue(y > 10 && y < 38.5f + 5, $"the ship climbs toward the plane at 38.5: {y}");
    }

    [TestMethod]
    public void ACoastingShipFasterThanOneThatIsCriticallyHitTakesTheFiveSecondTimer()
    {
        var (world, doomed) = CriticalHitWorldWithMotion(
            new StartingState { DestroyedSystems = ["Engines"], Velocity = new Vector3(0, 0, 20) }
        );
        TickUntil(world, e => e.OfType<ShotLanded>().Any());
        Assert.IsTrue(world.State(doomed)!.Dying);
        Assert.AreEqual(5, world.State(doomed)!.DyingRemaining, 1e-9);
    }

    [TestMethod]
    public void AShipPushedWhileStillKeepsTheThreeSecondTimerAndTheStopToHitModifier()
    {
        var (world, doomed) = CriticalHitWorldWithMotion(null);
        var motion = world.State(doomed)!.Motion!;
        // pushed 24 units over the ticks before the hit (the beam lands 0.1 s after it fires), always at rest and
        // within the 25 units of its tether point that the hold lets it stay
        for (var i = 0; i < 3; i++)
        {
            motion.Push(new Vector3(0, 0, 8));
            world.Step(CombatWorld.Tick);
            Assert.IsTrue(world.State(doomed)!.Stopped);
            Assert.AreEqual(0, world.State(doomed)!.Speed);
        }

        TickUntil(world, e => e.OfType<ShotLanded>().Any());
        Assert.AreEqual(3, world.State(doomed)!.DyingRemaining, 1e-9);
    }

    [TestMethod]
    public void AShooterThatIsPushedWhileStillHasTheStopToHitModifier()
    {
        var a = Fighter("a", 1, 0, Weapon(range: 200, hitChance: 0.5f, stopModifier: 0.2f));
        a = a with { Motion = Owned(a.Object) };
        var b = Fighter("b", 2, 100);
        var world = World(() => 0.6, a, b);
        world.Attack(a.Object, b.Object);
        a.Motion!.Push(new Vector3(0, 0, 5));
        Assert.IsTrue(world.Step(CombatWorld.Tick).OfType<ShotFired>().First().Shot.Hits);
    }

    [TestMethod]
    public void AShooterThatFliesHasNoStopToHitModifierAndOneAtRestHasIt()
    {
        // 0.5 to hit, +0.2 when the shooter is stopped, roll 0.6: only a stopped shooter hits
        var a = Fighter("a", 1, 0, Weapon(delay: 0.02f, range: 2000, hitChance: 0.5f, stopModifier: 0.2f));
        a = a with { Motion = Owned(a.Object) };
        var b = Fighter("b", 2, 100);
        var world = World(() => 0.6, a, b);
        Assert.IsTrue(world.Step(CombatWorld.Tick).OfType<ShotFired>().First().Shot.Hits, "at rest");
        world.Move(a.Object, 0, 1000);
        world.Step(1);
        Assert.IsTrue(world.State(a.Object)!.Speed > 3);
        var shots = world.Step(0.1).OfType<ShotFired>().ToList();
        Assert.AreEqual(5, shots.Count);
        Assert.IsFalse(shots.Any(s => s.Shot.Hits), "flying: the physics velocity is not 0");
    }

    [TestMethod]
    public void AShipThatFliesAtTheCriticalHitDiesAfterFiveSecondsAndOneAtRestAfterThree()
    {
        var (flying, doomed) = CriticalHitWorldWithMotion(null);
        flying.Move(doomed, 0, 1000);
        TickUntil(flying, e => e.OfType<ShotLanded>().Any());
        Assert.IsTrue(flying.State(doomed)!.Speed > 1);
        Assert.AreEqual(5, flying.State(doomed)!.DyingRemaining, 1e-9);

        var (resting, still) = CriticalHitWorldWithMotion(null);
        TickUntil(resting, e => e.OfType<ShotLanded>().Any());
        Assert.AreEqual(3, resting.State(still)!.DyingRemaining, 1e-9);
    }

    [TestMethod]
    public void AMoveOrderAimsAtTheMiddleOfTheWorldsCombatBand()
    {
        var ship = WithSystems("s", 1, 0, null, Sys("Engines"));
        var motion = Owned(ship.Object);
        ship = ship with { Motion = motion };
        var world = new CombatWorld(
            new UnitPoses([ship.Object]),
            [
                new CombatUnit
                {
                    Unit = ship.Object,
                    Profile = ship.Profile,
                    DefinitionName = "s.odf",
                    Radius = 0,
                    Mobile = true,
                    Motion = motion,
                },
            ],
            1,
            () => 0,
            (_, _, pose) => pose.Position
        )
        {
            Band = new CombatBand(100, 200),
        };
        world.Move(ship.Object, 0, 500);
        Assert.AreEqual(150, motion.Order!.Goal.Y);
        Assert.AreEqual(new Vector3(0, 150, 500), motion.Order.Goal);
    }

    [TestMethod]
    public void TwoRunsOfAFlyingShipAtOneFrameRateAreIdenticalAndTheTickPosesDoNotDependOnIt()
    {
        static (Vector3, Vector3, ShipPose) Run(int fps)
        {
            var (world, poses, ship, _) = OwnedShip();
            world.Move(ship.Object, 300, 700);
            for (var i = 0; i < 3 * fps; i++)
            {
                world.Step(1.0 / fps);
            }

            return (poses.Pose(ship.Object).Position, world.State(ship.Object)!.Velocity, poses.Pose(ship.Object));
        }

        Assert.AreEqual(Run(30), Run(30));
        var (thirty, sixty) = (Run(30), Run(60));
        Assert.IsTrue(Vector3.Distance(thirty.Item1, sixty.Item1) < 1e-3f, $"{thirty.Item1} {sixty.Item1}");
        Assert.AreEqual(thirty.Item3.Roll, sixty.Item3.Roll, 1e-4);
    }

    [TestMethod]
    public void ADerelictShipWithAStartingVelocityCoastsFromTheFirstTick()
    {
        var ship = Starting(0, new StartingState { Crew = 0, Velocity = new Vector3(0, 0, 10) });
        var spec = new Spec
        {
            Object = ship,
            Profile = Profile(null),
            Motion = Owned(ship),
        };
        var world = World(out var poses, null, spec);
        world.Step(CombatWorld.Tick);
        Assert.AreEqual(10 - 2 * CombatWorld.Tick, world.State(ship)!.Velocity.Z, 1e-3);
        Assert.IsTrue(poses.Pose(ship).Position.Z > 0.19f);
    }

    [TestMethod]
    public void AStartingVelocityOnAShipWithThrustLeavesItAtRest()
    {
        var ship = Starting(0, new StartingState { Velocity = new Vector3(0, 0, 10) });
        var spec = new Spec
        {
            Object = ship,
            Profile = Profile(null, systems: [Sys("Engines")]),
            Motion = Owned(ship),
        };
        var world = World(out var poses, null, spec);
        world.Step(1);
        Assert.AreEqual(Vector3.Zero, world.State(ship)!.Velocity);
        Assert.AreEqual(ship.Position, poses.Pose(ship).Position);
    }

    [TestMethod]
    public void TwoRunsOfACoastingShipAtOneFrameRateAreIdentical()
    {
        static (Vector3, Vector3) Run()
        {
            var ship = Starting(
                0,
                new StartingState { DestroyedSystems = ["Engines"], Velocity = new Vector3(1, 2, 3) }
            );
            var spec = new Spec
            {
                Object = ship,
                Profile = Profile(null, systems: [Sys("Engines")]),
                Motion = Owned(ship),
            };
            var world = World(out var poses, null, spec);
            for (var i = 0; i < 120; i++)
                world.Step(1.0 / 30);

            return (poses.Pose(ship).Position, world.State(ship)!.Velocity);
        }

        Assert.AreEqual(Run(), Run());
    }

    // A critical hit on b, whose own motion the world owns; b starts as `start` says.
    static (CombatWorld World, PlacedObject Doomed) CriticalHitWorldWithMotion(StartingState? start)
    {
        var a = new Spec
        {
            Object = Placed("a", 1, 0),
            Profile = Profile(new[] { Weapon(Beam(damage: 200, life: 0.1f), delay: 100, range: 300) }),
        };
        var doomed = Placed("b", 2, 100) with { Start = start };
        var b = new Spec
        {
            Object = doomed,
            // the engines are a system only where the start destroys them: a system would share the hit locations
            Profile = Profile(maxHealth: 0, hull: 0, systems: start == null ? [] : [Sys("Engines")]),
            Motion = Owned(doomed),
        };
        var world = World(null, a, b);
        world.Attack(a.Object, b.Object);

        return (world, doomed);
    }

    [TestMethod]
    public void EveryShipWhoseStepGoesNonFiniteIsReportedOnceNotOnlyTheFirst()
    {
        var a = WithSystems("a", 1, 0, null, Sys("Engines"));
        var b = WithSystems("b", 1, 500, null, Sys("Engines"));
        var (motionA, motionB) = (Owned(a.Object), Owned(b.Object));
        var world = World(null, a with { Motion = motionA }, b with { Motion = motionB });
        using var log = new CapturedLog();
        for (var i = 0; i < 3; i++)
        {
            motionA.SetVelocity(new Vector3(float.NaN, 0, 0));
            motionB.SetVelocity(new Vector3(float.NaN, 0, 0));
            world.Step(CombatWorld.Tick);
        }

        Assert.AreEqual(1, log.Lines.Count(l => l.Contains("flight of a failed")), log.Text);
        Assert.AreEqual(1, log.Lines.Count(l => l.Contains("flight of b failed")), log.Text);
    }

    [TestMethod]
    public void AFaultInAShipsProcessIsReportedOnceNotAsAFlightFaultAndTheOtherShipsKeepFlying()
    {
        var a = WithSystems("a", 1, 0, null, Sys("Engines"));
        var b = WithSystems("b", 1, 500, null, Sys("Engines"));
        var (motionA, motionB) = (Owned(a.Object), Owned(b.Object));
        var world = World(out var poses, null, a with { Motion = motionA }, b with { Motion = motionB });
        world.Move(a.Object, 0, 500);
        world.Move(b.Object, 500, 500);
        motionA.Avoidance = (_, _, _) => throw new ArgumentException("the seam broke");
        using var log = new CapturedLog();
        var before = poses.Pose(b.Object).Position;
        for (var i = 0; i < 50; i++)
            world.Step(CombatWorld.Tick);

        Assert.AreEqual(1, log.Lines.Count(l => l.Contains("the seam broke")), log.Text);
        Assert.IsFalse(log.Lines.Any(l => l.Contains("flight of a failed")), log.Text);
        Assert.AreNotEqual(before, poses.Pose(b.Object).Position);
    }

    [TestMethod]
    public void AProcessThatThrowsADifferentMessageEveryTickIsReportedOnceAndItsShipsStepStillRuns()
    {
        var a = WithSystems("a", 1, 0, null, Sys("Engines"));
        var motionA = Owned(a.Object);
        var world = World(out var poses, null, a with { Motion = motionA });
        world.Move(a.Object, 0, 500);
        var ticks = 0;
        motionA.Avoidance = (_, _, _) => throw new ArgumentException($"the seam broke at {++ticks}");
        motionA.SetVelocity(new Vector3(0, 0, 5));
        using var log = new CapturedLog();
        var before = poses.Pose(a.Object).Position;
        for (var i = 0; i < 20; i++)
            world.Step(CombatWorld.Tick);

        Assert.IsTrue(ticks > 1, "the process ran on more than one tick");
        Assert.AreEqual(1, log.Lines.Count(l => l.Contains("process of a failed")), log.Text);
        Assert.AreNotEqual(before, poses.Pose(a.Object).Position);
    }

    // A race whose values differ from the fixture race in every value the rules read.
    static readonly RaceValues OtherRace = Race with
    {
        CrewYellowStatus = 0.9f,
        CrewRedStatus = 0.6f,
        WeaponYellow = 5,
        WeaponRed = 7,
        RepairYellow = 4,
        RepairRed = 8,
    };

    // x holds team 1 at 0, y team 2 at 40, z team 2 at 80, all armed and at red alert; the weapons reach 100.
    static (CombatWorld World, Spec X, Spec Y, Spec Z) ThreeFighters()
    {
        var x = Fighter("x", 1, 0, Weapon());
        var y = Fighter("y", 2, 40, Weapon());
        var z = Fighter("z", 2, 80, Weapon());

        return (World(null, x, y, z), x, y, z);
    }

    [TestMethod]
    public void AfterATeamChangeTheFormerTeammatesPickTheUnitAndTheNewTeammatesDoNot()
    {
        var (world, x, y, z) = ThreeFighters();
        world.Step(0.02);
        Assert.AreEqual(y.Object, world.State(x.Object)!.Target?.Unit);
        world.ChangeTeam(y.Object, 1, "", Race);
        world.Step(0.02);
        Assert.AreEqual(y.Object, world.State(z.Object)!.Target?.Unit, "a former teammate picks it");
        Assert.AreNotEqual(y.Object, world.State(x.Object)!.Target?.Unit, "a new teammate does not");
    }

    [TestMethod]
    public void ATeamChangedUnitsWeaponsStopFiringAtItsNewTeammatesAndFireAtItsNewEnemies()
    {
        var (world, x, y, z) = ThreeFighters();
        world.ChangeTeam(y.Object, 1, "", Race);
        var events = world.Step(0.02);
        Assert.AreEqual(
            0,
            events.OfType<ShotFired>().Count(e => e.Shot.Shooter.Unit == y.Object && e.Shot.Target.Unit == x.Object)
        );
        Assert.AreEqual(z.Object, world.State(y.Object)!.Target?.Unit);
    }

    [TestMethod]
    public void AfterATeamChangeCrewStatusReloadAndRepairUseTheNewRace()
    {
        var a = WithSystems(
            "a",
            1,
            0,
            Weapon(delay: 2, crewAffects: true),
            Sys("Weapons", hitPoints: 2, repairTime: 2)
        );
        var world = World(null, a);
        var state = world.State(a.Object)!;
        state.Crew = 40; // ratio 0.4: yellow by the fixture race, red by the other
        Assert.AreEqual(CrewStatus.Yellow, CombatRules.CrewStatusOf(state));
        world.ChangeTeam(a.Object, 3, "", OtherRace);
        Assert.AreEqual(CrewStatus.Red, CombatRules.CrewStatusOf(state));
        Assert.AreEqual(2 * 7, CombatRules.ReloadDelay(state.Weapons[0].Profile, state), 1e-9);
        state.Systems["Weapons"].HitPoints = 0;
        CombatRules.Repair(state, 1);
        Assert.AreEqual(1.0 / 8, state.Systems["Weapons"].HitPoints, 1e-9);
    }

    [TestMethod]
    public void ATeamChangeStopsTheOrderKeepsTheAlertStanceAndTheUnitsPlace()
    {
        var a = Fighter("a", 1, 0, Weapon(range: 500));
        var b = Fighter("b", 2, 300);
        var world = World(out var poses, null, a, b);
        var state = world.State(a.Object)!;
        state.Alert = AlertStance.Yellow;
        world.Attack(a.Object, b.Object);
        world.Step(0.02);
        var pose = poses.Pose(a.Object);
        var readout = CombatReadout.Line("a", state);
        world.ChangeTeam(a.Object, 3, "", OtherRace);
        Assert.IsTrue(world.Units.Contains(state), "the unit stays among the units the selection and readout read");
        Assert.AreEqual(readout, CombatReadout.Line("a", state), "the readout line is the same");
        Assert.IsNull(state.Order);
        Assert.IsFalse(state.AutoMove);
        Assert.IsNull(state.PlayerMove);
        Assert.AreEqual(AlertStance.Yellow, state.Alert);
        Assert.AreEqual(3, state.Team);
        Assert.AreEqual(state.Position ?? default, state.Tether);
        Assert.AreSame(state, world.State(a.Object), "the unit keeps its entry");
        Assert.AreEqual(pose, poses.Pose(a.Object));
    }

    [TestMethod]
    public void AnAttackOrderKeepsItsTargetWhenTheTargetMovesFromOneHostileTeamToAnother()
    {
        // the order stores the diplomacy value, enemy, not the team number (0x468cd0, 0x42fb90)
        var a = Fighter("a", 1, 0, Weapon(range: 500));
        var b = Fighter("b", 2, 300);
        var world = World(null, a, b);
        world.Attack(a.Object, b.Object);
        world.ChangeTeam(b.Object, 3, "", OtherRace);
        world.Step(0.02);
        Assert.AreSame(world.State(b.Object), world.State(a.Object)!.Order);
    }

    [TestMethod]
    public void TwoUnitsHostileByTheirCurrentTeamsAreHostileAndAUnitTheWorldLostIsHostileToNone()
    {
        var a = Fighter("a", 1, 0);
        var b = Fighter("b", 2, 10);
        var world = World(null, a, b);
        Assert.IsTrue(world.Hostile(a.Object, b.Object));
        world.ChangeTeam(b.Object, 1, "", Race);
        Assert.IsFalse(world.Hostile(a.Object, b.Object));
        Assert.IsFalse(world.Hostile(a.Object, Placed("gone", 2, 0)));
    }

    [TestMethod]
    public void TwoUnitsOfOneModelAndPlacedTeamKeepSeparateEntriesWhenOneChangesTeam()
    {
        var a = Fighter("a", 1, 0);
        var b = Fighter("b", 1, 10);
        var world = World(out var poses, null, a, b);
        world.ChangeTeam(a.Object, 2, "", OtherRace);
        Assert.AreEqual(2, world.State(a.Object)!.Team);
        Assert.AreEqual(1, world.State(b.Object)!.Team);
        Assert.AreNotSame(world.State(a.Object), world.State(b.Object));
        Assert.AreNotEqual(poses.Pose(a.Object), poses.Pose(b.Object));
    }

    [TestMethod]
    public void AnAttackOrderOnALiveHostileUnitDropsTheTargetWhenItTurnsDerelict()
    {
        var a = Fighter("a", 1, 0, Weapon(range: 500));
        var b = Fighter("b", 2, 300);
        var world = World(null, a, b);
        world.Attack(a.Object, b.Object);
        world.State(b.Object)!.Derelict = true;
        world.Step(0.02);
        Assert.IsNull(world.State(a.Object)!.Order);
    }

    [TestMethod]
    public void AnAttackOrderDropsAnOutOfRangeTargetThatTurnsDerelict()
    {
        var a = Fighter("a", 1, 0, Weapon(range: 100));
        var b = Fighter("b", 2, 5000);
        var world = World(null, a, b);
        world.Attack(a.Object, b.Object);
        world.Step(0.02);
        Assert.AreSame(world.State(b.Object), world.State(a.Object)!.Order);
        world.State(b.Object)!.Derelict = true;
        world.Step(0.02);
        Assert.IsNull(world.State(a.Object)!.Order);
    }

    [TestMethod]
    public void AnAttackOrderDropsTheTargetWhenItChangesTeamToTheAttackersTeam()
    {
        var a = Fighter("a", 1, 0, Weapon(range: 500));
        var b = Fighter("b", 2, 300);
        var world = World(null, a, b);
        world.Attack(a.Object, b.Object);
        world.ChangeTeam(b.Object, 1, "", Race);
        world.Step(0.02);
        Assert.IsNull(world.State(a.Object)!.Order);
    }

    [TestMethod]
    public void AnAttackOrderGivenOnADerelictKeepsItsTargetWhileItStaysDerelict()
    {
        var a = Fighter("a", 1, 0, Weapon(range: 500));
        var b = Fighter("b", 2, 300);
        var world = World(null, a, b);
        world.State(b.Object)!.Derelict = true;
        Assert.IsTrue(world.Attack(a.Object, b.Object));
        world.Step(0.5);
        Assert.AreSame(world.State(b.Object), world.State(a.Object)!.Order);
    }

    [TestMethod]
    public void AMoveOrdersTetherPointLiesAtTheGoalsHeightTheMiddleOfTheBand()
    {
        var (world, _, ship, _) = OwnedShip();
        Assert.AreNotEqual(world.Band.Middle, ship.Object.Position.Y);
        world.Move(ship.Object, 300, 400);
        Assert.AreEqual(new Vector3(300, world.Band.Middle, 400), world.State(ship.Object)!.Tether);
    }
}
