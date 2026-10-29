using System.Linq;
using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Sandbox;
using static OpenArmada.Tests.CombatFixtures;

namespace OpenArmada.Tests;

// A move order's arrival and the hold of an idle ship, with ships pushed through the world's push route. The ship
// drives at 10 units/s and faces +Z, its tether point is where it starts (the origin).
[TestClass]
public class MoveArrivalTests
{
    sealed record Rig(CombatWorld World, UnitPoses Poses, PlacedObject Unit, UnitCombatState State)
    {
        public Vector3 Position => Poses.Pose(Unit).Position;
    }

    // A ship with a motion of the given tooCloseToTurn, an optional weapon (range 100), an engines system, and other
    // units of the world.
    static Rig Build(
        double tooClose = 25,
        bool weapon = false,
        bool engines = false,
        params (PlacedObject Unit, float Radius)[] others
    )
    {
        var unit = Placed("s", 1, 0);
        var motion = new ShipMotion(
            new PhysicsValues
            {
                ImpulseSpeed = 10,
                YawRotationRate = 1,
                MaxRoll = null,
                TooCloseToTurn = tooClose,
            },
            unit.Position,
            0
        );
        var poses = new UnitPoses(new[] { unit }.Concat(others.Select(o => o.Unit)));
        var units = new[]
        {
            new CombatUnit
            {
                Unit = unit,
                Profile = Profile(
                    weapon ? new[] { Weapon(range: 100) } : null,
                    systems: engines ? new[] { Sys("Engines") } : null
                ),
                DefinitionName = "s.odf",
                Radius = 0,
                Mobile = true,
                Motion = motion,
            },
        }.Concat(
            others.Select(o => new CombatUnit
            {
                Unit = o.Unit,
                Profile = Profile(),
                DefinitionName = "o.odf",
                Radius = 0,
                Mobile = false,
            })
        );
        var world = new CombatWorld(poses, units, 1, () => 0, (_, _, pose) => pose.Position);

        return new Rig(world, poses, unit, world.State(unit)!);
    }

    static void Run(Rig rig, double seconds) => rig.World.Step(seconds);

    [TestMethod]
    public void AnUnpushedShipDrivesToItsPointAndEndsTheOrderThere()
    {
        var rig = Build();
        rig.World.Move(rig.Unit, 0, 100);
        Run(rig, 1);
        Assert.AreEqual((0f, 100f), rig.State.PlayerMove);
        Run(rig, 10.5);
        Assert.IsNull(rig.State.PlayerMove);
        Assert.AreEqual(100, rig.Position.Z, 1e-3);
    }

    [TestMethod]
    public void APushThatLeavesTheShipUnderTooCloseToTurnFromItsPointEndsTheOrderAndTheShipStopsThere()
    {
        var rig = Build();
        rig.World.Move(rig.Unit, 0, 100);
        Run(rig, 0.02);
        rig.World.Push(rig.Unit, new Vector3(0, 7, 80)); // 21 from the point, 3D
        var stoppedAt = rig.Position;
        Assert.IsNull(rig.State.PlayerMove);
        Assert.AreEqual(stoppedAt, rig.State.Tether); // its height included
        Assert.AreEqual(7f, rig.State.Tether.Y);
        Assert.AreEqual(Vector3.Zero, rig.State.Velocity);
        Run(rig, 5);
        Assert.AreEqual(stoppedAt, rig.Position);
        Assert.IsNull(rig.State.PlayerMove);
    }

    [TestMethod]
    public void APushThatLeavesTheShipExactlyAtTooCloseToTurnOrBeyondLeavesTheOrderOpenAndTheShipDrivesOn()
    {
        foreach (var z in new[] { 75f, 60f })
        {
            var rig = Build();
            rig.World.Move(rig.Unit, 0, 100);
            rig.World.Push(rig.Unit, new Vector3(0, 0, z));
            Assert.IsNotNull(rig.State.PlayerMove, $"push to {z}");
            Run(rig, 1);
            Assert.AreEqual(z + 10, rig.Position.Z, 0.5);
            Assert.IsNotNull(rig.State.PlayerMove);
        }
    }

    [TestMethod]
    public void EachShipsTooCloseToTurnDecidesWhetherAPushEndsItsOrder()
    {
        foreach (var (tooClose, ends) in new[] { (40.0, false), (50.0, true) })
        {
            var rig = Build(tooClose);
            rig.World.Move(rig.Unit, 0, 100);
            rig.World.Push(rig.Unit, new Vector3(0, 0, 55)); // 45 from the point
            Assert.AreEqual(ends, rig.State.PlayerMove == null, $"tooCloseToTurn {tooClose}");
        }
    }

