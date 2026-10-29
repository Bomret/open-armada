using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Formats;
using OpenArmada.Sandbox;
using static OpenArmada.Tests.CombatFixtures;

namespace OpenArmada.Tests;

// The group move through the world: the ordered ships (selection order) and a click give each ship its own move order
// to its cell. The world's Z is the game's mirrored, so a click at world z = -700 is the game's z = 700.
[TestClass]
public class GroupMoveTests
{
    const float K = 40;
    const float Mid = 38.5f;
    static readonly MapBounds Map = new()
    {
        MinX = 0,
        MaxX = 4000,
        MinY = 0,
        MaxY = 100,
        MinZ = 0,
        MaxZ = 4000,
    };

    sealed record Rig(CombatWorld World, UnitPoses Poses, PlacedObject[] Ships)
    {
        public UnitCombatState State(int i) => World.State(Ships[i])!;

        public Vector3 Position(int i) => Poses.Pose(Ships[i]).Position;
    }

    // Mobile ships at the given (x, z) with the given heights, driving at 100 units/s; other units are stations.
    static Rig Build(IEnumerable<(float X, float Z, float Y)> ships, int stations = 0)
    {
        var placed = ships
            .Select((s, i) => Placed($"s{i}", 1, s.X, s.Z) with { Position = new Vector3(s.X, s.Y, s.Z) })
            .ToArray();
        var posts = Enumerable.Range(0, stations).Select(i => Placed($"post{i}", 1, 3000 + i, 3000)).ToArray();
        var poses = new UnitPoses(placed.Concat(posts));
        var units = placed
            .Select(p => new CombatUnit
            {
                Unit = p,
                Profile = Profile(systems: new[] { Sys("Engines") }),
                DefinitionName = "s.odf",
                Radius = 0,
                Mobile = true,
                Motion = new ShipMotion(
                    new PhysicsValues
                    {
                        ImpulseSpeed = 100,
                        YawRotationRate = 3,
                        MaxRoll = null,
                    },
                    p.Position,
                    0
                ),
            })
            .Concat(
                posts.Select(p => new CombatUnit
                {
                    Unit = p,
                    Profile = Profile(),
                    DefinitionName = "p.odf",
                    Radius = 0,
                    Mobile = false,
                })
            );
        var world = new CombatWorld(poses, units, 1, () => 0, (_, _, pose) => pose.Position);

        return new Rig(world, poses, placed);
    }

    static Rig Row(int n, float y = 0) => Build(Enumerable.Range(0, n).Select(i => (1000f + i * 10, -700f, y)));

    static Vector2 Click(float x, float z) => new(x, z);

    static Vector2 Dest(Rig rig, int i) => rig.State(i).PlayerMove is { } m ? new Vector2(m.X, m.Z) : Vector2.Zero;

    // the ship's cell for a block offset in cells, the click at world (2000, -2000)
    static Vector2 Cell(float cols, float rows) => new(2000 + cols * K, -2000 - rows * K);

    [TestMethod]
    public void OneShipGoesToTheClickItself()
    {
        var rig = Row(1);
        rig.World.GroupMove(rig.Ships, Click(2000, -2000), K, Mid, Map);
        Assert.AreEqual(Cell(0, 0), Dest(rig, 0));
    }

    [TestMethod]
    public void TwoThreeFourAndFiveShipsTakeDistinctCellsAtThePositionsTheRuleGives()
    {
        var expected = new Dictionary<int, Vector2[]>
        {
            // world z is the game's mirrored: a row up in the game (larger z) is a smaller world z
            [2] = [Cell(-1, 0), Cell(0, 0)],
            [3] = [Cell(-1, 0), Cell(0, 0), Cell(-1, -1)],
            [4] = [Cell(-1, 0), Cell(-1, -1), Cell(0, -1), Cell(0, 0)],
        };
        foreach (var (n, cells) in expected)
        {
            var rig = Row(n);
            rig.World.GroupMove(rig.Ships, Click(2000, -2000), K, Mid, Map);
            // the row stands along x at one z: the world's cells follow the game's rule with z mirrored
            var got = Enumerable.Range(0, n).Select(i => Dest(rig, i)).ToArray();
            Assert.AreEqual(n, got.Distinct().Count(), $"{n} ships: distinct cells");
            CollectionAssert.AreEquivalent(cells.Select(c => new Vector2(c.X, c.Y)).ToArray(), got, $"{n} ships");
        }

        var five = Row(5);
        five.World.GroupMove(five.Ships, Click(2000, -2000), K, Mid, Map);
        CollectionAssert.AreEquivalent(
            new[] { Cell(-1, 1), Cell(0, 1), Cell(1, 1), Cell(-1, 0), Cell(0, 0) },
            Enumerable.Range(0, 5).Select(i => Dest(five, i)).ToArray()
        );
    }

    [TestMethod]
    public void AllShipsArriveAndEndTheirOrders()
    {
        foreach (var n in new[] { 2, 3, 4, 5 })
        {
            var rig = Row(n);
            rig.World.GroupMove(rig.Ships, Click(2000, -2000), K, Mid, Map);
            var cells = Enumerable.Range(0, n).Select(i => Dest(rig, i)).ToArray();
            rig.World.Step(60);
            for (var i = 0; i < n; i++)
            {
                Assert.IsNull(rig.State(i).PlayerMove, $"{n} ships, ship {i}: order ended");
                var at = rig.Position(i);
                Assert.IsTrue(
                    Vector2.Distance(new Vector2(at.X, at.Z), cells[i]) < 25,
                    $"{n} ships, ship {i}: at its cell"
                );
            }
        }
    }

