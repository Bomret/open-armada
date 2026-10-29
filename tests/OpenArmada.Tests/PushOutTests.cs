using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Formats;
using OpenArmada.Sandbox;
using static OpenArmada.Tests.CombatFixtures;

namespace OpenArmada.Tests;

// The push-out of the world's post-simulate pass (0x4644b0): a ship that is not derelict and not dying (a coasting
// ship included) that overlaps an asteroid field or a planet is moved straight out in the plane. Ships have a model radius of 10, drive at 10 units/s facing +Z, and
// start at the origin; an obstacle of radius 100 at (0, 0, 200) has its edge at z = 90 for them (100 + 10 less 200).
[TestClass]
public class PushOutTests
{
    static readonly Obstacle Field = new(new Vector3(0, 0, 200), 100);

    sealed class Rig
    {
        public required CombatWorld World { get; init; }
        public required UnitPoses Poses { get; init; }
        public required PlacedObject Unit { get; init; }

        public Vector3 At => Poses.Pose(Unit).Position;

        public UnitCombatState State => World.State(Unit)!;

        public void Tick(int count = 1)
        {
            for (var i = 0; i < count; i++)
            {
                World.Step(CombatWorld.Tick);
            }
        }
    }

    // One ship at `at`, with the given obstacles; `start` makes it coast (engines destroyed) or a derelict.
    static Rig Build(Vector3 at, StartingState? start = null, double tooClose = 25, params Obstacle[] obstacles)
    {
        var unit = Placed("s", 1, at.X, at.Z) with { Position = at, Start = start };
        var poses = new UnitPoses(new[] { unit });
        var motion = new ShipMotion(
            new PhysicsValues
            {
                ImpulseSpeed = 10,
                YawRotationRate = 1,
                MaxRoll = null,
                TooCloseToTurn = tooClose,
            },
            at,
            0
        );
        var combat = new CombatUnit
        {
            Unit = unit,
            Profile = Profile(systems: [Sys("Engines")]),
            DefinitionName = "s.odf",
            Radius = 0,
            ModelRadius = 10,
            Mobile = true,
            Motion = motion,
        };
        var world = new CombatWorld(poses, [combat], 1, () => 0, (_, _, pose) => pose.Position)
        {
            Obstacles = obstacles,
        };

        return new Rig
        {
            World = world,
            Poses = poses,
            Unit = unit,
        };
    }

    static Rig Build(params Obstacle[] obstacles) => Build(Vector3.Zero, null, 25, obstacles);

    [TestMethod]
    public void AShipOrderedToAPointInsideAFieldWithinTooCloseToTurnOfTheEdgeEndsAtTheEdgeWithItsOrderEnded()
    {
        var rig = Build(Vector3.Zero, null, 25, Field);
        rig.World.Move(rig.Unit, 0, 100); // 10 inside the edge at z = 90
        rig.Tick(600);
        Assert.IsNull(rig.State.PlayerMove);
        Assert.AreEqual(90f, rig.At.Z, 1e-3);
        Assert.AreEqual(0f, rig.At.X, 1e-3);
        Assert.AreEqual(Vector3.Zero, rig.State.Velocity);
    }

    [TestMethod]
    public void AShipOrderedDeeperInsideAFieldRestsAtTheEdgeWithItsOrderOpenAndDoesNotMoveBackAndForth()
    {
        var rig = Build(Field);
        rig.World.Move(rig.Unit, 0, 150); // 60 inside the edge, farther than tooCloseToTurn
        rig.Tick(500); // 10 s: it has reached the edge
        var rests = new List<float>();
        for (var i = 0; i < 200; i++)
        {
            rig.Tick();
            rests.Add(rig.At.Z);
        }

        Assert.IsNotNull(rig.State.PlayerMove);
        Assert.IsTrue(rests.All(z => System.Math.Abs(z - 90) < 1e-3), $"z ranged {rests.Min()}..{rests.Max()}");
    }

