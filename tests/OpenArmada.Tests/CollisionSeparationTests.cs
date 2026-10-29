using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Formats;
using OpenArmada.Sandbox;
using static OpenArmada.Tests.CombatFixtures;

namespace OpenArmada.Tests;

// The collision separation of the world's post-simulate pass (0x464910). Ships are spheres of their model radius
// (10 unless a test says otherwise), with a motion that drives at 10 units/s and faces +Z. A coasting ship has its
// engines destroyed and a starting velocity, so it keeps that speed (it slows by 2.0 per second per second) while
// the thrust ships of a test stay still.
[TestClass]
public class CollisionSeparationTests
{
    sealed record Ship(
        string Label,
        Vector3 Position,
        float Radius = 10,
        Vector3? Coasting = null,
        bool Station = false,
        bool Borg = false,
        float Heading = 0,
        ShieldEllipsoid? Shield = null,
        bool Derelict = false,
        double TooClose = 25
    );

    sealed class Rig
    {
        public required CombatWorld World { get; init; }
        public required UnitPoses Poses { get; init; }
        public required Dictionary<string, PlacedObject> Objects { get; init; }

        public Vector3 At(string label) => Poses.Pose(Objects[label]).Position;

        public float Heading(string label) => Poses.Pose(Objects[label]).Heading;

        public UnitCombatState State(string label) => World.State(Objects[label])!;

        public void Tick(int count = 1)
        {
            for (var i = 0; i < count; i++)
            {
                World.Step(CombatWorld.Tick);
            }
        }
    }

    static Rig Build(float maxPrevention = 0.4f, params Ship[] ships)
    {
        var objects = ships.ToDictionary(
            s => s.Label,
            s =>
                Placed(s.Label, 1, s.Position.X, s.Position.Z) with
                {
                    Position = s.Position,
                    Start =
                        s.Coasting is { } v ? new StartingState { DestroyedSystems = ["Engines"], Velocity = v }
                        : s.Derelict ? new StartingState { Crew = 0 }
                        : null,
                }
        );
        var poses = new UnitPoses(objects.Values);
        var units = ships.Select(s =>
        {
            var o = objects[s.Label];

            return new CombatUnit
            {
                Unit = o,
                Profile = Profile(systems: [Sys("Engines")]),
                DefinitionName = "s.odf",
                Radius = 0,
                ModelRadius = s.Radius,
                Shield = s.Shield,
                Mobile = !s.Station,
                Motion = s.Station
                    ? null
                    : new ShipMotion(
                        new PhysicsValues
                        {
                            ImpulseSpeed = 10,
                            YawRotationRate = 1,
                            MaxRoll = null,
                            TooCloseToTurn = s.TooClose,
                            BorgPhysics = s.Borg,
                        },
                        o.Position,
                        s.Heading
                    ),
            };
        });
        var world = new CombatWorld(poses, units, 1, () => 0, (_, _, pose) => pose.Position)
        {
            MaxPreventionRotation = maxPrevention,
        };

        return new Rig
        {
            World = world,
            Poses = poses,
            Objects = objects,
        };
    }

    static Rig Build(params Ship[] ships) => Build(0.4f, ships);

    // A at the origin, B 12 units ahead on +Z: the spheres of radius 10 overlap by 8 (the 8.2 below is after A's own tick).

    [TestMethod]
    public void AMovingShipThatRunsIntoAStillOneTakesTheWholeSeparationItself()
    {
        var rig = Build(
            new Ship("a", Vector3.Zero, Coasting: new Vector3(0, 0, 10)),
            new Ship("b", new Vector3(0, 0, 12))
        );
        rig.Tick();
        Assert.AreEqual(12f, rig.At("b").Z, 1e-4); // the still ship's share is 0
        Assert.AreEqual(-8f, rig.At("a").Z, 0.05); // 0.2 of its own flight, then back out of the 8.2 overlap
    }

    [TestMethod]
    public void TheStillShipMovesOnlySlightlyAndTheMovingShipTakesMostByTheirShareOfTheCombinedSpeed()
    {
        var rig = Build(
            new Ship("a", Vector3.Zero, Coasting: new Vector3(0, 0, 10)),
            new Ship("b", new Vector3(0, 0, 12), Coasting: new Vector3(0, 0, 0.5f))
        );
        rig.Tick();
        var bMoved = rig.At("b").Z - 12 - 0.01f; // minus its own flight of 0.5 * 0.02
        var aMoved = 0.2f - rig.At("a").Z;
        Assert.IsTrue(bMoved > 0 && bMoved < 0.5, $"b moved {bMoved}");
        Assert.AreEqual(8.2f * 10f / 10.5f, aMoved, 0.1);
    }

