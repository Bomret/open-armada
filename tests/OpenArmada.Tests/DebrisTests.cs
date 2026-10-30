using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Formats;
using OpenArmada.Sandbox;
using static OpenArmada.Tests.CombatFixtures;

namespace OpenArmada.Tests;

// The debris pieces of a unit that ends: the game's rule (0x58cef0, 0x57cee0, 0x57cf90, 0x57d270, 0x57cd90) over models
// built in code and a scripted random source.
[TestClass]
public class DebrisTests
{
    static Sod.Matrix34 Identity =>
        new()
        {
            Right = Vector3.UnitX,
            Up = Vector3.UnitY,
            Front = Vector3.UnitZ,
        };

    // A mesh node whose faces are the triangles (i, 0, 0), (i, 3, 0), (i, 0, 3) for i in 0..faces.
    internal static Sod.Node MeshNode(string name, string parent, int faces, Sod.Matrix34? transform = null)
    {
        var vertices = new List<Vector3>();
        var corners = new List<(ushort, ushort)>();
        for (var i = 0; i < faces; i++)
        {
            var at = (ushort)vertices.Count;
            vertices.AddRange([new Vector3(i, 0, 0), new Vector3(i, 3, 0), new Vector3(i, 0, 3)]);
            corners.AddRange([(at, at), ((ushort)(at + 1), (ushort)(at + 1)), ((ushort)(at + 2), (ushort)(at + 2))]);
        }

        return new Sod.Node
        {
            Type = Sod.NodeType.Mesh,
            Name = name,
            Parent = parent,
            Transform = transform ?? Identity,
            Mesh = new Sod.Mesh
            {
                Vertices = vertices.ToArray(),
                TexCoords = vertices.Select(v => new Vector2(v.X, v.Y)).ToArray(),
                Groups = [new Sod.Group { Corners = corners.ToArray() }],
            },
        };
    }

    static Sod.Node Plain(string name, string parent, Sod.Matrix34? transform = null) =>
        new()
        {
            Name = name,
            Parent = parent,
            Transform = transform ?? Identity,
        };

    internal static Sod Model(params Sod.Node[] nodes) => new() { Version = 1.8f, Nodes = nodes };

    static Func<double> Script(params double[] values)
    {
        var queue = new Queue<double>(values);

        return () => queue.Count > 0 ? queue.Dequeue() : 0;
    }

    sealed record Scene(CombatWorld World, PlacedObject Unit, UnitPoses Poses);

    // A unit with the model (null: none) in a world of the limits; Motion makes the unit a mover.
    static Scene Build(
        Sod? model,
        DebrisLimits? limits = null,
        Func<double>? random = null,
        FlightModel? motion = null,
        Vector3? at = null,
        CombatProfile? profile = null
    )
    {
        var unit = Placed("u", 1, at?.X ?? 0, at?.Z ?? 0);
        var poses = new UnitPoses([unit]);
        var world = new CombatWorld(
            poses,
            [
                new CombatUnit
                {
                    Unit = unit,
                    Profile = profile ?? Profile(),
                    DefinitionName = "u.odf",
                    Radius = 0,
                    Mobile = motion != null,
                    Motion = motion,
                    Model = model,
                },
            ],
            1,
            random ?? (() => 0),
            (_, _, pose) => pose.Position,
            debris: limits
        );

        return new Scene(world, unit, poses);
    }

    // The unit's critical hit has run its time out: it leaves in the next tick.
    static IReadOnlyList<CombatEvent> End(Scene scene)
    {
        var state = scene.World.State(scene.Unit)!;
        state.Dying = true;
        state.DyingRemaining = 0;

        return scene.World.Step(CombatWorld.Tick);
    }

    static DebrisLimits Limits(float density = 1, int max = 100, float life = 1, float spin = 4, float speed = 10) =>
        new()
        {
            Density = density,
            MaxChunks = max,
            MaxLifetime = life,
            MaxAngularVelocity = spin,
            MaxVelocity = speed,
        };

    [TestMethod]
    public void AnAccumulatorThatReachesOneButDoesNotExceedItSpawnsNoPiece()
    {
        // density 1.0 over 4 faces: the accumulator reads 1, 2, 2, 2 before each spawn; 1.0 is not above 1.0 (strict)
        var scene = Build(Model(MeshNode("hull", "", 4)), Limits(density: 1));
        End(scene);
        Assert.AreEqual(3, scene.World.Debris.Count);
    }