    [TestMethod]
    public void AShipOrderedToAPointInsideAPlanetEndsAtItsEdgeTheSameWay()
    {
        var planet = new Obstacle(new Vector3(0, 0, 150), 40); // edge at 150 - 50 = 100
        var near = Build(planet);
        near.World.Move(near.Unit, 0, 110); // 10 inside
        near.Tick(700);
        Assert.IsNull(near.State.PlayerMove);
        Assert.AreEqual(100f, near.At.Z, 1e-3);

        var deep = Build(planet);
        deep.World.Move(deep.Unit, 0, 150); // at the center, 50 inside
        deep.Tick(700);
        Assert.IsNotNull(deep.State.PlayerMove);
        Assert.AreEqual(100f, deep.At.Z, 1e-3);
    }

    [TestMethod]
    public void ThePushIsStraightOutInThePlaneAndTheShipKeepsItsHeight()
    {
        var rig = Build(new Vector3(60, 7, 0), null, 25, new Obstacle(new Vector3(0, 0, 0), 100)); // 60 from the center, ring 110
        rig.Tick();
        Assert.AreEqual(7f, rig.At.Y, 1e-4);
        Assert.AreEqual(110f, System.MathF.Sqrt(rig.At.X * rig.At.X + rig.At.Z * rig.At.Z), 0.01); // on the ring...
        Assert.AreEqual(0f, rig.At.Z, 0.01); // ...straight out along +X from the center
    }

    [TestMethod]
    public void TheObstacleRadiusIsTheInputThatSetsWhereTheShipRests()
    {
        foreach (var radius in new[] { 50f, 100f, 300f })
        {
            var rig = Build(Vector3.Zero, null, 25, new Obstacle(new Vector3(0, 0, 400), radius));
            rig.World.Move(rig.Unit, 0, 400);
            rig.Tick(2000);
            Assert.AreEqual(400 - radius - 10, rig.At.Z, 1e-3, $"radius {radius}");
        }
    }

    [TestMethod]
    public void ACoastingShipWithDestroyedEnginesIsPushedOutOfAFieldLikeAnyOther()
    {
        var coasting = Build(
            Vector3.Zero,
            new StartingState { DestroyedSystems = ["Engines"], Velocity = new Vector3(0, 0, 10) },
            25,
            new Obstacle(new Vector3(0, 0, 30), 10) // edge at z = 10; the coast would reach 25
        );
        coasting.Tick(500);
        Assert.IsTrue(coasting.At.Z <= 10 + 1e-3, $"coasting ship rests at {coasting.At.Z}");
        Assert.IsTrue(coasting.At.Z > 5, $"coasting ship rests at {coasting.At.Z}");
    }

    [TestMethod]
    public void ADyingShipEntersAFieldUnhindered()
    {
        var dying = Build(new Vector3(0, 0, 25), null, 25, new Obstacle(new Vector3(0, 0, 30), 10));
        dying.State.Dying = true;
        dying.State.DyingRemaining = 1000;
        dying.Tick(3);
        Assert.AreEqual(25f, dying.At.Z, 1e-4);
    }

    [TestMethod]
    public void ADerelictShipEntersAFieldUnhindered()
    {
        var derelict = Build(
            Vector3.Zero,
            new StartingState { Crew = 0, Velocity = new Vector3(0, 0, 10) },
            25,
            new Obstacle(new Vector3(0, 0, 30), 10)
        );
        derelict.Tick(500);
        Assert.IsTrue(derelict.At.Z > 20, $"derelict ship rests at {derelict.At.Z}");
    }

