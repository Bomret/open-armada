using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Formats;
using OpenArmada.Sandbox;

namespace OpenArmada.Tests;

// The path planner over maps built in code (INV-0001): the search with its quirks, the quadtree's write rule, and
// the plans of a 2000 x 2000 map under the install's shipped RTS_CFG.h values.
[TestClass]
public class PathPlannerTests
{
    const float Height = 38.5f;

    static readonly MapBounds Map = new()
    {
        MinX = 0,
        MaxX = 2000,
        MinY = 0,
        MaxY = 100,
        MinZ = 0,
        MaxZ = 2000,
    };

    // the values of the install's RTS_CFG.h
    static readonly PathPlanConfig Shipped = new()
    {
        QuadtreeResolution = 50,
        DefaultTerrainValue = 100,
        NebulaeSafety = 1.8f,
        AsteroidBeltCost = 201,
        AsteroidBeltDistance = 150,
        BlackholeCost = -1,
        FormationSpacing = 100,
    };

    // a point of the map's frame in the sandbox's, where Z is mirrored
    static Vector3 At(float x, float z) => new(x, Height, -z);

    static (float X, float Z)[] Route(IReadOnlyList<Vector3> points) => points.Select(p => (p.X, -p.Z)).ToArray();

    static PathPlanner Planner(PathPlanConfig? config = null, params PathWrite[] writes) =>
        PathPlanner.Create(Map, config ?? Shipped, writes)!;

    // the wall: impassable from x 950 to 1050 and z 0 to 1600 (the finest cells it touches, edges included, make it
    // x 900 to 1100 and z up to 1650)
    static readonly PathWrite Wall = new(950, 0, 1050, 1600, 0, -1);

    // inside the box by more than 0.01: a route may run along its edge
    static bool Inside(Vector3 p, float x0, float z0, float x1, float z1) =>
        p.X > x0 + 0.01f && p.X < x1 - 0.01f && -p.Z > z0 + 0.01f && -p.Z < z1 - 0.01f;

    // the points of the segments between route points, one unit apart
    static IEnumerable<Vector3> Along(IReadOnlyList<Vector3> route)
    {
        for (var i = 1; i < route.Count; i++)
        {
            var length = Vector3.Distance(route[i - 1], route[i]);
            for (var s = 0f; s < length; s += 1f)
            {
                yield return Vector3.Lerp(route[i - 1], route[i], s / length);
            }
        }
    }

    // ---- the search ----

    static PathNode Node(float x, float z) => new(x, z);

    [TestMethod]
    public void AnOpenNodeIsNeverImproved()
    {
        // S to A costs 10 and S to B to A costs 2; with no heuristic B is reached first, but A is already open
        // (visited, not closed) and the game never improves such a node, so the route keeps the dearer edge.
        PathNode s = Node(0, 0),
            a = Node(100, 0),
            b = Node(0, 100),
            g = Node(200, 0);
        s.Link(a, 10);
        s.Link(b, 1);
        b.Link(a, 1);
        a.Link(g, 1);

        var route = new PathSearch(0).Run(s, g);

        CollectionAssert.AreEqual(new[] { s, a, g }, route);
    }

    [TestMethod]
    public void AClosedNodeIsReopenedWhenALaterRouteBeatsItsF()
    {
        // A closes first (its f is the least), then B reaches it for 2 against the 10 of the first route: A reopens
        // with B as its parent. G was reached from the first A and, being open, keeps its parent A.
        PathNode s = Node(0, 0),
            a = Node(50, 0),
            b = Node(0, 100),
            g = Node(100, 0);
        s.Link(a, 10);
        s.Link(b, 1);
        a.Link(g, 1000);
        b.Link(a, 1);

        var route = new PathSearch(1).Run(s, g);

        CollectionAssert.AreEqual(new[] { s, b, a, g }, route);
    }

    [TestMethod]
    public void AGoalNoEdgeLeadsToHasNoRoute()
    {
        PathNode s = Node(0, 0),
            g = Node(10, 0);
        s.Link(Node(5, 5), 1);

        Assert.IsNull(new PathSearch(1).Run(s, g));
    }