    [TestMethod]
    public void AFaceSpawnsAPieceForEachWholeStepOfItsAccumulator()
    {
        // 2.5 per face over 2 faces: 2.5 gives two pieces and leaves 0.5, then 3.0 gives two and leaves 1.0
        var scene = Build(Model(MeshNode("hull", "", 2)), Limits(density: 2.5f));
        End(scene);
        Assert.AreEqual(4, scene.World.Debris.Count);
    }

    [TestMethod]
    public void EachMeshKeepsItsOwnAccumulator()
    {
        // two meshes of two faces each at density 1: each yields one piece; one shared accumulator would yield three
        var scene = Build(Model(MeshNode("a", "", 2), MeshNode("b", "a", 2)), Limits(density: 1));
        End(scene);
        Assert.AreEqual(2, scene.World.Debris.Count);
    }

    [TestMethod]
    public void AMeshUnderADamageNodeThatDoesNotShowLeavesNoPiece()
    {
        var model = Model(Plain("root", ""), Plain("damage", "root"), MeshNode("engines", "damage", 3));
        var scene = Build(model, Limits(density: 2));
        End(scene);
        Assert.AreEqual(0, scene.World.Debris.Count);
    }

    [TestMethod]
    public void AMeshUnderADamageNodeThatShowsAtTheEndLeavesPieces()
    {
        var model = Model(Plain("root", ""), Plain("damage", "root"), MeshNode("engines", "damage", 3));
        var scene = Build(model, Limits(density: 2), profile: Profile(systems: [Sys(SystemNames.Engines)]));
        scene.World.State(scene.Unit)!.Systems[SystemNames.Engines].Destroyed = true;
        End(scene);
        Assert.AreEqual(5, scene.World.Debris.Count); // 1, then 2, then 2
    }

    [TestMethod]
    public void AMeshOutsideTheDamageGroupAlwaysLeavesPieces()
    {
        var model = Model(Plain("root", ""), Plain("damage", "root"), MeshNode("hull", "root", 3));
        var scene = Build(model, Limits(density: 2));
        End(scene);
        Assert.AreEqual(5, scene.World.Debris.Count);
    }

    [TestMethod]
    public void TheLiveCountNeverExceedsTheCapAndTheOldestPiecesGoFirst()
    {
        var scene = Build(Model(MeshNode("hull", "", 20)), Limits(density: 1.5f, max: 5));
        End(scene);
        var faces = scene.World.Debris.Select(p => p.Face).ToList();
        Assert.AreEqual(5, faces.Count);
        Assert.IsTrue(faces.Min() >= 14, string.Join(",", faces));
    }

    [TestMethod]
    public void AShatterFarOverTheCapKeepsOnlyItsNewestPieces()
    {
        // density 100 over 2000 faces makes about 200000 pieces; the 3000 left are the 100 of each of the last 30 faces
        var scene = Build(Model(MeshNode("hull", "", 2000)), Limits(density: 100, max: 3000));
        End(scene);
        var faces = scene.World.Debris.Select(p => p.Face).ToList();
        CollectionAssert.AreEqual(
            Enumerable.Range(1970, 30).SelectMany(f => Enumerable.Repeat(f, 100)).ToList(),
            faces
        );
    }

    [TestMethod]
    public void ADensityACapOrALifetimeOfZeroGivesNoDebrisAndTheUnitStillEnds()
    {
        foreach (var limits in new[] { Limits(density: 0), Limits(max: 0), Limits(life: 0) })
        {
            var scene = Build(Model(MeshNode("hull", "", 4)), limits);
            var events = End(scene);
            Assert.AreEqual(0, scene.World.Debris.Count);
            Assert.AreEqual(1, events.OfType<UnitDestroyed>().Count());
        }
    }

    [TestMethod]
    public void AUnitWithoutAModelOrWithoutFacesEndsWithoutDebris()
    {
        foreach (var model in new Sod?[] { null, Model(Plain("root", "")), Model(MeshNode("hull", "", 0)) })
        {
            var scene = Build(model, Limits());
            var events = End(scene);
            Assert.AreEqual(0, scene.World.Debris.Count);
            Assert.AreEqual(1, events.OfType<UnitDestroyed>().Count());
        }
    }

    [TestMethod]
    public void APieceTakesItsDrawsInTheGamesOrder()
    {
        // life, then the direction's three signed draws (the vector is third, second, first), then the speed, then the
        // tumble's three (third, second, first)
        var random = Script(0.5, 0.75, 0.5, 1.0, 0.25, 0.75, 0.5, 1.0);
        var scene = Build(Model(MeshNode("hull", "", 2)), Limits(life: 4, speed: 20, spin: 2), random);
        End(scene);
        var piece = scene.World.Debris.Single();
        Assert.AreEqual(2.0, piece.Life, 1e-6);
        var direction = Vector3.Normalize(new Vector3(1, 0, 0.5f));
        AssertNear(direction * 5, piece.Velocity);
        AssertNear(new Vector3(2, 0, 1), piece.AngularVelocity);
    }

