using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Formats;
using OpenArmada.Sandbox;
using static OpenArmada.Tests.CombatFixtures;

namespace OpenArmada.Tests;

// The black hole of the combat world: its pull on ships without thrust (0x450a80), the push-out of ships with thrust
// from its ring (0x4644b0), and its event horizon (0x44da40, 0x450a40). Ships have a model radius of 10 and an
// impulse speed of 10; the world ticks of 0.02 s.
[TestClass]
public class BlackHoleTests
{
    static readonly StartingState NoEngines = new() { DestroyedSystems = ["Engines"] };
    static readonly StartingState NoCrew = new() { Crew = 0 };

    sealed class Rig
    {
        public required CombatWorld World { get; init; }
        public required UnitPoses Poses { get; init; }
        public required IReadOnlyList<PlacedObject> Units { get; init; }

        public PlacedObject Unit => Units[0];

        public Vector3 At => Poses.Pose(Unit).Position;

        public UnitCombatState State => World.State(Unit)!;

        public List<CombatEvent> Events { get; } = [];

        public void Tick(int count = 1)
        {
            for (var i = 0; i < count; i++)
            {
                Events.AddRange(World.Step(CombatWorld.Tick));
            }
        }
    }

    static BlackHole Hole(
        Vector3 center,
        float gravityRadius = 100,
        float eventRadius = 5,
        float force = 0.5f,
        float rotation = 0.02f
    ) =>
        new()
        {
            Object = Placed("hole", 0, center.X, center.Z),
            Center = center,
            GravityRadius = gravityRadius,
            EventRadius = eventRadius,
            GravityForce = force,
            Rotation = rotation,
        };

    static ShipMotion Motion(Vector3 at) =>
        new(
            new PhysicsValues
            {
                ImpulseSpeed = 10,
                YawRotationRate = 1,
                MaxRoll = null,
            },
            at,
            0
        );

    // The first unit is a ship at `at` (starting as `start`); `others` are placed too: a station when its Mobile flag is
    // false. Units without a motion take their pose from the poses, so they stand where they are placed.
    static Rig Build(
        Vector3 at,
        StartingState? start,
        BlackHole[] holes,
        Sod? model = null,
        params (Vector3 At, bool Mobile)[] others
    )
    {
        var units = new List<PlacedObject>();
        var combat = new List<CombatUnit>();
        void Add(Vector3 position, bool mobile, StartingState? s, Sod? m)
        {
            var unit = Placed($"u{units.Count}", 1, position.X, position.Z) with { Position = position, Start = s };
            units.Add(unit);
            combat.Add(
                new CombatUnit
                {
                    Unit = unit,
                    Profile = Profile(systems: [Sys("Engines")]),
                    DefinitionName = "u.odf",
                    Radius = 0,
                    ModelRadius = 10,
                    Mobile = mobile,
                    Motion = mobile ? Motion(position) : null,
                    Model = m,
                }
            );
        }

        Add(at, true, start, model);
        foreach (var (position, mobile) in others)
        {
            Add(position, mobile, null, null);
        }

        var poses = new UnitPoses(units);
        var world = new CombatWorld(poses, combat, 1, new Random(7).NextDouble, (_, _, pose) => pose.Position)
        {
            BlackHoles = holes,
        };

        return new Rig
        {
            World = world,
            Poses = poses,
            Units = units,
        };
    }

    static Rig Build(Vector3 at, StartingState? start, params BlackHole[] holes) => Build(at, start, holes, null);

    [TestMethod]
    public void AShipWithDestroyedEnginesInsideTheGravityRadiusIsPulledToTheCenterFromRest()
    {
        // F = 0.5 x 100^2 / 50^2 = 2; the change of one tick is 2 x 0.02 toward the center
        var rig = Build(new Vector3(0, 0, 50), NoEngines, Hole(Vector3.Zero));
        rig.Tick();
        Assert.AreEqual(0f, rig.State.Velocity.X, 1e-6);
        Assert.AreEqual(0f, rig.State.Velocity.Y, 1e-6);
        Assert.AreEqual(-0.04f, rig.State.Velocity.Z, 1e-6);
    }