    // ---- the quadtree ----

    static PathQuadtree Tree(PathPlanConfig? config = null) => PathQuadtree.Create(2000, 2000, config ?? Shipped)!;

    // a grid point deep in the map, 1000 units from every edge of the grid's 3000
    static int ValueAt(PathQuadtree tree, float x, float z) => tree.ValueOf(tree.Lookup(x, z));

    [TestMethod]
    public void TheMapEdgesAreImpassableStripsThatReachOneCellPastTheCellPlus500()
    {
        var tree = Tree();

        // the strip is the cell plus 500 wide (550); the cell that touches its edge is written too (600)
        Assert.AreEqual(-1, ValueAt(tree, 599, 1500));
        Assert.AreEqual(100, ValueAt(tree, 601, 1500));
        Assert.AreEqual(-1, ValueAt(tree, 2401, 1500));
        Assert.AreEqual(100, ValueAt(tree, 2399, 1500));
        Assert.AreEqual(-1, ValueAt(tree, 1500, 599));
        Assert.AreEqual(-1, ValueAt(tree, 1500, 2401));
    }

    [TestMethod]
    public void ARectangleThatTouchesACellsEdgeWritesIt()
    {
        var tree = Tree();
        tree.Write(1000, 1000, 1000, 1000, 300);

        // the point is the corner of four finest cells; each takes the value
        Assert.AreEqual(300, ValueAt(tree, 975, 975));
        Assert.AreEqual(300, ValueAt(tree, 1025, 975));
        Assert.AreEqual(300, ValueAt(tree, 975, 1025));
        Assert.AreEqual(300, ValueAt(tree, 1025, 1025));
        Assert.AreEqual(100, ValueAt(tree, 925, 975));
    }

    [TestMethod]
    public void ACellValueOnlyGrowsAndImpassableAlwaysWins()
    {
        var tree = Tree();
        tree.Write(1000, 1000, 1000, 1000, 300);
        tree.Write(1000, 1000, 1000, 1000, 198);
        Assert.AreEqual(300, ValueAt(tree, 975, 975), "a lower value does not overwrite");
        tree.Write(1000, 1000, 1000, 1000, 400);
        Assert.AreEqual(400, ValueAt(tree, 975, 975), "a higher one does");
        tree.Write(1000, 1000, 1000, 1000, -1);
        Assert.AreEqual(-1, ValueAt(tree, 975, 975), "impassable wins");
        tree.Write(1000, 1000, 1000, 1000, 5000);
        Assert.AreEqual(-1, ValueAt(tree, 975, 975), "and nothing replaces it");
    }

    [TestMethod]
    public void ACellAtTheDefaultTakesAnyValueEvenALowerOne()
    {
        var tree = Tree();
        tree.Write(1000, 1000, 1000, 1000, 2);
        Assert.AreEqual(2, ValueAt(tree, 975, 975));
        tree.Write(1000, 1000, 1000, 1000, 198);
        Assert.AreEqual(198, ValueAt(tree, 975, 975));
    }

    [TestMethod]
    public void AWriteSetsTheDirtyFlagAndARebuildClearsIt()
    {
        var tree = Tree();
        Assert.IsTrue(tree.Dirty);
        tree.Rebuild();
        Assert.IsFalse(tree.Dirty);
        tree.Write(1000, 1000, 1000, 1000, 300);
        Assert.IsTrue(tree.Dirty);
    }

    [TestMethod]
    public void AFinestCellCountBeyondWhatIsAllocatedGivesNoPlanner()
    {
        Assert.IsNull(PathPlanner.Create(Map, Shipped with { QuadtreeResolution = 0.5f }, []));
    }