    [TestMethod]
    public void TwoStillShipsMoveHalfEach()
    {
        var rig = Build(new Ship("a", Vector3.Zero), new Ship("b", new Vector3(0, 0, 12)));
        rig.Tick();
        Assert.AreEqual(-4f, rig.At("a").Z, 1e-3);
        Assert.AreEqual(16f, rig.At("b").Z, 1e-3);
    }

    [TestMethod]
    public void AShipOverlappingAStationMovesByItsOwnShareOnlyAndTheStationNeverMoves()
    {
        var rig = Build(new Ship("a", Vector3.Zero), new Ship("station", new Vector3(0, 0, 12), Station: true));
        rig.Tick();
        Assert.AreEqual(-4f, rig.At("a").Z, 1e-3); // half of the 8; the station's half is lost
        rig.Tick(40);
        Assert.AreEqual(new Vector3(0, 0, 12), rig.At("station"));
        Assert.AreEqual(-8f, rig.At("a").Z, 0.1); // the remainder halves each tick until the surfaces just meet
    }

    [TestMethod]
    public void OverlappingShipsAtEqualHeightsEndAtDifferentHeightsTheRunningShipUpAndKeepThemAndTheirVelocity()
    {
        var rig = Build(new Ship("a", Vector3.Zero), new Ship("b", new Vector3(0, 0, 12)));
        rig.Tick();
        Assert.AreEqual(0.21f, rig.At("a").Y, 1e-4); // 0.42 at half the share
        Assert.AreEqual(-0.21f, rig.At("b").Y, 1e-4);
        rig.Tick(10);
        Assert.AreEqual(0.21f, rig.At("a").Y, 1e-4);
        Assert.AreEqual(Vector3.Zero, rig.State("a").Velocity);
    }

    [TestMethod]
    public void TheVerticalPushGoesTowardTheLowerShip()
    {
        var rig = Build(new Ship("a", new Vector3(0, 1, 0)), new Ship("b", new Vector3(0, 3, 12)));
        rig.Tick();
        Assert.IsTrue(rig.At("a").Y < 1, "a, the lower ship, goes down");
        Assert.IsTrue(rig.At("b").Y > 3, "b, the higher ship, goes up");
    }

    [TestMethod]
    public void ACollisionSeparationLeavesEveryShipsSpeedAsItWas()
    {
        var rig = Build(
            new Ship("a", Vector3.Zero, Coasting: new Vector3(0, 0, 10)),
            new Ship("b", new Vector3(0, 0, 12), Coasting: new Vector3(0, 0, 0.5f))
        );
        rig.Tick();
        Assert.AreEqual(10f - 2 * CombatWorld.Tick, rig.State("a").Speed, 1e-3);
        Assert.AreEqual(0.5f - 2 * CombatWorld.Tick, rig.State("b").Speed, 1e-3);
    }

    // B sits to the side of A's nose, so the push turns A's nose away from it: toward -X.
    static Ship[] SideBySide(bool borg, float aSpeed = 0, float bSpeed = 0, float bHeading = 0) =>
        [
            new Ship("a", Vector3.Zero, Borg: borg, Coasting: aSpeed > 0 ? new Vector3(0, 0, aSpeed) : null),
            new Ship(
                "b",
                new Vector3(6, 0, 10),
                Heading: bHeading,
                Coasting: bSpeed > 0 ? new Vector3(0, 0, bSpeed) : null
            ),
        ];

    [TestMethod]
    public void AShipWhoseNosePointsTowardTheOtherTurnsItsHeadingAwayByTheGamesAngle()
    {
        var rig = Build(SideBySide(false));
        rig.Tick();
        // d = (-6, 0, -10), |d| = 11.66, penetration 8.34. The turn runs after the move: a has gone half the push
        // away, so |e| = 11.66 + 4.17 = 15.83 on the same line, and alpha = acos(10 / 11.66) = 0.540. The angle is
        // 8.34 / 15.83 * 0.4 / 0.540 = 0.390 rad about the axis of the nose and the push, which is nearly
        // horizontal, so nearly all of it is heading
        Assert.AreEqual(-0.388f, rig.Heading("a"), 0.01);
    }

    [TestMethod]
    public void TheCollisionTurnUsesTheMaxPreventionRotationItIsGiven()
    {
        var half = Build(0.2f, SideBySide(false));
        half.Tick();
        Assert.AreEqual(-0.194f, half.Heading("a"), 0.01); // half the angle of the test above
    }

    [TestMethod]
    public void ABorgPhysicsShipAddsToTheVerticalPushAndNeverTurns()
    {
        var rig = Build(SideBySide(true));
        rig.Tick();
        Assert.AreEqual(0f, rig.Heading("a"));
        Assert.AreEqual(0.42f, rig.At("a").Y, 1e-4); // 0.42 + 0.42 at half the share
    }

