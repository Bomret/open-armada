using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Assets;
using OpenArmada.Sandbox;
using static OpenArmada.Tests.CombatFixtures;

namespace OpenArmada.Tests;

// The game's obstacle-avoidance scorer (0x42ef70) over bodies built in code (INV-0001). The expected directions are
// worked by hand from the recovered rules: ten candidates of the wanted direction, each scored 100 x alignment minus
// 100 x weight x R^2 / dmin^2 where it passes inside R. A ship flies at speed 10 toward the game's +Z, which is the
// sandbox's -Z (the sandbox mirrors Z), so a turn toward the game's -X is the ship's left.
[TestClass]
public class ObstacleAvoidanceTests
{
    // A point given in the game's frame, in the sandbox's.
    static Vector3 G(float x, float y, float z) => new(x, y, -z);

    static readonly Vector3 Wanted = G(0, 0, 1);

    // The ship at the origin flying at 10 toward the game's +Z, with an impulse speed of 10.
    static FlightModel Ship()
    {
        var ship = FlightFixtures.Cruiser(Vector3.Zero, MathF.PI);
        ship.SetVelocity(G(0, 0, 10));

        return ship;
    }

    static AvoidanceBody Still(float x, float z, float radius, int avoidanceClass = 1000, bool avoidMe = true) =>
        new()
        {
            Center = G(x, 0, z),
            Radius = radius,
            Craft = true,
            AvoidanceClass = avoidanceClass,
            AvoidMe = avoidMe,
        };

    static AvoidanceBody Self => new() { Center = Vector3.Zero, Radius = 2 };

    static ObstacleAvoidance Scorer(AvoidanceKeys keys, params AvoidanceBody[] others) => new(keys, Self, () => others);

    static void AreClose(Vector3 expected, Vector3 actual, string what = "")
    {
        Assert.IsTrue(Vector3.Distance(expected, actual) < 1e-4f, $"{what} expected {expected}, got {actual}");
    }

    [TestMethod]
    public void AShipHeadingAtAStillShipTakesTheLeftTurnOf30DegreesThatTheScoringRanksFirst()
    {
        // the ship ahead: R = (8 + 2) x 1.1 = 11; the candidates c0 and c7 hit it dead on (-inf), c1 and c2 and the
        // pitches pass at 7.8 inside R, c3 and c4 (30 degrees) pass at 15 outside with 100 cos 30, c5 and c6 score 50;
        // the tie of c3 and c4 goes to the lower index
        var scorer = Scorer(AvoidanceKeys.Game, Still(0, 30, 8));
        var s30 = MathF.Sin(MathF.PI / 6);
        var c30 = MathF.Cos(MathF.PI / 6);
        AreClose(G(-s30, 0, c30), scorer.Steer(Ship(), G(0, 0, 500), Wanted));
    }

    [TestMethod]
    public void ANeighbourWithNothingInTheWayLeavesTheWantedDirection()
    {
        var wanted = G(0.3f, 0, 1);
        AreClose(wanted, Scorer(AvoidanceKeys.Game, Still(0, 500, 5)).Steer(Ship(), G(0, 0, 500), wanted), "far");
        AreClose(wanted, Scorer(AvoidanceKeys.Game, Still(80, 0, 5)).Steer(Ship(), G(0, 0, 500), wanted), "beside");
        AreClose(wanted, Scorer(AvoidanceKeys.Game).Steer(Ship(), G(0, 0, 500), wanted), "alone");
    }

    [TestMethod]
    public void ASlowerNeighbourWeighsDouble()
    {
        // a neighbour at 20 of radius 3, moving away at 0.5 (below 1: weight 2) or at 1.5 (weight 1): the penalty of the
        // 15 degree turn, 100 x 2 x R^2 / dmin^2 against half of it, decides between 30 and 15 degrees
        Vector3 Pick(float speed)
        {
            var neighbour = Still(0, 20, 3) with { Velocity = G(0, 0, speed) };

            return Scorer(AvoidanceKeys.Game, neighbour).Steer(Ship(), G(0, 0, 500), Wanted);
        }

        var (s15, c15) = MathF.SinCos(MathF.PI / 12);
        var (s30, c30) = MathF.SinCos(MathF.PI / 6);
        AreClose(G(-s30, 0, c30), Pick(0.5f), "slow");
        AreClose(G(-s15, 0, c15), Pick(1.5f), "faster");
    }