    [TestMethod]
    public void ThePullPointsAlongTheThreeDimensionalDirectionToTheCenter()
    {
        // the ship is 30 beside and 40 above the center: d = 50, F = 2, the direction (-0.6, -0.8, 0)
        var rig = Build(new Vector3(30, 40, 0), NoEngines, Hole(Vector3.Zero));
        rig.Tick();
        Assert.AreEqual(-0.024f, rig.State.Velocity.X, 1e-6);
        Assert.AreEqual(-0.032f, rig.State.Velocity.Y, 1e-6);
    }

    [TestMethod]
    public void AShipOutsideTheGravityRadiusIsNotPulled()
    {
        var rig = Build(new Vector3(0, 0, 100.5f), NoEngines, Hole(Vector3.Zero));
        rig.Tick(5);
        Assert.AreEqual(Vector3.Zero, rig.State.Velocity);
    }

    [TestMethod]
    public void ADerelictShipIsPulledToo()
    {
        var rig = Build(new Vector3(0, 0, 50), NoCrew, Hole(Vector3.Zero));
        rig.Tick();
        Assert.AreEqual(-0.04f, rig.State.Velocity.Z, 1e-6);
    }

    [TestMethod]
    public void AShipWithThrustIsNotPulled()
    {
        var rig = Build(new Vector3(0, 0, 50), null, Hole(Vector3.Zero));
        rig.Tick(50);
        Assert.AreEqual(new Vector3(0, 0, 50), rig.At);
        Assert.AreEqual(Vector3.Zero, rig.State.Velocity);
    }

    [TestMethod]
    public void AShipFallsToTheCenterAndItsSpeedNeverExceedsTheImpulseSpeed()
    {
        // a strong hole: F = 450 at the edge of its 100 radius, so one tick adds far more than the cap
        var rig = Build(new Vector3(0, 0, 60), NoEngines, Hole(Vector3.Zero, force: 450, eventRadius: 0));
        var fastest = 0.0;
        for (var i = 0; i < 100; i++)
        {
            rig.Tick();
            fastest = Math.Max(fastest, rig.State.Speed);
        }

        Assert.AreEqual(10.0, fastest, 1e-4);
        Assert.IsTrue(rig.At.Z < 60, $"the ship is at {rig.At.Z}");
    }

    [TestMethod]
    public void TheCurveOfAShipPassingTheHoleBendsTowardIt()
    {
        // coasts along +X at 5, 40 beside the hole's line: the pull gives it a velocity toward the hole (-Z)
        var rig = Build(
            new Vector3(-30, 0, 40),
            new StartingState { DestroyedSystems = ["Engines"], Velocity = new Vector3(5, 0, 0) },
            Hole(new Vector3(0, 0, 0), eventRadius: 0)
        );
        rig.Tick(25);
        Assert.IsTrue(rig.State.Velocity.Z < 0, $"velocity {rig.State.Velocity}");
        Assert.IsTrue(rig.At.Z < 40, $"at {rig.At}");
    }

    [TestMethod]
    public void TwoHolesPullsAdd()
    {
        // each pull is F = 2 at 50: one along +X from the origin, one along +Z
        var rig = Build(
            Vector3.Zero,
            NoEngines,
            Hole(new Vector3(50, 0, 0), eventRadius: 0),
            Hole(new Vector3(0, 0, 50), eventRadius: 0)
        );
        rig.Tick();
        Assert.AreEqual(0.04f, rig.State.Velocity.X, 1e-6);
        Assert.AreEqual(0.04f, rig.State.Velocity.Z, 1e-6);
    }