    [TestMethod]
    public void AShipWithAShareOfOneTenthOrLessAndAShipWhoseNosePointsAwayDoNotTurn()
    {
        // a flies at 0.5 beside b at 10: a's share is below a tenth; b's nose points away from a
        var rig = Build(SideBySide(false, aSpeed: 0.5f, bSpeed: 10));
        rig.Tick();
        Assert.AreEqual(0f, rig.Heading("a"));
        Assert.AreEqual(0f, rig.Heading("b"));
    }

    [TestMethod]
    public void ADyingShipNeitherMovesNorMovesAnotherShip()
    {
        var rig = Build(new Ship("a", Vector3.Zero), new Ship("b", new Vector3(0, 0, 12)));
        rig.State("a").Dying = true;
        rig.Tick(3);
        Assert.AreEqual(Vector3.Zero, rig.At("a"));
        Assert.AreEqual(new Vector3(0, 0, 12), rig.At("b"));
    }

    [TestMethod]
    public void ADerelictShipRunsNoPassButIsMovedAsANeighbour()
    {
        var beside = Build(
            new Ship("derelict", Vector3.Zero, Derelict: true),
            new Ship("station", new Vector3(0, 0, 12), Station: true)
        );
        beside.Tick();
        Assert.AreEqual(Vector3.Zero, beside.At("derelict")); // no pass of its own, and a station runs none

        var rig = Build(new Ship("derelict", Vector3.Zero, Derelict: true), new Ship("b", new Vector3(0, 0, 12)));
        rig.Tick();
        Assert.AreEqual(-4f, rig.At("derelict").Z, 1e-3); // b's pass moves it by half
    }

    [TestMethod]
    public void AShipOrderedOntoTheSpotWhereAnotherHoldsIsSeparatedEndsItsOrderAndBothRestApart()
    {
        // radii of 10: the surfaces meet 20 from the holder, 25 or less from the point under tooCloseToTurn
        var rig = Build(new Ship("a", Vector3.Zero), new Ship("b", new Vector3(0, 0, 100)));
        rig.World.Move(rig.Objects["a"], 0, 100);
        rig.Tick(750); // 15 s
        Assert.IsNull(rig.State("a").PlayerMove);
        Assert.IsTrue(Vector3.Distance(rig.At("a"), rig.At("b")) >= 19.9f, "apart");
        Assert.AreNotEqual(rig.At("a").Y, rig.At("b").Y);
        var (a, b) = (rig.At("a"), rig.At("b"));
        rig.Tick(250);
        Assert.AreEqual(a, rig.At("a"));
        Assert.AreEqual(b, rig.At("b"));
    }

    [TestMethod]
    public void AShipHeldOffFartherThanTooCloseToTurnFromItsPointKeepsItsOrderOpenAtTheContact()
    {
        // radii of 30: the surfaces meet 60 from the holder, beyond the 25 of tooCloseToTurn from the point
        var rig = Build(new Ship("a", Vector3.Zero, Radius: 30), new Ship("b", new Vector3(0, 0, 100), Radius: 30));
        rig.World.Move(rig.Objects["a"], 0, 100);
        rig.Tick(200); // 4 s at 10 units/s: the surfaces meet
        Assert.AreEqual(40f, rig.At("a").Z, 0.1);
        Assert.IsNotNull(rig.State("a").PlayerMove);
    }

    [TestMethod]
    public void AShipHeldOffFartherThanTooCloseToTurnClimbsOverTheHolderEndsItsOrderAboveItAndNeitherMovesAfterwards()
    {
        var rig = Build(new Ship("a", Vector3.Zero, Radius: 30), new Ship("b", new Vector3(0, 0, 100), Radius: 30));
        rig.World.Move(rig.Objects["a"], 0, 100);
        rig.Tick(800); // 16 s: the contact at 4 s, the lift, and the arrival above the holder
        Assert.IsNull(rig.State("a").PlayerMove);
        Assert.IsTrue(rig.At("a").Y > rig.At("b").Y, $"a at {rig.At("a").Y}, b at {rig.At("b").Y}");
        Assert.IsTrue(
            Vector2.Distance(new Vector2(rig.At("a").X, rig.At("a").Z), new Vector2(0, 100)) <= 25,
            $"a at {rig.At("a")}"
        );
        var (a, b) = (rig.At("a"), rig.At("b"));
        rig.Tick(250);
        Assert.AreEqual(a, rig.At("a"));
        Assert.AreEqual(b, rig.At("b"));
    }

