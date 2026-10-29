using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Formats;
using OpenArmada.Sandbox;
using static OpenArmada.Tests.CombatFixtures;

namespace OpenArmada.Tests;

// The world's outcome does not depend on the frame rate: it runs fixed ticks of 0.02 s and carries the rest of a frame
// to the next. One setup of every forced motion, stepped at 10 and at 60 frames per second, is compared at each 0.1 s.
// Ships have a model radius of 10 and an impulse speed of 10.
[TestClass]
public class FrameRateTests
{
    // A black hole at the origin (gravity radius 100, event radius 5); Drifter, engines destroyed, falls in from 30 and
    // is swallowed, which shatters it into debris; Driver, with thrust, is ordered across the hole and held at its ring;
    // Mover coasts into Holder at 10 per second, and the collision separation moves them apart.
    static (CombatWorld World, UnitPoses Poses, PlacedObject[] Units) Build()
    {
        var model = DebrisTests.Model(DebrisTests.MeshNode("hull", "", 4));
        var noEngines = new StartingState { DestroyedSystems = ["Engines"] };
        (string Label, Vector3 At, StartingState? Start, Sod? Model)[] ships =
        [
            ("Drifter", new Vector3(0, 0, 30), noEngines, model),
            ("Driver", new Vector3(-60, 0, 0), null, null),
            ("Mover", new Vector3(300, 0, 0), noEngines with { Velocity = new Vector3(0, 0, 10) }, null),
            ("Holder", new Vector3(300, 0, 25), null, null),
        ];
        var units = ships.Select(s => Placed(s.Label, 1, s.At.X, s.At.Z) with { Start = s.Start }).ToArray();
        var poses = new UnitPoses(units);
        var combat = ships.Select(
            (s, i) =>
                new CombatUnit
                {
                    Unit = units[i],
                    Profile = Profile(systems: [Sys("Engines")]),
                    DefinitionName = "s.odf",
                    Radius = 0,
                    ModelRadius = 10,
                    Mobile = true,
                    Motion = new ShipMotion(
                        new PhysicsValues
                        {
                            ImpulseSpeed = 10,
                            YawRotationRate = 1,
                            MaxRoll = null,
                        },
                        s.At,
                        0
                    ),
                    Model = s.Model,
                }
        );
        var world = new CombatWorld(poses, combat, 1, new Random(7).NextDouble, (_, _, pose) => pose.Position)
        {
            BlackHoles =
            [
                new BlackHole
                {
                    Object = Placed("hole", 0, 0),
                    Center = Vector3.Zero,
                    GravityRadius = 100,
                    EventRadius = 5,
                    GravityForce = 450,
                    Rotation = 0,
                },
            ],
        };
        world.Move(units[1], 60, 0);

        return (world, poses, units);
    }

    // What a state dump would show of the world: the time, each unit's pose, velocity, order, and end, and the debris.
    static string State(CombatWorld world, UnitPoses poses, PlacedObject[] units) =>
        $"t={world.Time:R} "
        + string.Join(
            " ",
            units.Select(u =>
                world.State(u) is { } s
                    ? $"{u.Label}:{poses.Pose(u).Position} {poses.Pose(u).Heading:R} {s.Velocity} {s.PlayerMove} {s.Dying}"
                    : $"{u.Label}:gone"
            )
        )
        + $" debris={world.Debris.Count} "
        + string.Join(" ", world.Debris.Select(p => $"{p.Position} {p.AxisX} {p.Life:R}"));

    [TestMethod]
    public void TheWorldAtTenAndAtSixtyFramesPerSecondIsTheSameAtEveryTenthOfASecond()
    {
        var (slow, slowPoses, slowUnits) = Build();
        var (fast, fastPoses, fastUnits) = Build();
        var differ = new List<string>();
        for (var tenth = 1; tenth <= 100; tenth++)
        {
            slow.Step(0.1);
            for (var frame = 0; frame < 6; frame++)
            {
                fast.Step(1.0 / 60);
            }

            var (a, b) = (State(slow, slowPoses, slowUnits), State(fast, fastPoses, fastUnits));
            if (a != b)
            {
                differ.Add($"{tenth / 10.0} s:\n 10 fps {a}\n 60 fps {b}");
            }
        }

        Assert.AreEqual(0, differ.Count, string.Join("\n", differ.Take(2)));
    }

    [TestMethod]
    public void TheSetupHasACollisionABlackHoleAndDebris()
    {
        var (world, poses, units) = Build();
        var holderStart = units[3].Position;
        var debris = 0;
        for (var i = 0; i < 100; i++)
        {
            world.Step(0.1);
            debris = Math.Max(debris, world.Debris.Count);
        }

        Assert.IsNull(world.State(units[0]), "Drifter is swallowed");
        Assert.IsTrue(debris > 0, "Drifter left debris");
        // the ring is 1.5 x 5, plus Driver's radius of 10; Driver slides round it and is not swallowed
        var driver = poses.Pose(units[1]).Position;
        Assert.IsTrue(new Vector2(driver.X, driver.Z).Length() >= 17.5f - 1e-3, $"Driver is inside the ring: {driver}");
        Assert.IsFalse(world.State(units[1])!.Dying);
        // Mover would coast through Holder; the separation keeps it short of Holder, which takes no share while still
        var (mover, holder) = (poses.Pose(units[2]).Position, poses.Pose(units[3]).Position);
        Assert.AreEqual(holderStart, holder);
        Assert.IsTrue(mover.Z < holder.Z && Vector3.Distance(mover, holder) >= 20 - 1e-3, $"Mover is at {mover}");
    }
}