    [TestMethod]
    public void TheGridExtentsKeepTheirFractionAsFloats()
    {
        // 0x408e30 stores the extents as floats: 500 + 500 + 2000.5 is 3000.5, not 3000
        var tree = PathQuadtree.Create(2000.5f, 1000.25f, Shipped)!;

        Assert.AreEqual(3000.5f, tree.ExtentX);
        Assert.AreEqual(2000.25f, tree.ExtentZ);
        Assert.AreEqual(3000.25f, tree.ClampX(3000.25f), "a point under the extent stays where it is");
        Assert.AreEqual(2000.1f, tree.ClampZ(2000.1f));
        Assert.AreEqual(
            3000f,
            tree.ClampX(3000.75f),
            "beyond the extent it goes to the extent cut to whole finest cells"
        );
    }

    // ---- the plans ----

    [TestMethod]
    public void AGoalInTheShipsOwnLeafIsTheTwoPointPath()
    {
        var route = Planner().Plan(At(900, 1300), At(1100, 1500), Height)!;

        CollectionAssert.AreEqual(new[] { At(900, 1300), At(1100, 1500) }, route.ToArray());
    }

    [TestMethod]
    public void AcrossOpenSpaceTheShipFliesStraight()
    {
        var start = At(500, 1000);
        var goal = At(1500, 1000);
        var route = Planner().Plan(start, goal, Height)!;

        Assert.AreEqual(start, route[0]);
        Assert.AreEqual(goal, route[^1]);
        foreach (var p in route)
        {
            Assert.AreEqual(Height, p.Y, 1e-3, "the points are at the middle of the band");
            Assert.AreEqual(-1000, p.Z, 1e-3, "and on the straight line");
        }
    }

    [TestMethod]
    public void AShipOrderedAcrossAWallRoutesAroundItsEndOnTheGamesPath()
    {
        var route = Planner(null, Wall).Plan(At(500, 1000), At(1500, 1000), Height)!;

        // Derivation (map frame, the grid being the map plus 500; cell sizes 50, 100, 200, 400, 800 from the finest up):
        // - The wall's cells: x 950 to 1050 touches the finest cells 900 to 1100, z 0 to 1600 those up to 1650. The
        //   quadtree splits every cell the wall covers in part: 200-cells at x 900 to 1100 (z 100 to 1500, impassable),
        //   100-cells at z 1500 to 1600, 50-cells at z 1600 to 1700 (impassable below 1650, passable above), and 200-cells
        //   at x 700 to 900. Away from it the leaves stay large: the ship's is x 300 to 700, z 700 to 1100 (400), the goal's
        //   x 1100 to 1900, z 300 to 1100 (800), and x 1100 to 1900, z 1100 to 1900 lies above it.
        // - The search leaves the ship's leaf to the east (the 200-cell at x 700 to 900, z 900 to 1100), climbs the 200-cells
        //   at x 700 to 900 to z 1700, crosses the 50-cells of z 1650 to 1700 over the wall, and enters the 800-cell above
        //   the goal's. (The north neighbour of the ship's leaf ties with the east one: both reach the 200-cell at z 1300 to
        //   1500 at 316.2 + 400 = 716.2. The search's neighbour and heap order pick the east one; the test pins that.)
        // - The funnel pulls the route straight between the portals. The first portal, x = 700, z 900 to 1100, holds the line
        //   from (500, 1000) to the wall's corner, which crosses x = 700 at z 1325, at its upper end (700, 1100). The corners
        //   of the 50-cells over the wall are the wall's top corners (900 and 1100, 1650: the touching cells widen the wall
        //   by one finest cell each way); from the second one the straight line to the goal is free.
        CollectionAssert.AreEqual(
            new[] { (500f, 1000f), (700f, 1100f), (900f, 1650f), (1100f, 1650f), (1500f, 1000f) },
            Route(route)
        );
        Assert.IsTrue(route.Skip(1).Take(route.Count - 2).All(p => p.Y == Height));
        Assert.IsFalse(Along(route).Any(p => Inside(p, 900, 0, 1100, 1650)), "no point of the route is in the wall");
    }

