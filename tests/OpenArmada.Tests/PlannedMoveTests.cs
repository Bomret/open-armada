using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Formats;
using OpenArmada.Sandbox;
using static OpenArmada.Tests.CombatFixtures;

namespace OpenArmada.Tests;

// The move order over the planner: the order list and its replans (0x42ded0) and the combat world's use of them.
[TestClass]
public class PlannedMoveTests
{
    static readonly MapBounds Map = new()
    {
        MinX = 0,
        MaxX = 2000,
        MinY = 0,
        MaxY = 100,
        MinZ = 0,
        MaxZ = 2000,
    };

    static readonly PathPlanConfig Shipped = new() { QuadtreeResolution = 50, DefaultTerrainValue = 100 };

    // an impassable wall at x 950 to 1050, z (map) 0 to 1600: with the touching cells x 900 to 1100, z up to 1650
    static readonly PathWrite Wall = new(950, 0, 1050, 1600, 0, -1);

    static PathPlanner Planner() => PathPlanner.Create(Map, Shipped, [Wall])!;

    // the sandbox's frame mirrors Z
    static Vector3 At(float x, float z, float y = 38.5f) => new(x, y, -z);

    // ---- the order list ----

    [TestMethod]
    public void TheFirstUpdateLeadsToTheOrdersFirstPointAndTheSecondPassesItAndPlansToTheGoal()
    {
        var ship = FlightFixtures.Cruiser(At(500, 1000));
        var move = new PlannedMove([At(500, 1000), At(1500, 1000)]);

        Assert.IsTrue(move.Update(ship, Planner(), 0, CombatBand.Stock));
        Assert.AreEqual(0, move.Index);
        Assert.AreEqual(At(500, 1000), ship.Order!.Goal, "the first point is where the ship stood");

        Assert.IsTrue(move.Update(ship, Planner(), 0.02, CombatBand.Stock));

        Assert.AreEqual(1, move.Index, "the first point is passed once the ship is within the lead distance of it");
        Assert.AreEqual(At(1500, 1000), ship.Order.Goal);
    }

    [TestMethod]
    public void AGoalWithNoPathMakesTheUpdateSayNoAndStartsNoPathToIt()
    {
        var ship = FlightFixtures.Cruiser(At(500, 1000));
        var move = new PlannedMove([At(500, 1000), At(1000, 800)]);
        var planner = Planner();
        move.Update(ship, planner, 0, CombatBand.Stock);

        Assert.IsFalse(move.Update(ship, planner, 0.02, CombatBand.Stock));

        Assert.AreEqual(At(500, 1000), ship.Order!.Goal, "the earlier path to the ship's own point stays");
    }

    [TestMethod]
    public void AShipInAnImpassableCellHasNoPathToItsOwnPointSoItsMoveOrderPlansNothing()
    {
        // the game plans the first path to the list's first point (the ship's position); an impassable one has none
        var ship = FlightFixtures.Cruiser(At(1000, 800));
        var move = new PlannedMove([At(1000, 800), At(1500, 1000)]);

        Assert.IsFalse(move.Update(ship, Planner(), 0, CombatBand.Stock));
        Assert.IsNull(ship.Order);
    }

    [TestMethod]
    public void WithoutAPlannerThePathIsTheTwoPoints()
    {
        var ship = FlightFixtures.Cruiser(At(500, 1000));
        var move = new PlannedMove([At(500, 1000), At(1500, 1000)]);
        move.Update(ship, null, 0, CombatBand.Stock);

        Assert.IsTrue(move.Update(ship, null, 0.02, CombatBand.Stock));

        Assert.AreEqual(At(1500, 1000), ship.Order!.Goal);
    }

    // ---- the stuck test ----

    // A ship planned across the wall that never moves again; the count starts at second 0.
    static (PlannedMove Move, FlightModel Ship, PathPlanner Planner) Stuck()
    {
        var ship = FlightFixtures.Cruiser(At(500, 1000));
        var planner = Planner();
        var move = new PlannedMove([At(500, 1000), At(1500, 1000)]);
        move.Update(ship, planner, 0, CombatBand.Stock);
        move.Update(ship, planner, 0, CombatBand.Stock);
        move.Update(ship, planner, 0, CombatBand.Stock); // the first pass with a path stores the second and the place

        return (move, ship, planner);
    }