    [TestMethod]
    public void TheLastPickIsFavoured()
    {
        // a still ship ahead and a little to the game's -X: the right turn of 30 degrees is the best
        var (s30, c30) = MathF.SinCos(MathF.PI / 6);
        var beside = Still(-4, 25, 8);
        var ahead = Still(0, 25, 8);
        // the same ship dead ahead ties the 30 degree turns: a fresh scorer takes the lower index, the left
        AreClose(G(-s30, 0, c30), Scorer(AvoidanceKeys.Game, ahead).Steer(Ship(), G(0, 0, 500), Wanted), "fresh");
        // a scorer that went right goes right again, for its 50 x alignment bonus with the last pick
        var bodies = new List<AvoidanceBody> { beside };
        var scorer = new ObstacleAvoidance(AvoidanceKeys.Game, Self, () => bodies);
        AreClose(G(s30, 0, c30), scorer.Steer(Ship(), G(0, 0, 500), Wanted), "first");
        bodies[0] = ahead;
        AreClose(G(s30, 0, c30), scorer.Steer(Ship(), G(0, 0, 500), Wanted), "hysteresis");
    }

    [TestMethod]
    public void ABestScoreBelowZeroScalesTheDirectionByThreeQuarters()
    {
        // a huge still ship ahead: every candidate passes inside R, the 60 degree turns score least badly
        var (s60, c60) = MathF.SinCos(MathF.PI / 3);
        var steer = Scorer(AvoidanceKeys.Game, Still(0, 40, 30)).Steer(Ship(), G(0, 0, 500), Wanted);
        AreClose(G(-s60 * 0.75f, 0, c60 * 0.75f), steer);
    }

    [TestMethod]
    public void AShipYieldsOnlyToObjectsOfItsOwnAvoidanceClassOrHigherOrOfSpeedBelowOne()
    {
        // the ship's class is 1000
        AreClose(
            Wanted,
            Scorer(AvoidanceKeys.Game, Still(0, 30, 5, 500) with { Velocity = G(0, 0, 5) })
                .Steer(Ship(), G(0, 0, 500), Wanted),
            "lower class, moving"
        );
        Assert.AreNotEqual(
            Wanted,
            Scorer(AvoidanceKeys.Game, Still(0, 30, 5, 500)).Steer(Ship(), G(0, 0, 500), Wanted),
            "lower class, still"
        );
        Assert.AreNotEqual(
            Wanted,
            Scorer(AvoidanceKeys.Game, Still(0, 30, 5, 2000)).Steer(Ship(), G(0, 0, 500), Wanted),
            "higher class"
        );
    }

    [TestMethod]
    public void AnObjectThatIsNotToBeAvoidedIsIgnored()
    {
        AreClose(
            Wanted,
            Scorer(AvoidanceKeys.Game, Still(0, 30, 5, avoidMe: false)).Steer(Ship(), G(0, 0, 500), Wanted)
        );
    }

    static AvoidanceBody Field(float z, float radius, float collisionRadius = 0) =>
        new()
        {
            Center = G(0, 0, z),
            Radius = radius,
            Field = true,
            CollisionRadius = collisionRadius,
        };

    [TestMethod]
    public void AShipHeadingIntoAFieldsPredictedHitWithTheGoalBeyondItKeepsItsDirectionAndFlagsTheHit()
    {
        // at the impulse speed 10 for 4 s the ship reaches the field of reach (2 + 30) x 1.5 at 50; a still ship dead
        // ahead of it, which would turn the ship, is not scored: the hit returns at once
        var scorer = Scorer(AvoidanceKeys.Game, Still(0, 20, 5), Field(50, 30));
        AreClose(Wanted, scorer.Steer(Ship(), G(0, 0, 500), Wanted));
        Assert.IsTrue(scorer.TakeFieldHit());
        Assert.IsFalse(scorer.TakeFieldHit(), "taking the flag clears it");
    }