    [TestMethod]
    public void AHullSurfacePointWhoseProbeIsAtTheMapOriginHasExtentZero()
    {
        // b's probe point, 4 model radii from its center toward a, is the map origin: no direction to sample the
        // ellipsoid along, so b's extent is 0 and a's sphere of 8 alone reaches over the 5 between the centers
        var a = new CollisionSeparation.Body(new Vector3(0, 0, -35), 8, null);
        var b = new CollisionSeparation.Body(
            new Vector3(0, 0, -40),
            10,
            new ShieldEllipsoid(Vector3.Zero, new Vector3(30, 5, 5), Vector3.Zero)
        );
        var contact = CollisionSeparation.Between(a, b);
        Assert.IsNotNull(contact);
        Assert.AreEqual(3f, contact.Value.Penetration, 1e-4);
    }

    [TestMethod]
    public void TwoBorgPhysicsShipsAddTheirVerticalPushesAtTheShare()
    {
        var rig = Build(new Ship("a", Vector3.Zero, Borg: true), new Ship("b", new Vector3(0, 0, 12), Borg: true));
        rig.Tick();
        Assert.AreEqual(0.63f, rig.At("a").Y, 1e-4); // 0.42 * 3 at half the share
        Assert.AreEqual(-0.63f, rig.At("b").Y, 1e-4);
    }

    [TestMethod]
    public void TheCollisionTurnOfAShipWithThrustTurnsItsVelocityWithItsHeading()
    {
        // a drives +Z at 10 beside b, which is off its nose to the side: the turn runs, and a ship with thrust takes
        // its velocity from its heading, so the velocity direction follows (the game's turn never writes velocity)
        var rig = Build(SideBySide(false));
        rig.World.Move(rig.Objects["a"], 0, 1000);
        rig.Tick();
        var heading = rig.Heading("a");
        var velocity = rig.State("a").Velocity;
        Assert.IsTrue(heading < -0.1f, $"turned away from b: {heading}");
        Assert.IsTrue(velocity.Length() > 0.01f, $"a drives: {velocity}");
        Assert.AreEqual(heading, MathF.Atan2(velocity.X, velocity.Z), 1e-3);
    }

    [TestMethod]
    public void ShipsWithAShieldEllipsoidGiveFinitePositionsAtCoincidingCentersAndNearTheMapOrigin()
    {
        using var log = new CapturedLog();
        var ellipsoid = new ShieldEllipsoid(Vector3.Zero, new Vector3(30, 5, 5), Vector3.Zero);
        var rig = Build(
            new Ship("a", new Vector3(100, 0, 0), Shield: ellipsoid),
            new Ship("b", new Vector3(100, 0, 0), Shield: ellipsoid),
            new Ship("c", new Vector3(0, 0, 0), Shield: ellipsoid),
            new Ship("d", new Vector3(0.5f, 0, 0.5f), Shield: ellipsoid),
            new Ship("e", new Vector3(3, 0, 1), Shield: ellipsoid)
        );
        rig.Tick(20);
        foreach (var label in new[] { "a", "b", "c", "d", "e" })
        {
            var p = rig.At(label);
            Assert.IsTrue(float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z), label);
            Assert.IsTrue(float.IsFinite(rig.Heading(label)), label);
        }

        Assert.AreEqual(0, log.Lines.Count(l => l.Contains("ERROR")), log.Text);
    }

    [TestMethod]
    public void TwoShipsAtOnePointAShipWithoutMeshesAndAShipWithoutAShieldEllipsoidGiveFinitePositionsAndNoFault()
    {
        using var log = new CapturedLog();
        var rig = Build(
            new Ship("a", Vector3.Zero),
            new Ship("b", Vector3.Zero),
            new Ship("bare", new Vector3(5, 0, 5), Radius: 0),
            new Ship("sphere", new Vector3(8, 0, 8), Shield: null)
        );
        rig.Tick(20);
        foreach (var label in new[] { "a", "b", "bare", "sphere" })
        {
            var p = rig.At(label);
            Assert.IsTrue(float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z), label);
            Assert.IsTrue(float.IsFinite(rig.Heading(label)), label);
        }

        Assert.AreEqual(0, log.Lines.Count(l => l.Contains("ERROR")), log.Text);
    }

    [TestMethod]
    public void TheHullSurfacePointSamplesTheEllipsoidAlongTheDirectionOfTheProbeFromTheMapOrigin()
    {
        // an ellipsoid of radii (30, 5, 5) around a ship at (100, 0, 0), probed toward a ship on the same row: the
        // game's probe point (100 - 4 * 10 * u) lies along +X from the map origin, so the radius sampled is 30
        // and the overlap the surfaces make is 30 + 10 - 20, where a sphere of 10 would make none
        var rig = Build(
            new Ship(
                "a",
                new Vector3(100, 0, 0),
                Shield: new ShieldEllipsoid(Vector3.Zero, new Vector3(30, 5, 5), Vector3.Zero)
            ),
            new Ship("b", new Vector3(120, 0, 0))
        );
        rig.Tick();
        Assert.IsTrue(rig.At("a").X < 100 && rig.At("b").X > 120, "pushed apart by the long ellipsoid axis");
    }
}