    [TestMethod]
    public void AShipThatMakesNoProgressForMoreThanTwelveWholeSecondsReplansFromItsPositionToTheOrdersFirstPoint()
    {
        var (move, ship, planner) = Stuck();
        var first = ship.Order;

        move.Update(ship, planner, 12.99, CombatBand.Stock);
        Assert.AreSame(first, ship.Order, "12 whole seconds is not more than 12");

        move.Update(ship, planner, 13.0, CombatBand.Stock);

        Assert.AreNotSame(first, ship.Order);
        Assert.AreEqual(0, move.Index, "the game drops the path and its order index to 0 (0x42ded0)");
        // the path leads to the order's first point, where the ship stood at the order; the order's end stays the goal
        Assert.AreEqual(At(500, 1000), ship.Order!.Goal);
        Assert.AreEqual(FlightModel.Game(At(1500, 1000)), ship.Order.FinalGame);
    }

    [TestMethod]
    public void AShipThatMovedMoreThanOneUnitRestartsTheCount()
    {
        var (move, ship, planner) = Stuck();
        var first = ship.Order;

        ship.Push(new Vector3(0, 0, -2)); // 2 units: a squared distance of 4, over 1
        move.Update(ship, planner, 10, CombatBand.Stock);
        move.Update(ship, planner, 22.9, CombatBand.Stock);
        Assert.AreSame(first, ship.Order, "12 whole seconds after second 10");

        move.Update(ship, planner, 23, CombatBand.Stock);

        Assert.AreNotSame(first, ship.Order);
    }

    [TestMethod]
    public void AMoveOfNoMoreThanOneUnitDoesNotRestartTheCount()
    {
        var (move, ship, planner) = Stuck();
        var first = ship.Order;

        ship.Push(new Vector3(0, 0, -1)); // a squared distance of 1 is not over 1
        move.Update(ship, planner, 10, CombatBand.Stock);
        move.Update(ship, planner, 13, CombatBand.Stock);

        Assert.AreNotSame(first, ship.Order);
    }

    [TestMethod]
    public void AShipWithinTheLeadDistanceOfTheGoalNeverCountsAsStuck()
    {
        var ship = FlightFixtures.Cruiser(At(500, 1000));
        var planner = Planner();
        var move = new PlannedMove([At(500, 1000), At(500, 1100)]);
        move.Update(ship, planner, 0, CombatBand.Stock);
        move.Update(ship, planner, 0, CombatBand.Stock);
        move.Update(ship, planner, 0, CombatBand.Stock);
        ship.Push(new Vector3(0, 0, -100)); // at the goal, inside the lead distance 150 (a push is not progress either)
        var order = ship.Order;

        move.Update(ship, planner, 60, CombatBand.Stock);

        Assert.AreSame(order, ship.Order);
    }

    [TestMethod]
    public void OnceTheShipIsWithinTheLeadDistanceOfTheOrdersFirstPointItReplansToTheGoal()
    {
        var (move, ship, planner) = Stuck();
        move.Update(ship, planner, 13, CombatBand.Stock); // the path leads to the first point, under the lead distance

        // the ship stands at the first point: the next update passes it and plans to the goal again
        move.Update(ship, planner, 13.02, CombatBand.Stock);

        Assert.AreEqual(1, move.Index);
        Assert.AreEqual(At(1500, 1000), ship.Order!.Goal);
    }

    // ---- the combat world ----

    static (CombatWorld World, UnitPoses Poses, PlacedObject Ship, FlightModel Motion) Flying(
        PathPlanner? planner,
        float x = 500,
        float z = 1000
    )
    {
        var ship = Placed("s", 1, x, -z);
        // a lead distance of the stock destphys (the square root of 150), not the fixtures' 150
        var motion = new FlightModel(
            FlightFixtures.Values(
                FlightFixtures.Fast.With(("impulseSpeed", 10)),
                FlightFixtures.Fast.With(("impulseSpeed", 10))
            ) with
            {
                PathLeadDistance = Math.Sqrt(150),
            },
            FlightFixtures.Pose(ship.Position)
        );
        var poses = new UnitPoses([ship]);
        var world = new CombatWorld(
            poses,
            [
                new CombatUnit
                {
                    Unit = ship,
                    Profile = Profile(null, systems: [Sys("Engines")]),
                    DefinitionName = "s.odf",
                    Radius = 0,
                    ModelRadius = 0,
                    Mobile = true,
                    Motion = motion,
                },
            ],
            1,
            () => 0,
            (_, _, pose) => pose.Position
        )
        {
            Planner = planner,
        };

        return (world, poses, ship, motion);
    }