    [TestMethod]
    public void ADirectionShorterThanATenthIsDrawnAgain()
    {
        // 0.5 x3 is the zero vector; the next three give (1, 0, 0) (the third draw is x)
        var random = Script(1, 0.5, 0.5, 0.5, 0.5, 0.5, 1.0, 1.0, 0.5, 0.5, 0.5);
        var scene = Build(Model(MeshNode("hull", "", 2)), Limits(life: 1, speed: 10), random);
        End(scene);
        AssertNear(new Vector3(10, 0, 0), scene.World.Debris.Single().Velocity);
    }

    [TestMethod]
    public void APieceMovesAndTumblesEveryTickAndIsGoneWhenItsLifeRunsOut()
    {
        var random = Script(0.5, 0.75, 0.5, 1.0, 1.0, 0.5, 0.5, 1.0);
        var scene = Build(Model(MeshNode("hull", "", 2)), Limits(life: 1, speed: 10, spin: 2), random);
        End(scene);
        var piece = scene.World.Debris.Single();
        var start = piece.Position;
        var up = piece.AxisY;
        for (var i = 0; i < 24; i++)
        {
            scene.World.Step(CombatWorld.Tick);
        }

        Assert.AreEqual(1, scene.World.Debris.Count);
        AssertNear(start + piece.Velocity * (float)(24 * CombatWorld.Tick), piece.Position);
        // spinning about its own x axis at 2 rad/s for 0.48 s: the x axis stays, the y axis turns by 0.96 rad
        AssertNear(piece.AxisX, Vector3.UnitX);
        Assert.AreEqual(MathF.Cos(0.96f), Vector3.Dot(up, piece.AxisY), 1e-3);
        scene.World.Step(CombatWorld.Tick);
        scene.World.Step(CombatWorld.Tick);
        Assert.AreEqual(0, scene.World.Debris.Count);
    }

    [TestMethod]
    public void APieceWhoseLifeReachesExactlyZeroStaysUntilTheNextTick()
    {
        // the game removes a piece once its life is below 0 (0x57cd90): a life of one tick is 0 after it, not below
        var scene = Build(Model(MeshNode("hull", "", 2)), Limits(life: 1), Script(CombatWorld.Tick));
        End(scene);
        scene.World.Step(CombatWorld.Tick);
        Assert.AreEqual(1, scene.World.Debris.Count);
        scene.World.Step(CombatWorld.Tick);
        Assert.AreEqual(0, scene.World.Debris.Count);
    }

    [TestMethod]
    public void OnlyTheShownLevelOfALodControlNodeShatters()
    {
        // inferred: the inactive detail levels are hidden, as the view hides them; the shown one has the most faces
        var lod = new Sod.Node
        {
            Type = Sod.NodeType.LodControl,
            Name = "lod",
            Parent = "root",
            Transform = Identity,
        };
        var model = Model(
            Plain("root", ""),
            lod,
            MeshNode("low", "lod", 2),
            MeshNode("high", "lod", 4),
            Plain("mid", "lod"),
            MeshNode("midMesh", "mid", 3)
        );
        var scene = Build(model, Limits(density: 2));
        End(scene);
        Assert.IsTrue(scene.World.Debris.All(p => p.Node.Name == "high"));
        Assert.AreEqual(7, scene.World.Debris.Count); // 4 faces at density 2: 1, then 2, 2, 2
    }

    [TestMethod]
    public void ALodControlNodeWithoutChildrenShowsNoLevel()
    {
        var lod = new Sod.Node
        {
            Type = Sod.NodeType.LodControl,
            Name = "lod",
            Parent = "root",
            Transform = Identity,
        };
        Assert.IsNull(Model(Plain("root", ""), lod).ShownLodLevel(lod));
    }

    [TestMethod]
    public void AUnitWhoseModelHasAChildlessLodControlNodeStillEndsWithDebrisFromItsOtherMeshes()
    {
        var lod = new Sod.Node
        {
            Type = Sod.NodeType.LodControl,
            Name = "lod",
            Parent = "root",
            Transform = Identity,
        };
        var scene = Build(Model(Plain("root", ""), lod, MeshNode("hull", "root", 2)), Limits(density: 2));
        End(scene);
        Assert.IsTrue(scene.World.Debris.Count > 0);
        Assert.IsTrue(scene.World.Debris.All(p => p.Node.Name == "hull"));
    }