    [TestMethod]
    public void WithoutRubberBandingTheRouteIsTheCentersOfTheCellsTheSearchPassed()
    {
        var route = Planner(Shipped with { PerformRubberBanding = false }, Wall)
            .Plan(At(500, 1000), At(1500, 1000), Height)!;

        CollectionAssert.AreEqual(
            new[]
            {
                (500f, 1000f),
                (800f, 1000f),
                (800f, 1200f),
                (800f, 1400f),
                (800f, 1600f),
                (925f, 1675f),
                (975f, 1675f),
                (1025f, 1675f),
                (1075f, 1675f),
                (1500f, 1500f),
                (1500f, 1000f),
            },
            Route(route)
        );
    }

    [TestMethod]
    public void WithoutPathPlanningEveryPlanIsTheTwoPointPath()
    {
        var route = Planner(Shipped with { UsePathPlanning = false }, Wall)
            .Plan(At(500, 1000), At(1500, 1000), Height)!;

        CollectionAssert.AreEqual(new[] { At(500, 1000), At(1500, 1000) }, route.ToArray());
    }

    [TestMethod]
    public void AGoalInAnImpassableCellHasNoPathEvenWithoutPathPlanning()
    {
        Assert.IsNull(Planner(null, Wall).Plan(At(500, 1000), At(1000, 800), Height));
        Assert.IsNull(
            Planner(Shipped with { UsePathPlanning = false }, Wall).Plan(At(500, 1000), At(1000, 800), Height)
        );
    }

    [TestMethod]
    public void AGoalThatIsNotANumberIsOffTheGridAndHasNoPath()
    {
        Assert.IsNull(Planner().Plan(At(500, 1000), new Vector3(float.NaN, Height, float.NaN), Height));
    }

    [TestMethod]
    public void AGoalWalledInHasNoPath()
    {
        // a ring of impassable squares around (1000, 1000): 4 sides, each wider than the ring's gap
        var ring = new[]
        {
            new PathWrite(850, 850, 1150, 850, 0, -1),
            new PathWrite(850, 1150, 1150, 1150, 0, -1),
            new PathWrite(850, 850, 850, 1150, 0, -1),
            new PathWrite(1150, 850, 1150, 1150, 0, -1),
        };

        Assert.IsNull(Planner(null, ring).Plan(At(300, 1000), At(1000, 1000), Height));
    }

    [TestMethod]
    public void AShipThatStartsInsideAnImpassableCellCanLeaveIt()
    {
        var route = Planner(null, Wall).Plan(At(1000, 800), At(1500, 1000), Height);

        Assert.IsNotNull(route, "an impassable cell has edges out of it");
    }

    [TestMethod]
    public void AGoalNearTheMapsEdgeIsPlannedAtTheClampedPointAndEndsWhereItWasClicked()
    {
        // 30 units from the edge: planned as if at 100, a free cell, but the route's last point is the click
        var goal = At(30, 1000);
        var route = Planner().Plan(At(500, 1000), goal, Height)!;

        Assert.AreEqual(goal, route[^1]);
        Assert.IsTrue(route.Count >= 2);
    }

    [TestMethod]
    public void TheClampKeepsAGoalOneBufferInsideTheMapAndAnEpsilonShortOfTheHighEdges()
    {
        var planner = Planner();

        Assert.AreEqual(new Vector2(100, 100), planner.ClampToMapEdge(new Vector2(-50, 20)));
        Assert.AreEqual(new Vector2(1000, 1000), planner.ClampToMapEdge(new Vector2(1000, 1000)));
        var high = planner.ClampToMapEdge(new Vector2(2500, 1999));
        Assert.AreEqual(1900f - 0.1f, high.X, 1e-3);
        Assert.AreEqual(1900f - 0.1f, high.Y, 1e-3);
    }

    [TestMethod]
    public void AValueTheMapEdgeKeysChangeMovesTheClamp()
    {
        var planner = Planner(Shipped with { MapEdgeBuffer = 40, MapEdgeEpsilon = 1 });

        Assert.AreEqual(new Vector2(40, 40), planner.ClampToMapEdge(new Vector2(0, 0)));
        Assert.AreEqual(1959f, planner.ClampToMapEdge(new Vector2(5000, 0)).X, 1e-3);
    }