    [TestMethod]
    public void AMoveAcrossTheWallIsTheGamesPathAndTheShipFliesAroundItsEnd()
    {
        var (world, poses, ship, motion) = Flying(Planner());
        world.Move(ship, 1500, -1000);
        Assert.IsNotNull(motion.Order);

        var arrived = false;
        for (var tick = 0; tick < 20000 && !arrived; tick++)
        {
            world.Step(CombatWorld.Tick);
            var p = poses.Pose(ship).Position;
            // the wall as written is x 950 to 1050 and z up to 1600; the carrot's lead cuts the planned corners a little
            Assert.IsFalse(
                p.X > 951 && p.X < 1049 && -p.Z > 1 && -p.Z < 1599,
                $"the ship flew into the wall at {p} (tick {tick})"
            );
            arrived = world.State(ship)!.PlayerMove == null;
        }

        Assert.IsTrue(arrived, "the order ends at the goal");
        Assert.IsTrue(Vector3.Distance(poses.Pose(ship).Position, At(1500, 1000)) < 45);
    }

    [TestMethod]
    public void WithoutAWallTheShipFliesStraightToItsGoal()
    {
        var (world, poses, ship, _) = Flying(PathPlanner.Create(Map, Shipped, [])!);
        world.Move(ship, 1500, -1000);

        for (var tick = 0; tick < 25 * 60; tick++)
        {
            world.Step(CombatWorld.Tick);
            Assert.IsTrue(Math.Abs(poses.Pose(ship).Position.Z + 1000) < 25, "it never leaves the line to the goal");
        }

        Assert.IsTrue(poses.Pose(ship).Position.X > 600);
    }

    [TestMethod]
    public void AGoalInAnImpassableCellLeavesTheShipHoldingWithNoOrderAndNoFault()
    {
        var (world, poses, ship, motion) = Flying(Planner());
        world.Move(ship, 1000, -800);

        Assert.IsNull(motion.Order);
        Assert.IsNull(world.State(ship)!.PlayerMove);
        world.Step(5);
        Assert.AreEqual(At(500, 1000, 0).X, poses.Pose(ship).Position.X, 1e-3);
        Assert.AreEqual(0, world.State(ship)!.Speed, 1e-3);
    }

    [TestMethod]
    public void AShipStandingInAnImpassableCellHoldsWhateverTheGoalOfItsMoveOrder()
    {
        // the first pass plans to the ship's own point (0x42ded0), which lies in the wall: no move order has a path
        var (world, poses, ship, motion) = Flying(Planner(), 1000, 800);
        world.Move(ship, 1500, -1000);

        Assert.IsNull(motion.Order);
        Assert.IsNull(world.State(ship)!.PlayerMove);
        world.Step(50);
        Assert.AreEqual(1000, poses.Pose(ship).Position.X, 1e-3);
    }

    [TestMethod]
    public void AGoalOffTheGridLeavesTheShipHolding()
    {
        var (world, _, ship, motion) = Flying(Planner());
        world.Move(ship, float.NaN, float.NaN);

        Assert.IsNull(motion.Order);
        Assert.IsNull(world.State(ship)!.PlayerMove);
    }

    [TestMethod]
    public void AShipForcedToStandStillForMoreThanTwelveSecondsReplansInTheWorld()
    {
        var (world, poses, ship, motion) = Flying(Planner());
        world.Move(ship, 1500, -1000);
        var first = motion.Order;
        // something holds the ship: every tick pushes it back to where it was, as a collision would
        var held = poses.Pose(ship).Position;
        for (var tick = 0; tick < 800; tick++)
        {
            world.Step(CombatWorld.Tick);
            motion.Push(held - motion.Pose.Position);
        }

        Assert.AreNotSame(first, motion.Order, "the order's path was planned again");
    }

    [TestMethod]
    public void AMoveWithoutAPlannerIsTheTwoPointPath()
    {
        var (world, _, ship, motion) = Flying(null);
        world.Move(ship, 1500, -1000);

        Assert.AreEqual(new Vector3(1500, world.Band.Middle, -1000), motion.Order!.Goal);
    }