    [TestMethod]
    public void AFieldTooFarForTheLookAheadTimeIsNoHit()
    {
        // 100 away: 100 - 10 x 4 is still beyond the reach of 48; at 60 the 4 s look-ahead hits and the stock 1.4 s
        // (60 - 14 beyond the reach of 38.4) does not
        var scorer = Scorer(AvoidanceKeys.Game, Field(100, 30));
        scorer.Steer(Ship(), G(0, 0, 500), Wanted);
        Assert.IsFalse(scorer.TakeFieldHit());
        var near = Scorer(AvoidanceKeys.Game, Field(60, 30));
        near.Steer(Ship(), G(0, 0, 500), Wanted);
        Assert.IsTrue(near.TakeFieldHit());
        var shortLook = Scorer(AvoidanceKeys.Game with { LookAheadTime = 1.4f, SafetyMargin = 1.2f }, Field(60, 30));
        shortLook.Steer(Ship(), G(0, 0, 500), Wanted);
        Assert.IsFalse(shortLook.TakeFieldHit());
    }

    [TestMethod]
    public void AFieldHitCountsOnlyWhereTheGoalLiesOnTheFieldsSide()
    {
        // the goal behind the ship: more than 90 degrees from the way to the field
        var scorer = Scorer(AvoidanceKeys.Game, Field(50, 30));
        scorer.Steer(Ship(), G(0, 0, -500), Wanted);
        Assert.IsFalse(scorer.TakeFieldHit());
    }

    [TestMethod]
    public void AFieldBehindTheShipIsNoHit()
    {
        var ship = Ship();
        var scorer = Scorer(AvoidanceKeys.Game, Field(-20, 30));
        scorer.Steer(ship, G(0, 0, 500), Wanted);
        Assert.IsFalse(scorer.TakeFieldHit());
    }

    [TestMethod]
    public void TheFieldsCollisionRadiusSizesThePrediction()
    {
        // a collision radius of 1 (reach 4.5) is no hit at 45, a collision radius of 30 (reach 48) is; the field's own
        // radius of 5 only widens the query
        var small = Scorer(AvoidanceKeys.Game, Field(45, 5, collisionRadius: 1));
        small.Steer(Ship(), G(0, 0, 500), Wanted);
        Assert.IsFalse(small.TakeFieldHit());
        var large = Scorer(AvoidanceKeys.Game, Field(45, 5, collisionRadius: 30));
        large.Steer(Ship(), G(0, 0, 500), Wanted);
        Assert.IsTrue(large.TakeFieldHit());
    }

    [TestMethod]
    public void ANeighbourHeadingForATargetIsTakenToMoveBy02VelocityPlus08SpeedTowardIt()
    {
        // a neighbour at 20 ahead of radius 4 (R = 6.6, R^2 = 43.56) moving at 5 toward the game's +X. With no target it
        // crosses the ship's way: the wanted path passes at sqrt(80), outside R, so the wanted direction stands. Heading
        // for a target to the game's -X it moves by 0.2 x (5, 0, 0) + 0.8 x 5 x (-1, 0, 0) = (-3, 0, 0): the wanted path
        // passes at sqrt(33.03), inside R, so c0 and c7 score -31.9 and the 15 degree left turn passes at 0.85 (-6050);
        // the right turns and the pitches pass outside R and keep 96.6 (c2 the lowest index) or 86.6
        var (s15, c15) = MathF.SinCos(MathF.PI / 12);
        var crossing = Still(0, 20, 4) with { Velocity = G(5, 0, 0) };
        AreClose(Wanted, Scorer(AvoidanceKeys.Game, crossing).Steer(Ship(), G(0, 0, 500), Wanted), "no target");
        var heading = crossing with { MoveTarget = G(-100, 0, 20) };
        AreClose(G(s15, 0, c15), Scorer(AvoidanceKeys.Game, heading).Steer(Ship(), G(0, 0, 500), Wanted), "target");
    }

    [TestMethod]
    public void ANeighbourWithATargetWithinOneUnitCountsAsStill()
    {
        // the same neighbour with its target 0.5 away takes nothing off the relative path: it is still and dead ahead, so
        // c0, c7, and the pitches fail and the left turn of 30 degrees (86.6, the lowest index of the tie) is taken
        var (s30, c30) = MathF.SinCos(MathF.PI / 6);
        var near = Still(0, 20, 4) with { Velocity = G(5, 0, 0), MoveTarget = G(0.5f, 0, 20) };
        AreClose(G(-s30, 0, c30), Scorer(AvoidanceKeys.Game, near).Steer(Ship(), G(0, 0, 500), Wanted));
    }