    [TestMethod]
    public void APushRefreshesThePositionAndVelocityAtOnce()
    {
        var rig = Build();
        rig.World.Move(rig.Unit, 0, 1000);
        Run(rig, 1);
        rig.World.Push(rig.Unit, new Vector3(0, 0, 100));
        Assert.AreEqual(rig.Position, rig.State.Position);
        Assert.AreEqual(rig.Position.Z, rig.State.Position!.Value.Z);
        Assert.IsTrue(rig.State.Velocity.Z > 9);
    }

    [TestMethod]
    public void AnIdleShipPushedFartherThanTooCloseToTurnFromItsTetherDrivesBackAndEndsTheReturnThere()
    {
        foreach (
            var (name, weapon, alert) in new[]
            {
                ("weaponless", false, AlertStance.Red),
                ("red", true, AlertStance.Red),
                ("yellow", true, AlertStance.Yellow),
                ("green", true, AlertStance.Green),
            }
        )
        {
            var rig = Build(weapon: weapon);
            rig.State.Alert = alert;
            rig.World.Push(rig.Unit, new Vector3(0, 0, 60));
            Run(rig, 20);
            Assert.AreEqual(0, rig.Position.Z, 1e-3, name);
            Assert.IsFalse(rig.State.Returning, name);
            Run(rig, 5);
            Assert.AreEqual(0, rig.Position.Z, 1e-3, name);
        }
    }

    [TestMethod]
    public void AnIdleShipPushedNearerThanTooCloseToTurnFromItsTetherStaysWhereItWasMoved()
    {
        foreach (
            var (name, weapon, alert) in new[]
            {
                ("weaponless", false, AlertStance.Red),
                ("red", true, AlertStance.Red),
                ("yellow", true, AlertStance.Yellow),
                ("green", true, AlertStance.Green),
            }
        )
        {
            var rig = Build(weapon: weapon);
            rig.State.Alert = alert;
            rig.World.Push(rig.Unit, new Vector3(0, 0, 20));
            Run(rig, 10);
            Assert.AreEqual(20, rig.Position.Z, 1e-3, name);
        }
    }

    [TestMethod]
    public void AReturnThatAPushInsideTheRadiusEndsLeavesTheShipThereAsItsNewTetherPoint()
    {
        var rig = Build();
        rig.World.Push(rig.Unit, new Vector3(0, 0, 60));
        Run(rig, 5); // turns around, then drives back
        Assert.IsTrue(rig.State.Returning);
        var z = rig.Position.Z;
        rig.World.Push(rig.Unit, new Vector3(0, 3, 12 - z)); // 12 from the tether point, 3 above it
        Assert.IsFalse(rig.State.Returning);
        Assert.AreEqual(rig.Position, rig.State.Tether);
        Run(rig, 10);
        Assert.AreEqual(12, rig.Position.Z, 1e-3);
    }

    [TestMethod]
    public void AChangeOfStanceDoesNotCancelTheReturn()
    {
        var rig = Build(weapon: true);
        rig.World.Push(rig.Unit, new Vector3(0, 0, 60));
        Run(rig, 1);
        rig.State.Alert = AlertStance.Yellow;
        Run(rig, 1);
        rig.State.Alert = AlertStance.Green;
        Run(rig, 20);
        Assert.AreEqual(0, rig.Position.Z, 1e-3);
    }

    [TestMethod]
    public void AnIdleRedShipWithAPickWithin300UnitsOfItsTetherStillClosesOnIt()
    {
        var hostile = Placed("h", 2, 100);
        var rig = Build(weapon: true, others: (hostile, 0f));
        rig.World.Push(rig.Unit, new Vector3(30, 0, 0)); // beyond tooCloseToTurn from the tether point
        Run(rig, 0.02);
        Assert.AreEqual(
            new MoveIntent
            {
                Kind = MoveKind.MoveTo,
                X = 100,
                Z = 0,
            },
            rig.State.Intent
        );
        Assert.IsFalse(rig.State.Returning);
    }

    [TestMethod]
    public void APushOnACoastingShipAndOnAShipWithAnAttackOrderChangesNoOrder()
    {
        var hostile = Placed("h", 2, 500);
        var attacker = Build(weapon: true, others: (hostile, 0f));
        attacker.World.Attack(attacker.Unit, hostile);
        attacker.World.Push(attacker.Unit, new Vector3(0, 0, 5));
        Assert.AreEqual("h", attacker.State.Order!.Unit.Label);
        Assert.IsFalse(attacker.State.Returning);

        var coaster = Build(engines: true);
        coaster.State.DestroySystem("Engines");
        Run(coaster, 0.02);
        coaster.World.Push(coaster.Unit, new Vector3(0, 0, 5));
        Assert.IsNull(coaster.State.PlayerMove);
        Assert.IsFalse(coaster.State.Returning);
        Assert.AreEqual(Vector3.Zero, coaster.State.Tether);
    }