    [TestMethod]
    public void AGroupMoveAcrossManyFieldsPlansEachShipsPathOnceForTheWholeFlight()
    {
        // the planner's cost is bounded: a path is planned when the order is given and again only for a stuck ship, never
        // per tick or per field. Each plan starts a new order on the flight, so the orders a ship flies count its plans.
        // Many fields: a row of impassable squares across the way with gaps, and costed squares around them.
        var writes = new List<PathWrite>();
        for (var z = 100f; z < 2000; z += 200)
        {
            writes.Add(new PathWrite(1000, z, 1050, z + 50, 0, -1));
            writes.Add(new PathWrite(700, z + 100, 750, z + 150, 0, 300));
            writes.Add(new PathWrite(1300, z + 100, 1350, z + 150, 0, 300));
        }

        var planner = PathPlanner.Create(Map, Shipped, writes)!;
        var ships = Enumerable.Range(0, 4).Select(i => Placed($"s{i}", 1, 300, -(850f + i * 100))).ToList();
        var motions = ships
            .Select(s => new FlightModel(
                FlightFixtures.Values(
                    FlightFixtures.Fast.With(("impulseSpeed", 10)),
                    FlightFixtures.Fast.With(("impulseSpeed", 10))
                ) with
                {
                    PathLeadDistance = Math.Sqrt(150),
                },
                FlightFixtures.Pose(s.Position)
            ))
            .ToList();
        var poses = new UnitPoses(ships);
        var world = new CombatWorld(
            poses,
            ships.Select(
                (s, i) =>
                    new CombatUnit
                    {
                        Unit = s,
                        Profile = Profile(null, systems: [Sys("Engines")]),
                        DefinitionName = "s.odf",
                        Radius = 0,
                        ModelRadius = 0,
                        Mobile = true,
                        Motion = motions[i],
                    }
            ),
            1,
            () => 0,
            (_, _, pose) => pose.Position
        )
        {
            Planner = planner,
        };

        world.GroupMove(ships, new Vector2(1700, -1000), 100, world.Band.Middle, Map);
        var flown = motions.Select(m => new List<object> { m.Order! }).ToList();
        for (var tick = 0; tick < 20000 && ships.Any(s => world.State(s)!.PlayerMove != null); tick++)
        {
            world.Step(CombatWorld.Tick);
            for (var i = 0; i < ships.Count; i++)
            {
                if (motions[i].Order is { } order && !ReferenceEquals(order, flown[i][^1]))
                {
                    flown[i].Add(order);
                }
            }
        }

        Assert.IsTrue(ships.All(s => world.State(s)!.PlayerMove == null), "every ship arrives, none is stuck");
        CollectionAssert.AreEqual(new[] { 1, 1, 1, 1 }, flown.Select(f => f.Count).ToArray(), "one plan per ship");
    }

    // ---- the attack's path move ----

    // An armed ship at (500, 1000) that flies, ordered to attack a station at (1500, 1000) beyond the wall.
    static (CombatWorld World, UnitPoses Poses, PlacedObject Attacker, FlightModel Motion) Attacking(
        PathPlanner? planner,
        float attackerX = 500,
        float attackerZ = 1000,
        float targetX = 1500,
        float targetZ = 1000
    )
    {
        var attacker = Placed("a", 1, attackerX, -attackerZ);
        var target = Placed("t", 2, targetX, -targetZ);
        var motion = FlightFixtures.Cruiser(attacker.Position, 0, 10, 40);
        var poses = new UnitPoses([attacker, target]);
        var world = new CombatWorld(
            poses,
            [
                new CombatUnit
                {
                    Unit = attacker,
                    Profile = Profile([Weapon(range: 200)], systems: [Sys("Engines")]),
                    DefinitionName = "a.odf",
                    Radius = 0,
                    ModelRadius = 0,
                    Mobile = true,
                    Motion = motion,
                },
                new CombatUnit
                {
                    Unit = target,
                    Profile = Profile(),
                    DefinitionName = "t.odf",
                    Radius = 0,
                    ModelRadius = 0,
                    Mobile = false,
                },
            ],
            1,
            () => 0,
            (_, _, pose) => pose.Position
        )
        {
            Planner = planner,
        };
        Assert.IsTrue(world.Attack(attacker, target));

        return (world, poses, attacker, motion);
    }

    [TestMethod]
    public void AnAttackTargetBeyond400IsReachedByTheGamesPathAroundTheWall()
    {
        var (world, poses, attacker, motion) = Attacking(Planner());

        for (var tick = 0; tick < 50 * 40; tick++)
        {
            world.Step(CombatWorld.Tick);
            var p = poses.Pose(attacker).Position;
            Assert.IsFalse(
                p.X > 951 && p.X < 1049 && -p.Z > 1 && -p.Z < 1599,
                $"the attacker flew into the wall at {p}"
            );
        }

        Assert.AreEqual(MoveKind.Path, world.State(attacker)!.Intent.Kind);
        Assert.AreEqual(At(1500, 1000, 0), motion.Order!.Goal, "the path leads to the target's position");
        Assert.IsTrue(
            -poses.Pose(attacker).Position.Z > 1000,
            "the ship has turned up the wall's side, not straight at the target"
        );
    }