    [TestMethod]
    public void AStationAheadVetoesTheVerticalCandidatesAndAShipDoesNot()
    {
        // an object of radius 8 at (0, 8, 25) of R = 11: c0 and c1 and c2 pass inside R, c8 (up) passes at 1.3 inside R, c3
        // and c4 pass at 14.8 (86.6) and c9 (down) at 11.3 (96.6). A ship takes c9; a station has c8 and c9 vetoed, so
        // the left turn of 30 degrees is taken
        var (s15, c15) = MathF.SinCos(MathF.PI / 12);
        var (s30, c30) = MathF.SinCos(MathF.PI / 6);
        var above = Still(0, 25, 8) with { Center = G(0, 8, 25) };
        AreClose(G(0, -s15, c15), Scorer(AvoidanceKeys.Game, above).Steer(Ship(), G(0, 0, 500), Wanted), "ship");
        AreClose(
            G(-s30, 0, c30),
            Scorer(AvoidanceKeys.Game, above with { Starbase = true }).Steer(Ship(), G(0, 0, 500), Wanted),
            "station"
        );
    }

    [TestMethod]
    public void ANeighbourWhoseRelativePathIsStillIsScoredAtItsPresentDistance()
    {
        // the ship flies at 5; a neighbour of radius 8 (R = 11) 10 behind it flies at 10 along the same way and catches up
        // (t = 2). The candidate c0 moves at the testing speed of 10, as fast as the neighbour: its relative path is
        // zero, it counts as still at distance 10, inside R, and scores 100 - 100 x 121 / 100 = -21. The other candidates
        // score -26.5 or less, so c0 is the best and, below 0, is scaled by 0.75
        var ship = FlightFixtures.Cruiser(Vector3.Zero, MathF.PI);
        ship.SetVelocity(G(0, 0, 5));
        var behind = Still(0, -10, 8) with { Velocity = G(0, 0, 10) };
        AreClose(G(0, 0, 0.75f), Scorer(AvoidanceKeys.Game, behind).Steer(ship, G(0, 0, 500), Wanted));
    }

    [TestMethod]
    public void ANeighbourAtSpeedExactlyOneIsYieldedToAtAnyClassAndAboveOneIsNot()
    {
        // the class gate passes a speed of at most 1.0: a lower-class neighbour at 1.0 turns the ship, at 1.01 it does not
        Vector3 Pick(float speed) =>
            Scorer(AvoidanceKeys.Game, Still(0, 30, 5, 500) with { Velocity = G(0, 0, speed) })
                .Steer(Ship(), G(0, 0, 500), Wanted);

        Assert.AreNotEqual(Wanted, Pick(1f), "at 1.0");
        AreClose(Wanted, Pick(1.01f), "at 1.01");
    }

    [TestMethod]
    public void AWantedDirectionOfZeroStoresZeroAsTheLastPick()
    {
        // a scorer that went right (a ship a little to the game's -X) and then had a zero wanted direction starts the next
        // pick without the 50 x alignment bonus: the ship dead ahead takes the left turn of 30 degrees, the lowest index
        var (s30, c30) = MathF.SinCos(MathF.PI / 6);
        var bodies = new List<AvoidanceBody> { Still(-4, 25, 8) };
        var scorer = new ObstacleAvoidance(AvoidanceKeys.Game, Self, () => bodies);
        AreClose(G(s30, 0, c30), scorer.Steer(Ship(), G(0, 0, 500), Wanted), "right");
        AreClose(Vector3.Zero, scorer.Steer(Ship(), G(0, 0, 500), Vector3.Zero), "zero");
        bodies[0] = Still(0, 25, 8);
        AreClose(G(-s30, 0, c30), scorer.Steer(Ship(), G(0, 0, 500), Wanted), "after");
    }

