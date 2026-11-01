using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Formats;
using OpenArmada.Sandbox;

namespace OpenArmada.Tests;

// What the map's objects write into the planner, and the tunables it reads (INV-0001: everything built in code).
[TestClass]
public class PathObstaclesTests
{
    static readonly PathPlanConfig Shipped = new()
    {
        QuadtreeResolution = 50,
        DefaultTerrainValue = 100,
        NebulaeSafety = 1.8f,
        AsteroidBeltCost = 201,
        AsteroidBeltDistance = 150,
        BlackholeCost = -1,
        BlackholeSafety = 1.5f,
        FormationSpacing = 100,
    };

    static AsteroidSite Site(float x, float z, Vector3[]? belt = null) =>
        new()
        {
            Label = "f",
            Class = "field",
            Center = new Vector3(x, 0, -z),
            Belt = belt,
        };

    static IReadOnlyList<PathWrite> Writes(
        IEnumerable<AsteroidSite>? sites = null,
        IEnumerable<Nebula>? nebulae = null,
        IEnumerable<BlackHole>? holes = null,
        IEnumerable<PathStation>? stations = null
    ) => PathObstacles.Writes(Shipped, sites ?? [], nebulae ?? [], holes ?? [], stations ?? []);

    [TestMethod]
    public void AnAsteroidFieldWritesAnOuterRingAtThreeTimesTheDefaultAndAnImpassableCore()
    {
        var writes = Writes([Site(1000, 700)]);

        // the core's half size is half the belt distance (75); the ring adds FORMATION_SPACING (100)
        CollectionAssert.AreEqual(
            new[] { new PathWrite(1000, 700, 1000, 700, 175, 300), new PathWrite(1000, 700, 1000, 700, 75, -1) },
            writes.ToArray()
        );
    }

    [TestMethod]
    public void ANebulaWritesItsPathingCostPaddedBySafetyTimesItsEffectRadius()
    {
        var nebula = new Nebula
        {
            Label = "n",
            Class = "mnebula3",
            Kind = 3,
            Center = new Vector3(400, 0, -900),
            EffectRadius = 100,
            PathingCost = 400.9f,
        };

        // the value is the cost truncated (ftol); the pad is 1.8 x 100
        CollectionAssert.AreEqual(
            new[] { new PathWrite(400, 900, 400, 900, 180, 400) },
            Writes(null, [nebula]).ToArray()
        );
    }

    [TestMethod]
    public void ABlackHolesPlannerRadiusIsTheSafetyCoefficientTimesItsEventRadiusNotAnEffectRadius()
    {
        var hole = new BlackHole
        {
            Object = Placed(),
            Center = new Vector3(1200, 0, -300),
            EventRadius = 30,
            GravityRadius = 500,
        };

        // 1.5 x 30 = 45, at BLACKHOLE_PATHING_COST (IMPASSABLE_TERRAIN, -1)
        CollectionAssert.AreEqual(
            new[] { new PathWrite(1200, 300, 1200, 300, 45, -1) },
            Writes(null, null, [hole]).ToArray()
        );
    }

    static PlacedObject Placed() =>
        new()
        {
            Label = "h",
            Class = "blackhole",
            Model = "m",
            Team = 0,
            Slot = 0,
            Position = Vector3.Zero,
            AxisX = Vector3.UnitX,
            AxisY = Vector3.UnitY,
            AxisZ = Vector3.UnitZ,
        };

    [TestMethod]
    public void AStarbasesFootprintIsItsModelBoxAtItsPositionWithNoPadAndTheValue198()
    {
        var station = new PathStation(new Vector3(1000, 0, -500), new Vector3(-50, 0, -20), new Vector3(60, 10, 30));

        CollectionAssert.AreEqual(
            new[] { new PathWrite(950, 480, 1060, 530, 0, 198) },
            Writes(null, null, null, [station]).ToArray()
        );
    }

    [TestMethod]
    public void ABeltWritesASquareAtItsFirstPointAndAtEveryPointMoreThanTheHalfDistanceFromTheLastOne()
    {
        // a straight belt of 1000 units along X at z 500 (the sandbox's Z is mirrored)
        var belt = new[] { new Vector3(200, 10, -500), new Vector3(1200, 10, -500) };

        var writes = Writes([Site(0, 0, belt)]);

        Assert.AreEqual(new PathWrite(200, 500, 200, 500, 75, 201), writes[0]);
        Assert.IsTrue(writes.All(w => w.Z0 == 500 && w.Pad == 75 && w.Value == 201));
        for (var i = 1; i < writes.Count; i++)
        {
            var gap = writes[i].X0 - writes[i - 1].X0;
            Assert.IsTrue(gap > 75 && gap <= 85, $"write {i} lies {gap} from the one before");
        }

        Assert.IsTrue(writes[^1].X0 >= 1100, "the walk reaches the belt's far end");
    }

    [TestMethod]
    public void ABeltPathPassesThroughItsPointsAtWholeParameters()
    {
        var points = new[] { new Vector3(0, 0, 0), new Vector3(100, 0, 50), new Vector3(300, 20, 50) };

        Assert.AreEqual(points[0], PathObstacles.PointAt(points, 0f));
        Assert.AreEqual(points[1].X, PathObstacles.PointAt(points, 1f).X, 1e-3);
        Assert.AreEqual(points[1].Z, PathObstacles.PointAt(points, 1f).Z, 1e-3);
        Assert.AreEqual(points[2], PathObstacles.PointAt(points, 2.5f), "past the last segment it is the last point");
    }