    [TestMethod]
    public void AShipOnTheLeftEndsInALeftCellAndATieGoesToTheEarlierShip()
    {
        var rig = Build([(1300, -700, 0), (1000, -700, 0)]);
        rig.World.GroupMove(rig.Ships, Click(2000, -2000), K, Mid, Map);
        Assert.IsTrue(Dest(rig, 1).X < Dest(rig, 0).X);
        var tie = Build([(1000, -700, 0), (1000, -700, 0)]);
        tie.World.GroupMove(tie.Ships, Click(2000, -2000), K, Mid, Map);
        Assert.AreEqual(Cell(-1, 0), Dest(tie, 0));
        Assert.AreEqual(Cell(0, 0), Dest(tie, 1));
    }

    [TestMethod]
    public void ASelectionSpreadOver800UnitsFormsTwoGroupsOneSideApart()
    {
        // the bearings to the click (the game's frame, z mirrored) are 0 and pi: the heading pi/2 puts the second
        // group's origin one spacing along +x
        var rig = Build([(0, -2000, 0), (4000, -2000, 0)]);
        rig.World.GroupMove(rig.Ships, Click(2000, -2000), K, Mid, Map);
        Assert.AreEqual(2000f, Dest(rig, 0).X, 1e-2);
        Assert.AreEqual(2000f + K, Dest(rig, 1).X, 1e-2);
        Assert.AreEqual(-2000f, Dest(rig, 1).Y, 1e-2);
    }

    [TestMethod]
    public void TheHeadingIsTheMeanOverEverySelectedUnitIncludingOnesThatTakeNoOrder()
    {
        // the station at world (3000, 3000) stands at the game's (3000, -3000): its bearing to the click (2000, 2000)
        // is atan2(5000, -1000); with the ships' 0 and pi the mean is a third of the sum, and the second group's origin
        // lies one spacing along (sin, -cos)(heading) in the game's frame (z mirrored in the world)
        var rig = Build([(0, -2000, 0), (4000, -2000, 0)], 1);
        var all = rig.Ships.Concat([rig.World.Units.First(u => !u.Mobile).Unit]).ToList();
        rig.World.GroupMove(all, Click(2000, -2000), K, Mid, Map);
        var heading = (0 + Math.PI + Math.Atan2(5000, -1000)) / 3;
        Assert.AreEqual(2000 + K * Math.Sin(heading), Dest(rig, 1).X, 1e-2);
        Assert.AreEqual(-2000 + K * Math.Cos(heading), Dest(rig, 1).Y, 1e-2);
    }

    [TestMethod]
    public void ShipsThatTakeNoMoveOrderTakeNoCellAndDoNotChangeTheGridSize()
    {
        var rig = Build([(1000, -700, 0), (1010, -700, 0), (1020, -700, 0), (1030, -700, 0), (1040, -700, 0)], 1);
        rig.State(2).Derelict = true;
        rig.State(3).DestroySystem("Engines");
        rig.State(4).Dying = true;
        var all = rig.Ships.Concat([rig.World.Units.First(u => !u.Mobile).Unit]).ToList();
        rig.World.GroupMove(all, Click(2000, -2000), K, Mid, Map);
        // two ships take the move: a 2 x 2 grid's first two cells; the others hold no order
        CollectionAssert.AreEquivalent(new[] { Cell(-1, 0), Cell(0, 0) }, new[] { Dest(rig, 0), Dest(rig, 1) });
        foreach (var i in new[] { 2, 3, 4 })
        {
            Assert.IsNull(rig.State(i).PlayerMove, $"ship {i}");
        }

        Assert.IsNull(rig.World.State(all[^1])!.PlayerMove);
    }

    [TestMethod]
    public void AClickNearTheMapsEdgeSendsCellsOutsideItToTheNearestEdgePoint()
    {
        var rig = Build(Enumerable.Range(0, 5).Select(i => (3000f + i * 10, -700f, 0f)));
        // five ships make a 3 x 3 block: the cells one right of the click lie at x = 4035, the row above it (the game's
        // larger z, the world's smaller) at z = -4030; both are outside the map
        rig.World.GroupMove(rig.Ships, Click(3995, -3990), K, Mid, Map);
        var cells = Enumerable.Range(0, 5).Select(i => Dest(rig, i)).ToArray();
        Assert.IsTrue(cells.All(c => c.X <= 4000 && c.Y >= -4000));
        Assert.IsTrue(cells.Any(c => c.X == 4000));
        Assert.IsTrue(cells.Any(c => c.Y == -4000));
    }

    [TestMethod]
    public void EachShipsTetherPointIsItsOwnCellAtItsOwnHeight()
    {
        var rig = Build([(1000, -700, 5), (1010, -700, 20)]);
        rig.World.GroupMove(rig.Ships, Click(2000, -2000), K, Mid, Map);
        for (var i = 0; i < 2; i++)
        {
            var cell = Dest(rig, i);
            Assert.AreEqual(new Vector3(cell.X, rig.Position(i).Y, cell.Y), rig.State(i).Tether);
        }

        Assert.AreEqual(5f, rig.Position(0).Y);
        Assert.AreEqual(20f, rig.Position(1).Y);
    }
}