    // The world's wiring of the avoidance: a ship at the middle of the stock band flying at 10 toward the game's +Z, and
    // one other unit, `other`, given in the game's frame relative to the ship. The ship's steering is read through its
    // flight's avoidance seam.
    static Vector3 Steered(
        Vector3 other,
        float radius,
        AvoidanceTraits? shipTraits = null,
        AvoidanceTraits? otherTraits = null,
        bool station = false,
        Vector3? otherVelocity = null,
        Vector3? otherGoal = null,
        Vector3? otherIntentTo = null,
        float lookAhead = 4f,
        params Obstacle[] obstacles
    )
    {
        var y = CombatBand.Stock.Middle;
        var ship = Placed("s", 1, 0) with { Position = new Vector3(0, y, 0) };
        var neighbour = Placed("n", 1, 0) with { Position = G(other.X, y + other.Y, other.Z) };
        var shipMotion = FlightFixtures.Cruiser(ship.Position, MathF.PI);
        shipMotion.SetVelocity(G(0, 0, 10));
        var moving = otherVelocity != null;
        var neighbourMotion = moving ? FlightFixtures.Cruiser(neighbour.Position) : null;
        neighbourMotion?.SetVelocity(otherVelocity!.Value);
        var world = new CombatWorld(
            new UnitPoses([ship, neighbour]),
            [
                new CombatUnit
                {
                    Unit = ship,
                    Profile = Profile(),
                    DefinitionName = "s.odf",
                    Radius = 0,
                    ModelRadius = 2,
                    Avoidance = shipTraits ?? AvoidanceTraits.Default,
                    Mobile = true,
                    Motion = shipMotion,
                },
                new CombatUnit
                {
                    Unit = neighbour,
                    Profile = Profile(),
                    DefinitionName = "n.odf",
                    Radius = 0,
                    ModelRadius = radius,
                    Avoidance = otherTraits ?? AvoidanceTraits.Default,
                    Starbase = station,
                    Mobile = moving,
                    Motion = neighbourMotion,
                },
            ],
            1,
            () => 0,
            (_, _, pose) => pose.Position
        )
        {
            Avoidance = AvoidanceKeys.Game with { LookAheadTime = lookAhead },
            Obstacles = obstacles.Select(o => o with { Center = o.Center + new Vector3(0, y, 0) }).ToArray(),
        };
        if (otherGoal is { } goal)
        {
            world.Move(neighbour, goal.X, -goal.Z);
        }

        if (otherIntentTo is { } to)
        {
            // a move intent, as the attack maneuver and the tether return set it: the world's frame, no order
            world.State(neighbour)!.Intent = new MoveIntent
            {
                Kind = MoveKind.MoveTo,
                X = to.X,
                Y = y,
                Z = -to.Z,
            };
        }

        var steer = world.State(ship)!.Motion!;

        return steer.Avoidance!(steer, G(0, y, 1000), Wanted);
    }

    [TestMethod]
    public void TheWorldGivesANeighbourTheMoveTargetOfItsOrder()
    {
        // the neighbour of the blend test, flown by the world: its move order's goal is its move target
        var (s15, c15) = MathF.SinCos(MathF.PI / 12);
        var other = new Vector3(0, 0, 20);
        AreClose(Wanted, Steered(other, 4, otherVelocity: G(5, 0, 0)), "no order");
        AreClose(
            G(s15, 0, c15),
            Steered(other, 4, otherVelocity: G(5, 0, 0), otherGoal: new Vector3(-100, 0, 20)),
            "order"
        );
    }

    [TestMethod]
    public void TheWorldGivesANeighbourTheMoveTargetOfItsMoveIntentWhereItHasNoOrder()
    {
        // the same neighbour as the order test, 20 ahead at speed 5 toward +X, its intent to move to (-100, 20): the
        // 0.2 x velocity + 0.8 x speed toward the target blend moves it by (-3, 0, 0) a second instead of (5, 0, 0),
        // which crosses the ship's path, so the ship turns by the same 15 degrees as for the order
        var (s15, c15) = MathF.SinCos(MathF.PI / 12);
        var other = new Vector3(0, 0, 20);
        AreClose(
            G(s15, 0, c15),
            Steered(other, 4, otherVelocity: G(5, 0, 0), otherIntentTo: new Vector3(-100, 0, 20)),
            "intent"
        );
    }