    [TestMethod]
    public void MeshesAreWalkedFromTheRootDownTheTreeNotInNodeOrder()
    {
        // node order: b, c, a; the tree: root, b, a, c (a mesh before its children, the children in model order)
        var model = Model(Plain("root", ""), MeshNode("b", "root", 1), MeshNode("c", "a", 1), MeshNode("a", "root", 1));
        var scene = Build(model, Limits(density: 2));
        End(scene);
        CollectionAssert.AreEqual(new[] { "b", "a", "c" }, scene.World.Debris.Select(p => p.Node.Name).ToArray());
    }

    [TestMethod]
    public void APieceTakesNoShareOfTheUnitsVelocity()
    {
        var motion = FlightFixtures.Cruiser(Vector3.Zero, 0, impulse: 100);
        // the move order's facing offset takes the first draw
        var random = Script(0.0, 0.5, 0.75, 0.5, 1.0, 1.0, 0.5, 0.5, 0.5);
        var scene = Build(Model(MeshNode("hull", "", 2)), Limits(life: 1, speed: 10), random, motion);
        scene.World.Move(scene.Unit, 0, 1000);
        scene.World.Step(1);
        Assert.IsTrue(scene.World.State(scene.Unit)!.Velocity.Length() > 50);
        End(scene);
        var expected = Vector3.Normalize(new Vector3(1, 0, 0.5f)) * 10;
        AssertNear(expected, scene.World.Debris.Single().Velocity);
    }

    [TestMethod]
    public void APiecesTriangleIsTheFacesRelativeToItsCentroidPlacedByTheUnitAndTheMeshNodesOwnMatrix()
    {
        // the mesh node scales by 2 and sits at (10, 0, 4); the face is (0,0,0), (0,3,0), (0,0,3) around centroid
        // (0, 1, 1); Z mirrors into the world frame
        var node = new Sod.Matrix34
        {
            Right = new Vector3(2, 0, 0),
            Up = new Vector3(0, 2, 0),
            Front = new Vector3(0, 0, 2),
            Position = new Vector3(10, 0, 4),
        };
        var scene = Build(Model(MeshNode("hull", "", 2, node)), Limits(density: 2), at: new Vector3(100, 0, 50));
        End(scene);
        var piece = scene.World.Debris.First();
        AssertNear(new Vector3(100 + 10, 2, 50 - 4 - 2), piece.Position);
        AssertNear(new Vector3(2, 0, 0), piece.AxisX);
        Assert.AreEqual(3, piece.Vertices.Length);
        // the drawn winding is reversed against the file's, as SodBuilder draws it
        AssertNear(new Vector3(0, -1, 1), piece.Vertices[0]);
        AssertNear(new Vector3(0, -1, -2), piece.Vertices[1]);
        AssertNear(new Vector3(0, 2, 1), piece.Vertices[2]);
    }

    [TestMethod]
    public void AParentNodesTransformDoesNotMovePiecesOfItsChildsMesh()
    {
        var turned = new Sod.Matrix34
        {
            Right = Vector3.UnitZ,
            Up = Vector3.UnitY,
            Front = -Vector3.UnitX,
            Position = new Vector3(1000, 500, 250),
        };
        var plain = Build(Model(Plain("p", ""), MeshNode("m", "p", 2)), Limits(density: 1));
        var moved = Build(Model(Plain("p", "", turned), MeshNode("m", "p", 2)), Limits(density: 1));
        End(plain);
        End(moved);
        AssertNear(plain.World.Debris.Single().Position, moved.World.Debris.Single().Position);
        AssertNear(plain.World.Debris.Single().AxisX, moved.World.Debris.Single().AxisX);
    }

    [TestMethod]
    public void TwoWorldsOfOneSeedMakeTheSamePieces()
    {
        List<DebrisPiece> Run()
        {
            var seeded = new Random(7);
            var scene = Build(Model(MeshNode("hull", "", 10)), Limits(density: 0.8f), seeded.NextDouble);
            End(scene);
            for (var i = 0; i < 10; i++)
            {
                scene.World.Step(CombatWorld.Tick);
            }

            return scene.World.Debris.ToList();
        }

        var a = Run();
        var b = Run();
        Assert.IsTrue(a.Count > 0);
        CollectionAssert.AreEqual(a.Select(p => p.Position).ToList(), b.Select(p => p.Position).ToList());
    }

    static void AssertNear(Vector3 expected, Vector3 actual) =>
        Assert.IsTrue((expected - actual).Length() < 1e-3, $"expected {expected}, was {actual}");
}