    [TestMethod]
    public void AnAttackersPathThatMakesNoProgressForMoreThanTwelveSecondsIsPlannedAgain()
    {
        var (world, poses, attacker, motion) = Attacking(Planner());
        world.Step(CombatWorld.Tick); // the intent is chosen at the end of a tick and acted on in the next
        world.Step(CombatWorld.Tick);
        var first = motion.Order;
        var held = poses.Pose(attacker).Position;

        for (var tick = 0; tick < 800; tick++)
        {
            world.Step(CombatWorld.Tick);
            motion.Push(held - motion.Pose.Position);
        }

        Assert.IsNotNull(first);
        Assert.AreNotSame(first, motion.Order);
    }

    [TestMethod]
    public void AnAttackTargetNoPathLeadsToLeavesTheAttackerHoldingWithNoFault()
    {
        // the target stands inside the wall: no path ends there
        var attacker = Placed("a", 1, 500, -1000);
        var target = Placed("t", 2, 1000, -800);
        var motion = FlightFixtures.Cruiser(attacker.Position, 0, 10, 40);
        var poses = new UnitPoses([attacker, target]);
        var world = new CombatWorld(
            poses,
            [
                new CombatUnit
                {
                    Unit = attacker,
                    Profile = Profile([Weapon(range: 200)], systems: [Sys("Engines")]),
                    DefinitionName = "a.odf",
                    Radius = 0,
                    ModelRadius = 0,
                    Mobile = true,
                    Motion = motion,
                },
                new CombatUnit
                {
                    Unit = target,
                    Profile = Profile(),
                    DefinitionName = "t.odf",
                    Radius = 0,
                    ModelRadius = 0,
                    Mobile = false,
                },
            ],
            1,
            () => 0,
            (_, _, pose) => pose.Position
        )
        {
            Planner = Planner(),
        };
        world.Attack(attacker, target);

        world.Step(10);

        Assert.IsNull(motion.Order);
        Assert.AreEqual(500, poses.Pose(attacker).Position.X, 1e-3);
    }

    [TestMethod]
    public void AnAttackTargetWithin400InsideTheWallIsClosedOnStraightWithNoPathPlanned()
    {
        // pins the game's near-target rule (0x428d20): inside the replan distance the attack moves straight at the target
        // and asks the planner nothing, so the ship flies at the wall and the attack stays
        var (world, poses, attacker, motion) = Attacking(Planner(), 700, 800, 1000, 800);
        world.Step(3);

        Assert.IsNotNull(world.State(attacker)!.Order, "the attack stays");
        Assert.IsNull(motion.Order, "no path was planned");
        Assert.IsTrue(
            poses.Pose(attacker).Position.X > 701,
            $"it flies at the wall, at {poses.Pose(attacker).Position}"
        );
    }

    [TestMethod]
    public void AnAttackWithNoPathToItsTargetEndsAndTheShipHoldsEvenOnceTheTargetLeavesTheWall()
    {
        // The game's stop order (0x46eac0) is a pending command 2 with a one-point list that 0x4267a0 hands to 0x426ad0 on
        // the next tick: case 2 replaces the attack (command 5, 6 or 7) by a move to the ship's own position. The attack
        // is gone, as a move order's is when no path leads to its goal.
        var (world, poses, attacker, motion) = Attacking(Planner());
        var state = world.State(attacker)!;
        var target = state.Order!.Unit;
        poses.Record(target, FlightFixtures.Cruiser(At(1000, 800)));

        world.Step(CombatWorld.Tick);
        world.Step(CombatWorld.Tick);

        Assert.IsNull(state.Order, "the attack is gone");
        Assert.IsNull(motion.Order, "the ship holds with no order");
        Assert.AreEqual(500, poses.Pose(attacker).Position.X, 1e-3);

        poses.Record(target, FlightFixtures.Cruiser(At(1500, 1000)));
        world.Step(10);

        Assert.IsNull(state.Order, "nothing gives the attack back");
        Assert.IsNull(motion.Order);
        Assert.AreEqual(500, poses.Pose(attacker).Position.X, 1e-3);
    }
}