    [TestMethod]
    public void TheWorldGivesAStationTheVerticalVetoAndAUnitItsAvoidMe()
    {
        var (s15, c15) = MathF.SinCos(MathF.PI / 12);
        var (s30, c30) = MathF.SinCos(MathF.PI / 6);
        var above = new Vector3(0, 8, 25);
        AreClose(G(0, -s15, c15), Steered(above, 8), "ship");
        AreClose(G(-s30, 0, c30), Steered(above, 8, station: true), "station");
        AreClose(Wanted, Steered(above, 8, otherTraits: new AvoidanceTraits(false, 1000)), "avoidMe 0");
        AreClose(
            Wanted,
            Steered(above, 8, otherTraits: new AvoidanceTraits(true, 500), otherVelocity: G(5, 0, 0)),
            "lower class, moving"
        );
    }

    [TestMethod]
    public void TheWorldGivesAShipItsCollisionRadiusForTheFieldPrediction()
    {
        // a field of radius 20 at 55 with a look-ahead of 1 s: the ship's model radius of 2 predicts no hit (55 - 10 is
        // beyond the reach of 33), so the ship at 20 ahead turns away; a collision radius of 100 (reach 180) is a hit,
        // which keeps the wanted direction
        var field = new Obstacle(new Vector3(0, 0, -55), 20, true);
        var plain = Steered(new Vector3(0, 0, 20), 4, lookAhead: 1f, obstacles: field);
        Assert.AreNotEqual(Wanted, plain, "model radius");
        AreClose(
            Wanted,
            Steered(
                new Vector3(0, 0, 20),
                4,
                shipTraits: new AvoidanceTraits(true, 1000, 100),
                lookAhead: 1f,
                obstacles: field
            ),
            "collision radius"
        );
    }

    readonly TempInstall _install = new();

    [TestCleanup]
    public void CleanUp() => _install.CleanUp();

    ResolvedDefinition Definition(string text) =>
        ResolvedDefinition.Resolve(new GameFileSystem(_install.MakeInstall(("addon/u.odf", text))), "u.odf");

    [TestMethod]
    public void ADefinitionsAvoidMeAvoidanceClassAndCollisionRadiusAreRead()
    {
        var reports = new List<string>();
        Assert.AreEqual(
            new AvoidanceTraits(false, 500, 35f),
            AvoidanceTraits.Read(Definition("avoidMe = 0\navoidanceClass = 500\ncollisionRadius = 35"), reports.Add)
        );
        Assert.AreEqual(AvoidanceTraits.Default, AvoidanceTraits.Read(Definition("hull = 1"), reports.Add), "none set");
        Assert.AreEqual(
            AvoidanceTraits.Default,
            AvoidanceTraits.Read(Definition("collisionRadius = -4"), reports.Add),
            "radius not above 0"
        );
        Assert.AreEqual(0, reports.Count);
    }

    [TestMethod]
    public void ANonFiniteCollisionRadiusIsReportedOncePerClassAndCountsAs0()
    {
        foreach (var value in new[] { "1e40", "-1e40", "NaN", "Infinity" })
        {
            var reports = new List<string>();
            var definition = Definition($"collisionRadius = {value}");
            Assert.AreEqual(AvoidanceTraits.Default, AvoidanceTraits.Read(definition, reports.Add), value);
            Assert.AreEqual(1, reports.Count, value);
            StringAssert.Contains(reports[0], "collisionRadius");

            reports.Clear();
            var byClass = AvoidanceTraits.ByClass(
                new Dictionary<string, ResolvedDefinition> { ["c"] = definition },
                reports.Add
            );
            byClass("c");
            byClass("c");
            Assert.AreEqual(1, reports.Count, $"{value}: once per class");
        }
    }

    [TestMethod]
    public void ABadAvoidanceValueTakesTheDefaultAndIsReportedOncePerClass()
    {
        var reports = new List<string>();
        var definition = Definition("avoidMe = 1.5\navoidanceClass = high\ncollisionRadius = wide");
        Assert.AreEqual(AvoidanceTraits.Default, AvoidanceTraits.Read(definition, reports.Add));
        Assert.AreEqual(3, reports.Count, "one report per bad key");
        StringAssert.Contains(reports[1], "avoidanceClass");

        reports.Clear();
        var byClass = AvoidanceTraits.ByClass(
            new Dictionary<string, ResolvedDefinition> { ["c"] = definition },
            reports.Add
        );
        byClass("c");
        byClass("C");
        Assert.AreEqual(3, reports.Count, "the second lookup of the class reports nothing");
        Assert.AreEqual(AvoidanceTraits.Default, byClass("none"), "a class without a definition");
        Assert.AreEqual(3, reports.Count);
    }