    [TestMethod]
    public void AnObstacleWrittenAfterAPlanTakesEffectOnTheNextOne()
    {
        var planner = Planner();
        var before = planner.Plan(At(500, 1000), At(1500, 1000), Height)!;
        Assert.IsTrue(before.All(p => Math.Abs(p.Z + 1000) < 1e-3), "straight before the wall");

        planner.Write(Wall);
        var after = planner.Plan(At(500, 1000), At(1500, 1000), Height)!;

        Assert.IsFalse(Along(after).Any(p => Inside(p, 900, 0, 1100, 1650)));
        Assert.IsTrue(after.Count > before.Count);
    }

    [TestMethod]
    public void AShipGoesAroundAnExpensiveRegionWhenTheDetourIsCheaper()
    {
        // a square of value 2000 (a nebula of the stock mnebula1) across the straight way
        var cost = new PathWrite(1000, 1000, 1000, 1000, 250, 2000);
        var route = Planner(null, cost).Plan(At(500, 1000), At(1500, 1000), Height)!;

        Assert.IsFalse(Along(route).Any(p => Inside(p, 760, 760, 1240, 1240)), "the route stays out of the square");
    }

    [TestMethod]
    public void AShipFliesThroughAnInexpensiveRegionAndAroundOnlyTheCostlyOne()
    {
        // 198 against the default 100: crossing 500 units costs 99000 against the 100000 of a detour of 500 more, so
        // the straight way across wins
        var cheap = new PathWrite(1000, 1000, 1000, 1000, 250, 198);
        var route = Planner(null, cheap).Plan(At(500, 1000), At(1500, 1000), Height)!;

        Assert.IsTrue(Along(route).Any(p => Inside(p, 760, 760, 1240, 1240)));
    }

    // ---- the plans around each kind of obstacle the map writes ----
    // Each fixture writes one object at (1000, 1000) through PathObstacles and plans (300, 1000) to (1700, 1000). Each is
    // checked against the obstacle's box too. Derivation of the expected routes (map frame, cell sizes 50, 100, 200, 400,
    // 800 from the finest up):
    // - The ship's leaf is x 300 to 700, z 700 to 1100, with its center at z 900; the goal's, for the field and the
    //   black hole and the nebulae, is x 1500 to 1900, z 700 to 1100 (center z 900 too), for the starbase x 1100 to 1900,
    //   z 300 to 1100 (center z 700). Both lie below the object's center line, so the way below is the shorter one.
    // - The field, black hole, and nebulae write a square whose touching cells reach x and z 800 to 1200 (the field's
    //   pad 175 gives 825 to 1175; the black hole's impassable square of 1.5 x its event radius 100 gives 850 to 1150;
    //   a nebula's 1.8 x 100 = 180 gives 820 to 1180). The quadtree splits down to 100-cells around it, so a row of
    //   passable cheap cells of z 700 to 800 runs below it from x 700 to 1300, and the next leaf east, x 1300 to 1500, is
    //   a 200-cell of z 700 to 900. The search takes that row: the cells of the square above it cost 3, 4, or 20 times the
    //   default 100 (the field's pad, the cheap nebula, the dear one) or are impassable, and the way over the top starts
    //   from the ship's leaf at z 900 and has farther to go.
    // - The funnel holds the line to the goal at the portals' ends: the portal from the ship's leaf into the row is
    //   x = 700, z 700 to 800, its upper end (700, 800) bends the route; the portal into the 200-cell is x = 1300, z 700
    //   to 800, its upper end (1300, 800) bends it again; the goal is then in sight. So the route is (300, 1000), (700, 800),
    //   (1300, 800), (1700, 1000) for the three.
    // - The starbase writes only a costly cell (value 198, not impassable), x 900 to 1100, z 900 to 1100. The cell
    //   below it, x 900 to 1100, z 700 to 900, costs the default 100; the portal from the ship's leaf into the 200-cell
    //   x 700 to 900, z 700 to 900 is x = 700, z 700 to 900, whose upper end (700, 900) bends the route, and the portal
    //   from the cell x 900 to 1100, z 700 to 900 into the goal's leaf is x = 1100, z 700 to 900, whose upper end
    //   (1100, 900) bends it again. So the route is (300, 1000), (700, 900), (1100, 900), (1700, 1000).