    [TestMethod]
    public void APushOutLeavesTheVelocityAsItWas()
    {
        var free = Build();
        var pushed = Build(Field);
        foreach (var rig in new[] { free, pushed })
        {
            rig.World.Move(rig.Unit, 0, 150);
            rig.Tick(498); // z is about 99.5 for the free ship; the pushed one sits at the edge
        }

        Assert.AreEqual(90f, pushed.At.Z, 1e-3);
        Assert.AreEqual(free.State.Velocity, pushed.State.Velocity);
        Assert.AreEqual(free.State.Speed, pushed.State.Speed);
        Assert.IsTrue(pushed.State.Speed > 9);
    }

    [TestMethod]
    public void AShipWhoseCenterIsTheObstacleCenterInThePlaneIsNotMovedAndNothingIsReported()
    {
        var rig = Build(new Vector3(0, 5, 200), null, 25, Field);
        using var log = new CapturedLog();
        rig.Tick(3);
        Assert.AreEqual(new Vector3(0, 5, 200), rig.At);
        Assert.AreEqual(0, log.Lines.Count, log.Text);
    }

    [TestMethod]
    public void AShipFarFromEveryObstacleDrivesOnUntouched()
    {
        var rig = Build(new Obstacle(new Vector3(500, 0, 500), 30));
        rig.World.Move(rig.Unit, 0, 100);
        rig.Tick(250);
        Assert.AreEqual(50f, rig.At.Z, 0.5);
        Assert.IsNotNull(rig.State.PlayerMove);
    }

    // Two fields of radius 100 that overlap: their centers 100 apart on X, so each one's edge for a ship of radius 10
    // passes the other's center.
    static readonly Obstacle[] Overlapping = [new(new Vector3(-50, 0, 200), 100), new(new Vector3(50, 0, 200), 100)];

    // The positions of the ship over `count` ticks.
    static List<Vector3> Track(Rig rig, int count)
    {
        var at = new List<Vector3>();
        for (var i = 0; i < count; i++)
        {
            rig.Tick();
            at.Add(rig.At);
        }

        return at;
    }

    static bool Outside(Vector3 at, Obstacle o) =>
        Vector2.Distance(new Vector2(at.X, at.Z), new Vector2(o.Center.X, o.Center.Z)) >= o.Radius + 10 - 1e-3f;

    // Every position of the track within 1e-3 of the first (float rounding of the two pushes, as for one field).
    static bool Rests(List<Vector3> at) => at.All(p => Vector3.Distance(p, at[0]) < 1e-3);

    [TestMethod]
    public void AShipHoldingInsideTwoOverlappingFieldsIsMovedOutOfBothAndComesToRest()
    {
        // pushed out at once, farther than tooCloseToTurn from its spot, it drives back toward the spot along the fields'
        // edges to the point where they meet (0, 101.98) and stays there
        var rig = Build(new Vector3(5, 0, 190), null, 25, Overlapping);
        rig.Tick();
        Assert.IsTrue(Overlapping.All(o => Outside(rig.At, o)), $"the ship is at {rig.At}");
        rig.Tick(1500);
        var at = Track(rig, 250);
        Assert.IsTrue(Overlapping.All(o => Outside(at[0], o)), $"the ship rests at {at[0]}");
        Assert.IsTrue(Rests(at), $"the ship moved: {string.Join(" ", at.Distinct().Take(4))}");
    }

    [TestMethod]
    public void AShipOrderedIntoTheOverlapOfTwoFieldsRestsOutsideBothAndDoesNotMoveBackAndForth()
    {
        var rig = Build(new Vector3(0, 0, 0), null, 25, Overlapping);
        rig.World.Move(rig.Unit, 0, 200); // the middle of the overlap
        rig.Tick(1000); // 20 s: it has reached the fields
        var at = Track(rig, 250);
        Assert.IsNotNull(rig.State.PlayerMove);
        Assert.IsTrue(Overlapping.All(o => Outside(at[0], o)), $"the ship rests at {at[0]}");
        Assert.IsTrue(Rests(at), $"the ship moved: {string.Join(" ", at.Distinct().Take(4))}");
    }
}