    [TestMethod]
    public void ADyingShipWithDestroyedEnginesIsPulledAndNotSwallowed()
    {
        var rig = Build(new Vector3(0, 0, 3), NoEngines, Hole(Vector3.Zero)); // inside the event radius of 5
        rig.State.Dying = true;
        rig.State.DyingRemaining = 1000;
        rig.Tick(3);
        Assert.IsTrue(rig.State.Velocity.Z < 0, $"velocity {rig.State.Velocity}");
        Assert.IsTrue(rig.State.Dying);
        Assert.AreEqual(0, rig.Events.OfType<UnitDestroyed>().Count());
    }

    [TestMethod]
    public void ADyingShipWithWorkingEnginesIsNotPulled()
    {
        // the game's gate (0x450bcc) is engines destroyed or derelict; dying is no part of it
        var rig = Build(new Vector3(0, 0, 50), null, Hole(Vector3.Zero));
        rig.State.Dying = true;
        rig.State.DyingRemaining = 1000;
        rig.Tick(5);
        Assert.AreEqual(Vector3.Zero, rig.State.Velocity);
    }

    [TestMethod]
    public void ADyingDerelictShipIsPulled()
    {
        var rig = Build(new Vector3(0, 0, 50), NoCrew, Hole(Vector3.Zero));
        rig.State.Dying = true;
        rig.State.DyingRemaining = 1000;
        rig.Tick();
        Assert.AreEqual(-0.04f, rig.State.Velocity.Z, 1e-6);
    }

    [TestMethod]
    public void AHoleWithAnEventRadiusOfZeroSwallowsNothingNotEvenAUnitAtItsCenter()
    {
        var rig = Build(Vector3.Zero, null, Hole(Vector3.Zero, eventRadius: 0, force: 0));
        rig.Tick(3);
        Assert.IsFalse(rig.State.Dying);
        Assert.AreEqual(0, rig.Events.OfType<UnitDestroyed>().Count());
    }