    [TestMethod]
    public void APushOnADerelictShipChangesNoOrder()
    {
        var rig = Build();
        rig.World.Move(rig.Unit, 0, 100);
        rig.State.Derelict = true;
        rig.World.Push(rig.Unit, new Vector3(0, 0, 80));
        Assert.AreEqual((0f, 100f), rig.State.PlayerMove);
        Assert.IsFalse(rig.State.Returning);
        Assert.AreEqual(new Vector3(0, 0, 100), rig.State.Tether); // the move order's point, as the order set it
    }

    [TestMethod]
    [DataRow(float.NaN)]
    [DataRow(float.PositiveInfinity)]
    public void APushWithANonFiniteDisplacementIsReportedAndLeavesTheShipWhereItStands(float bad)
    {
        var rig = Build();
        rig.World.Move(rig.Unit, 0, 100);
        var lines = new System.Collections.Generic.List<string>();
        using (OpenArmada.Diagnostics.Log.Redirect(lines.Add))
        {
            rig.World.Push(rig.Unit, new Vector3(0, 0, bad));
        }

        Assert.AreEqual(Vector3.Zero, rig.Position);
        Assert.AreEqual((0f, 100f), rig.State.PlayerMove);
        Assert.AreEqual(1, lines.Count);
        StringAssert.Contains(lines[0], "ERROR");
    }

    [TestMethod]
    public void APushWithTheSameNonFiniteDisplacementAgainIsReportedOnce()
    {
        var rig = Build();
        var lines = new System.Collections.Generic.List<string>();
        using (OpenArmada.Diagnostics.Log.Redirect(lines.Add))
        {
            for (var i = 0; i < 3; i++)
            {
                rig.World.Push(rig.Unit, new Vector3(0, 0, float.NaN));
            }
        }

        Assert.AreEqual(1, lines.Count);
    }

    [TestMethod]
    public void TheNonFinitePushesOfTwoUnitsThatShareALabelAreReportedOnceEach()
    {
        // an unlabeled layout object takes its class name as its label, so two units can share one
        var (first, second) = (Placed("fdestroy", 1, 0), Placed("fdestroy", 1, 100));
        CombatUnit Ship(PlacedObject unit) =>
            new()
            {
                Unit = unit,
                Profile = Profile(),
                DefinitionName = "s.odf",
                Radius = 0,
                Mobile = true,
                Motion = new ShipMotion(
                    new PhysicsValues
                    {
                        ImpulseSpeed = 10,
                        YawRotationRate = 1,
                        MaxRoll = null,
                    },
                    unit.Position,
                    0
                ),
            };
        var world = new CombatWorld(
            new UnitPoses(new[] { first, second }),
            new[] { Ship(first), Ship(second) },
            1,
            () => 0,
            (_, _, pose) => pose.Position
        );
        var lines = new System.Collections.Generic.List<string>();
        using (OpenArmada.Diagnostics.Log.Redirect(lines.Add))
        {
            world.Push(first, new Vector3(0, 0, float.NaN));
            world.Push(second, new Vector3(0, 0, float.NaN));
        }

        Assert.AreEqual(2, lines.Count);
    }

    [TestMethod]
    public void AShipThatCoastedAwayFromItsTetherPointDrivesBackOnceItsEnginesAreRepaired()
    {
        var rig = Build(engines: true);
        rig.World.Push(rig.Unit, new Vector3(0, 0, 10));
        rig.State.DestroySystem("Engines");
        Run(rig, 0.02);
        rig.World.Push(rig.Unit, new Vector3(0, 0, 50));
        Run(rig, 2);
        Assert.IsTrue(rig.Position.Z > 50);
        rig.State.Systems["Engines"].HitPoints = rig.State.Systems["Engines"].Max;
        rig.State.RestoreSystem("Engines");
        Run(rig, 30);
        Assert.AreEqual(0, rig.Position.Z, 1e-3);
    }

    [TestMethod]
    public void APushOnAUnitWithoutMotionMovesNothing()
    {
        var unit = Placed("r", 1, 0);
        var poses = new UnitPoses([unit]);
        var world = new CombatWorld(
            poses,
            [
                new CombatUnit
                {
                    Unit = unit,
                    Profile = Profile(),
                    DefinitionName = "r.odf",
                    Radius = 0,
                    Mobile = false,
                },
            ],
            1,
            () => 0,
            (_, _, pose) => pose.Position
        );
        world.Push(unit, new Vector3(5, 0, 5));
        Assert.AreEqual(Vector3.Zero, poses.Pose(unit).Position);
    }
}