    [TestMethod]
    public void WhereNothingCrowdsTheObstaclesAreWrittenAsTheyAreListed()
    {
        var all = Writes(
            [Site(10, 10)],
            [
                new Nebula
                {
                    Label = "n",
                    Class = "mnebula1",
                    Kind = 1,
                    Center = Vector3.Zero,
                    EffectRadius = 1,
                },
            ]
        );

        Assert.AreEqual(3, all.Count);
        Assert.AreEqual(-1, all[1].Value, "a field's core is written after its ring");
    }

    // ---- the tunables ----

    static GameConstants Constants(params string[] lines) =>
        GameConstants.Read("RTS_CFG.h", Encoding.ASCII.GetBytes(string.Join("\r\n", lines) + "\r\n"));

    static readonly string[] InstallKeys =
    [
        "float HIGHEST_QUADTREE_GRID_RESOLUTION = 50.0;",
        "int USE_PATH_PLANNING = 1;",
        "int IMPASSABLE_TERRAIN = -1;",
        "int DEFAULT_TERRAIN_VALUE = 100;",
        "int PERFORM_RUBBER_BANDING = 1;",
        "float NEBULAE_PATHING_SAFETY_COEFFICIENT = 1.8;",
        "float ASTEROID_BELT_PATHING_COST = 201.0;",
        "float ASTEROID_BELT_PATHING_DISTANCE = 150.0;",
        "float BLACKHOLE_PATHING_COST = IMPASSABLE_TERRAIN;",
        "float BLACKHOLE_PATHING_SAFETY_COEFFICIENT = 1.5;",
        "float MAP_EDGE_EPSILON = 0.1;",
        "float MAP_EDGE_BUFFER = 100.0;",
    ];

    [TestMethod]
    public void TheInstallsKeysAreReadIncludingTheIdentifierValuedBlackHoleCost()
    {
        var gaps = new Gaps();

        var config = PathPlanConfig.Read(Constants(InstallKeys), 100, gaps);

        Assert.AreEqual(50f, config.QuadtreeResolution);
        Assert.AreEqual(100, config.DefaultTerrainValue);
        Assert.AreEqual(-1, config.ImpassableTerrain);
        Assert.AreEqual(1.8f, config.NebulaeSafety);
        Assert.AreEqual(201f, config.AsteroidBeltCost);
        Assert.AreEqual(150f, config.AsteroidBeltDistance);
        Assert.AreEqual(-1f, config.BlackholeCost, "IMPASSABLE_TERRAIN, declared before it");
        Assert.AreEqual(1.5f, config.BlackholeSafety);
        Assert.AreEqual(100f, config.MapEdgeBuffer);
        Assert.AreEqual(100f, config.FormationSpacing);
        Assert.AreEqual(0, gaps.Entries.Count);
    }

    [TestMethod]
    public void AMissingKeyTakesTheGamesDefaultAndIsReportedOnceForAllMissingKeys()
    {
        var gaps = new Gaps();
        var keys = InstallKeys.Where(l => !l.Contains("ASTEROID_BELT") && !l.Contains("MAP_EDGE_BUFFER")).ToArray();

        var config = PathPlanConfig.Read(Constants(keys), 100, gaps);

        Assert.AreEqual(1000f, config.AsteroidBeltCost);
        Assert.AreEqual(200f, config.AsteroidBeltDistance);
        Assert.AreEqual(100f, config.MapEdgeBuffer);
        Assert.AreEqual(1, gaps.Entries.Count);
        CollectionAssert.AreEquivalent(
            new[] { "ASTEROID_BELT_PATHING_COST", "ASTEROID_BELT_PATHING_DISTANCE", "MAP_EDGE_BUFFER" },
            gaps.Entries[0].Names.ToArray()
        );
    }

    [TestMethod]
    public void AKeyDeclaredAsTheOtherTypeIsNotReadAsTheGameReadsItBySearchingForTheTypeItWants()
    {
        // 0x4fc0d0 takes an int only through an int declaration, 0x4fc1d0 a float only through a float one
        var gaps = new Gaps();
        var keys = InstallKeys
            .Select(l =>
                l.StartsWith("int DEFAULT_TERRAIN_VALUE") ? "float DEFAULT_TERRAIN_VALUE = 33.0;"
                : l.StartsWith("float NEBULAE") ? "int NEBULAE_PATHING_SAFETY_COEFFICIENT = 3;"
                : l
            )
            .ToArray();

        var config = PathPlanConfig.Read(Constants(keys), 100, gaps);

        Assert.AreEqual(10, config.DefaultTerrainValue, "the exe's default");
        Assert.AreEqual(1.5f, config.NebulaeSafety, "the exe's default");
        Assert.AreEqual(1, gaps.Entries.Count);
    }

    [TestMethod]
    public void AResolutionThatIsNotAboveZeroTakesTheDefaultAndIsReported()
    {
        var gaps = new Gaps();
        var keys = InstallKeys
            .Select(l => l.StartsWith("float HIGHEST") ? "float HIGHEST_QUADTREE_GRID_RESOLUTION = 0.0;" : l)
            .ToArray();

        var config = PathPlanConfig.Read(Constants(keys), 100, gaps);

        Assert.AreEqual(100f, config.QuadtreeResolution);
        Assert.AreEqual(1, gaps.Entries.Count);
    }

    [TestMethod]
    public void WithoutAHeaderEveryKeyIsTheGamesDefault()
    {
        var config = PathPlanConfig.Read(null, 40, new Gaps());

        Assert.AreEqual(PathPlanConfig.Game with { FormationSpacing = 40 }, config);
        Assert.AreEqual(100f, config.QuadtreeResolution);
        Assert.AreEqual(10, config.DefaultTerrainValue);
        Assert.AreEqual(1000f, config.BlackholeCost);
    }
}