    [TestMethod]
    public void AnExtremeGravityForceOrRadiusKeepsTheVelocityFinite()
    {
        foreach (var hole in new[] { Hole(Vector3.Zero, 1e5f, 0, 1e30f), Hole(Vector3.Zero, 1e30f, 0, 1e30f) })
        {
            var rig = Build(new Vector3(0, 0, 50), NoEngines, hole);
            rig.Tick(3);
            var v = rig.State.Velocity;
            Assert.IsTrue(float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z), $"velocity {v}");
            Assert.AreEqual(10f, rig.State.Speed, 1e-3);
        }
    }

    [TestMethod]
    public void AShipAtTheExactCenterGetsAFiniteVelocity()
    {
        foreach (var at in new[] { Vector3.Zero, new Vector3(0, 0, 0.5f) })
        {
            // a dying ship is pulled but never swallowed, so it stays to be read even at the center of a hole whose horizon
            // is 0 wide
            var rig = Build(at, NoEngines, Hole(Vector3.Zero, eventRadius: 0));
            rig.State.Dying = true;
            rig.State.DyingRemaining = 1000;
            rig.Tick(3);
            var v = rig.State.Velocity;
            Assert.IsTrue(float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z), $"velocity {v}");
            Assert.IsTrue(float.IsFinite(rig.At.X + rig.At.Y + rig.At.Z), $"at {rig.At}");
        }
    }

    // A hole 300 along +Z with an event radius of 20: its ring is 30, and a ship of model radius 10 rests 40 from the
    // center, at z = 260. No pull (a force of 0) so only the ring acts.
    static BlackHole Ring() => Hole(new Vector3(0, 0, 300), eventRadius: 20, force: 0);

    [TestMethod]
    public void AShipWithThrustOrderedThroughTheHoleEndsAtTheRingAndIsNeverSwallowed()
    {
        var rig = Build(Vector3.Zero, null, Ring());
        rig.World.Move(rig.Unit, 0, 300);
        for (var i = 0; i < 2000; i++)
        {
            rig.Tick();
            Assert.IsNotNull(rig.World.State(rig.Unit), $"swallowed at tick {i}");
            Assert.IsFalse(rig.State.Dying);
        }

        Assert.AreEqual(260f, rig.At.Z, 1e-3);
        Assert.AreEqual(0, rig.Events.OfType<UnitDestroyed>().Count());
    }

    [TestMethod]
    public void TheRingPushIsStraightOutInThePlaneAndKeepsTheHeight()
    {
        var rig = Build(new Vector3(35, 7, 300), null, Ring()); // 35 from the center, inside 40
        rig.Tick();
        Assert.AreEqual(40f, rig.At.X, 1e-3);
        Assert.AreEqual(7f, rig.At.Y, 1e-4);
        Assert.AreEqual(300f, rig.At.Z, 1e-3);
    }

    [TestMethod]
    public void AShipWithDestroyedEnginesOrADerelictOneIsNotPushedOutOfTheRing()
    {
        foreach (var start in new[] { NoEngines, NoCrew })
        {
            var rig = Build(new Vector3(35, 0, 300), start, Ring()); // between the event radius 20 and the ring 40
            rig.Tick(5);
            Assert.AreEqual(new Vector3(35, 0, 300), rig.At);
        }
    }

    [TestMethod]
    public void AShipFarFromTheRingIsLeftAlone()
    {
        var rig = Build(new Vector3(60, 0, 300), null, Ring());
        rig.Tick(5);
        Assert.AreEqual(new Vector3(60, 0, 300), rig.At);
    }

    // A hole at the origin with an event radius of 20 and no pull.
    static BlackHole Horizon() => Hole(Vector3.Zero, eventRadius: 20, force: 0);

    [TestMethod]
    public void AUnitInsideTheEventRadiusIsSwallowedOnTheFirstTickAndLeavesOnTheNextReportedOnce()
    {
        // ship with thrust, ship without, and a station, all inside 20 in the plane
        var rig = Build(
            new Vector3(5, 0, 0),
            null,
            [Horizon()],
            null,
            (new Vector3(0, 0, 8), true),
            (new Vector3(-10, 0, 0), false)
        );
        rig.World.State(rig.Units[1])!.Begin(NoEngines);
        rig.Tick();
        Assert.IsTrue(rig.Units.All(u => rig.World.State(u)!.Dying));
        Assert.IsTrue(rig.Units.All(u => rig.World.State(u)!.DyingRemaining <= 0));
        Assert.AreEqual(0, rig.Events.OfType<UnitDestroyed>().Count());
        rig.Tick();
        Assert.IsTrue(rig.Units.All(u => rig.World.State(u) == null));
        rig.Tick(50);
        var gone = rig.Events.OfType<UnitDestroyed>().ToList();
        Assert.AreEqual(3, gone.Count);
        Assert.IsTrue(gone.All(e => e.Cause == DestroyCause.BlackHole));
        CollectionAssert.AreEquivalent(rig.Units.ToList(), gone.Select(e => e.Unit).ToList());
    }

    [TestMethod]
    public void ASwallowedUnitShowsNoSmallExplosionsAndAFireballKillShowsThem()
    {
        var rig = Build(new Vector3(5, 0, 0), null, Horizon());
        rig.Tick(3);
        Assert.AreEqual(1, rig.Events.OfType<UnitDestroyed>().Count()); // it did leave
        Assert.AreEqual(0, rig.Events.OfType<SmallExplosion>().Count());

        // the same ship doomed by a hit instead, far from the hole, dies with small explosions and a fireball cause
        var doomed = Build(new Vector3(500, 0, 0), null, Horizon());
        doomed.State.Dying = true;
        doomed.State.DyingRemaining = 1;
        doomed.Tick(60);
        Assert.IsTrue(doomed.Events.OfType<SmallExplosion>().Any());
        Assert.AreEqual(DestroyCause.Fireball, doomed.Events.OfType<UnitDestroyed>().Single().Cause);
    }

    [TestMethod]
    public void TheEventHorizonIsMeasuredInThePlaneFromTheCenterAndIncludesItsEdge()
    {
        // 1000 above the plane still counts; exactly 20 away counts; 20.5 away does not
        var rig = Build(new Vector3(0, 1000, 20), null, [Horizon()], null, (new Vector3(20.5f, 0, 0), true));
        rig.Tick();
        Assert.IsTrue(rig.World.State(rig.Units[0])!.Dying);
        Assert.IsFalse(rig.World.State(rig.Units[1])!.Dying);
    }

    [TestMethod]
    public void ADyingUnitIsNotSwallowedAndAHolelessWorldSwallowsNothing()
    {
        var rig = Build(new Vector3(5, 0, 0), null, Horizon());
        rig.State.Dying = true;
        rig.State.DyingRemaining = 1000;
        rig.Tick(5);
        Assert.AreEqual(0, rig.Events.OfType<UnitDestroyed>().Count());
        Assert.IsTrue(rig.State.DyingRemaining > 900);

        var free = Build(new Vector3(5, 0, 0), null);
        free.Tick(5);
        Assert.IsFalse(free.State.Dying);
    }

    [TestMethod]
    public void ASwallowedUnitWithAModelShattersIntoDebrisWhenItLeaves()
    {
        var model = DebrisTests.Model(DebrisTests.MeshNode("hull", "", 4));
        var rig = Build(new Vector3(5, 0, 0), null, [Horizon()], model);
        rig.Tick();
        Assert.AreEqual(0, rig.World.Debris.Count);
        rig.Tick();
        Assert.IsTrue(rig.World.Debris.Count > 0);
    }

    [TestMethod]
    public void ADebrisPieceGetsNoPullFromABlackHole()
    {
        var model = DebrisTests.Model(DebrisTests.MeshNode("hull", "", 4));
        var pulling = Build(new Vector3(5, 0, 0), null, [Hole(Vector3.Zero, eventRadius: 20, force: 450)], model);
        var idle = Build(new Vector3(5, 0, 0), null, [Horizon()], model);
        foreach (var rig in new[] { pulling, idle })
        {
            rig.Tick(10);
        }

        CollectionAssert.AreEqual(
            idle.World.Debris.Select(p => p.Position).ToList(),
            pulling.World.Debris.Select(p => p.Position).ToList()
        );
    }

    [TestMethod]
    public void TheModelTurnsAboutTheVerticalAxisAtRotationRadiansPerSecondOfWorldTime()
    {
        var hole = Hole(Vector3.Zero, rotation: 0.5f);
        var turned = Vector3.Transform(Vector3.UnitX, hole.Turn(2)); // one radian
        Assert.AreEqual(MathF.Cos(1), turned.X, 1e-5);
        Assert.AreEqual(-MathF.Sin(1), turned.Z, 1e-5);
        Assert.AreEqual(0f, turned.Y, 1e-6);
        Assert.AreEqual(Quaternion.Identity, hole.Turn(0));
        // the same time gives the same turn, however it was reached
        Assert.AreEqual(hole.Turn(3.7), hole.Turn(3.7));
        Assert.AreEqual(1f, Vector3.Transform(Vector3.UnitY, hole.Turn(123.4)).Y, 1e-5);
    }

    [TestMethod]
    public void WithinOneUnitTheForceIsTheGravityForceTimesTheSquaredRadius()
    {
        // d = 0.5 < 1: F = 1e-5 x 100^2 = 0.1, so one tick changes the velocity by 0.002 (the formula with d^2 gives 0.008)
        var rig = Build(new Vector3(0, 0, 0.5f), NoEngines, Hole(Vector3.Zero, eventRadius: 0, force: 1e-5f));
        rig.Tick();
        Assert.AreEqual(-0.002f, rig.State.Velocity.Z, 1e-6);
    }
}