    [TestMethod]
    public void ABadAvoidanceValueIsLoggedOnceAsACombatWarnAndStillReachesTheCaller()
    {
        var reports = new List<string>();
        using var log = new CapturedLog();
        var byClass = AvoidanceTraits.ByClass(
            new Dictionary<string, ResolvedDefinition> { ["c"] = Definition("avoidMe = yes") },
            reports.Add
        );
        byClass("c");
        byClass("c");

        Assert.AreEqual(1, reports.Count, "the gap text reaches the caller once");
        Assert.AreEqual(1, log.Lines.Count, log.Text);
        StringAssert.Contains(log.Lines[0], " WARN combat: " + reports[0]);
    }

    // The world under test: a ship at the middle of the stock band flying to a goal 1000 ahead, with a still ship in the way
    // and, as given, a field.
    static (CombatWorld World, PlacedObject Ship) Flight(AvoidanceKeys? keys, bool field)
    {
        var y = CombatBand.Stock.Middle;
        var ship = Placed("s", 1, 0) with { Position = new Vector3(0, y, 0) };
        var neighbour = Placed("n", 1, 0) with { Position = new Vector3(0, y, 90) };
        var poses = new UnitPoses([ship, neighbour]);
        var world = new CombatWorld(
            poses,
            [
                new CombatUnit
                {
                    Unit = ship,
                    Profile = Profile(),
                    DefinitionName = "s.odf",
                    Radius = 0,
                    ModelRadius = 5,
                    Mobile = true,
                    Motion = FlightFixtures.Cruiser(ship.Position),
                },
                new CombatUnit
                {
                    Unit = neighbour,
                    Profile = Profile(),
                    DefinitionName = "n.odf",
                    Radius = 0,
                    ModelRadius = 5,
                    Mobile = false,
                },
            ],
            1,
            () => 0,
            (_, _, pose) => pose.Position
        )
        {
            Avoidance = keys ?? AvoidanceKeys.Stock,
            Obstacles = field ? [new Obstacle(new Vector3(0, y, 400), 20, true)] : [],
        };
        world.Move(ship, 0, 1000);

        return (world, ship);
    }

    // The widest the ship strays from the line to its goal and the furthest it gets along it, over the seconds.
    static (float MostX, float MostZ) Fly(CombatWorld world, PlacedObject ship, double seconds)
    {
        float mostX = 0f,
            mostZ = 0f;
        for (var i = 0; i < (int)(seconds / CombatWorld.Tick); i++)
        {
            world.Step(CombatWorld.Tick);
            var at = world.State(ship)!.Position!.Value;
            mostX = MathF.Max(mostX, MathF.Abs(at.X));
            mostZ = MathF.Max(mostZ, at.Z);
        }

        return (mostX, mostZ);
    }

    [TestMethod]
    public void WithTheKeyOffAShipFliesStraightThroughAShipInItsWay()
    {
        var (world, ship) = Flight(AvoidanceKeys.Stock, field: false);
        Assert.AreEqual(0f, Fly(world, ship, 8).MostX, 1e-3f);
    }

    [TestMethod]
    public void WithTheKeyOnAShipSteersAroundAShipInItsWay()
    {
        var (world, ship) = Flight(AvoidanceKeys.Game, field: false);
        Assert.IsTrue(Fly(world, ship, 8).MostX > 1f);
    }

    [TestMethod]
    public void WithTheKeyOnAShipHoldsBackAtAFieldItWouldHit()
    {
        // the field of radius 20 at 400: with the key off the ship runs on to the field's push-out at 375, with it on the
        // predicted hit (the field's center within 4 x speed + the radii of the ship) holds it each other tick
        var (world, ship) = Flight(AvoidanceKeys.Game, field: true);
        var (_, held) = Fly(world, ship, 40);
        var (open, openShip) = Flight(AvoidanceKeys.Stock, field: true);
        var (_, free) = Fly(open, openShip, 40);
        Assert.IsTrue(free > 370, $"with the key off the ship reaches the field, at {free}");
        Assert.IsTrue(held < free - 5, $"with the key on it stops short, at {held} against {free}");
    }
}