    static IReadOnlyList<Vector3> AroundObject(IReadOnlyList<PathWrite> writes) =>
        Planner(null, writes.ToArray()).Plan(At(300, 1000), At(1700, 1000), Height)!;

    static readonly (float X, float Z)[] BelowTheObstacle =
    [
        (300f, 1000f),
        (700f, 800f),
        (1300f, 800f),
        (1700f, 1000f),
    ];

    [TestMethod]
    public void AnAsteroidFieldsCoreIsRoutedAround()
    {
        var site = new AsteroidSite
        {
            Label = "f",
            Class = "field",
            Center = At(1000, 1000),
        };

        var route = AroundObject(PathObstacles.Writes(Shipped, [site], [], [], []));

        CollectionAssert.AreEqual(BelowTheObstacle, Route(route));
        Assert.IsFalse(Along(route).Any(p => Inside(p, 925, 925, 1075, 1075)), "the core is impassable");
    }

    [TestMethod]
    public void ABlackHoleIsRoutedAroundByItsEventRadiusTimesTheSafetyCoefficientNotItsGravityRadius()
    {
        var hole = new BlackHole
        {
            Object = CombatFixtures.Placed("h", 0, 1000, -1000),
            Center = At(1000, 1000),
            EventRadius = 100,
            GravityRadius = 450,
        };

        var route = AroundObject(PathObstacles.Writes(Shipped, [], [], [hole], []));

        CollectionAssert.AreEqual(BelowTheObstacle, Route(route));
        Assert.IsFalse(Along(route).Any(p => Inside(p, 850, 850, 1150, 1150)), "1.5 x 100 on each side is impassable");
    }

    [TestMethod]
    public void ANebulaOfCost2000IsRoutedAroundAndOneOfCost400Too()
    {
        Nebula Of(float cost) =>
            new()
            {
                Label = "n",
                Class = "mnebula1",
                Kind = 1,
                Center = At(1000, 1000),
                EffectRadius = 100,
                PathingCost = cost,
            };

        foreach (var cost in new[] { 2000f, 400f })
        {
            var route = AroundObject(PathObstacles.Writes(Shipped, [], [Of(cost)], [], []));

            CollectionAssert.AreEqual(BelowTheObstacle, Route(route), $"cost {cost}");
            Assert.IsFalse(
                Along(route).Any(p => Inside(p, 830, 830, 1170, 1170)),
                $"cost {cost}: the pad is 1.8 x 100"
            );
        }
    }

    [TestMethod]
    public void AStarbasesFootprintIsRoutedAround()
    {
        var station = new PathStation(At(1000, 1000), new Vector3(-60, 0, -60), new Vector3(60, 10, 60));

        var route = AroundObject(PathObstacles.Writes(Shipped, [], [], [], [station]));

        CollectionAssert.AreEqual(new[] { (300f, 1000f), (700f, 900f), (1100f, 900f), (1700f, 1000f) }, Route(route));
        Assert.IsFalse(Along(route).Any(p => Inside(p, 940, 940, 1060, 1060)));
    }

    [TestMethod]
    public void ARouteEndsAtTheShipAndTheGoalWhateverThePathBetween()
    {
        var start = new Vector3(500, 77f, -1000);
        var goal = new Vector3(1500, 12f, -1000);
        var route = Planner(null, Wall).Plan(start, goal, Height)!;

        Assert.AreEqual(start, route[0]);
        Assert.AreEqual(goal, route[^1]);
    }
}
