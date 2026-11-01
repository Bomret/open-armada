using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Sandbox;
using static OpenArmada.Tests.CombatFixtures;

namespace OpenArmada.Tests;

// The dogfight (0x42c9d0) and the attack state's band (0x428e90) through CombatWorld.Attack and Step. The attacker
// "a" is a ship of team 1 with a flight model (impulse speed 10, zero model radius), the target "t" a station of
// team 2 that stays where it was placed.
[TestClass]
public class DogfightTests
{
    sealed class Rig
    {
        public required CombatWorld World { get; init; }
        public required UnitPoses Poses { get; init; }
        public required PlacedObject A { get; init; }
        public required PlacedObject T { get; init; }

        public ShipPose Pose => Poses.Pose(A);

        public float Distance => Vector3.Distance(Pose.Position, Poses.Pose(T).Position);

        public FlightModel Flight => World.State(A)!.Motion!;

        public void Tick(int count = 1)
        {
            for (var i = 0; i < count; i++)
            {
                World.Step(CombatWorld.Tick);
            }
        }
    }

    // `at` and `target` are positions; `radius` is the attacker's combat radius.
    static Rig Build(
        Vector3 at,
        Vector3 target,
        IReadOnlyList<WeaponProfile>? weapons = null,
        float radius = 0,
        float targetRadius = 0,
        bool borg = false,
        float heading = 0
    )
    {
        var a = Placed("a", 1, at.X, at.Z) with { Position = at };
        var t = Placed("t", 2, target.X, target.Z) with { Position = target };
        var poses = new UnitPoses([a, t]);
        var values = FlightFixtures.Values(
            FlightFixtures.Fast.With(("impulseSpeed", 10)),
            FlightFixtures.Fast.With(("impulseSpeed", 10)),
            25
        ) with
        {
            BorgPhysics = borg,
        };
        var units = new[]
        {
            new CombatUnit
            {
                Unit = a,
                Profile = Profile(weapons ?? [Weapon(range: 100)]),
                DefinitionName = "a.odf",
                Radius = radius,
                Mobile = true,
                Motion = FlightFixtures.Ship(at, heading, values),
            },
            new CombatUnit
            {
                Unit = t,
                Profile = Profile(),
                DefinitionName = "t.odf",
                Radius = targetRadius,
                Mobile = false,
            },
        };
        var world = new CombatWorld(poses, units, 1, () => 0, (_, _, pose) => pose.Position);
        world.Attack(a, t);

        return new Rig
        {
            World = world,
            Poses = poses,
            A = a,
            T = t,
        };
    }

    static Vector3 V(float x, float z, float y = 38.5f) => new(x, y, z);

    [TestMethod]
    public void AShipWithATargetLocationWeaponTurnsInPlaceWithoutTravelling()
    {
        var rig = Build(V(0, 0), V(300, 0), [Weapon(range: 1000, targetLocation: true)], heading: 0);
        var start = rig.Pose.Position;
        rig.Tick(400);
        Assert.IsTrue(Vector3.Distance(start, rig.Pose.Position) < 1, "it stays where it is");
        Assert.AreEqual(MathF.PI / 2, rig.Pose.Heading, 0.1f, "it faces the target at +X");
    }

    [TestMethod]
    public void AShipWithATargetLocationWeaponFarFromItsTargetClosesByPathFirst()
    {
        var rig = Build(V(0, 0), V(3000, 0), [Weapon(range: 1000, targetLocation: true)], heading: MathF.PI / 2);
        rig.Tick(600);
        Assert.IsTrue(
            rig.Distance < 3000 - 30,
            $"it closes while the gap exceeds the range less 50, at {rig.Distance}"
        );
    }

    [TestMethod]
    public void ASpecialWeaponsLongerRangeDoesNotChangeTheShipsBand()
    {
        // the band is the longest non-special range, 100: the dogfight starts at a gap of 50, so a ship 80 away is
        // still closing; a band of 1000 would have it in the dogfight already
        var rig = Build(
            V(0, 0),
            V(80, 0),
            [Weapon(range: 100), Weapon(range: 1000, special: true)],
            heading: MathF.PI / 2
        );
        rig.Tick(2);
        Assert.AreEqual(MoveKind.MoveTo, rig.World.State(rig.A)!.Intent.Kind);
        var plain = Build(V(0, 0), V(80, 0), [Weapon(range: 1000)], heading: MathF.PI / 2);
        plain.Tick(2);
        Assert.AreEqual(MoveKind.Dogfight, plain.World.State(plain.A)!.Intent.Kind);
    }

    [TestMethod]
    public void ABorgShipAttackingDoesNotTurnToFaceItsTarget()
    {
        var rig = Build(V(0, 0), V(40, 0), borg: true, heading: 0);
        rig.Tick(600);
        Assert.AreEqual(0f, rig.Pose.Heading, 0.01f);
    }

    [TestMethod]
    public void ANonBorgShipInsideItsRangeLevelsAndTurnsToFaceItsTarget()
    {
        var rig = Build(V(0, 0), V(40, 0), heading: 0); // the gap of 40 is within the range 100 less 50
        rig.Tick(600);
        Assert.AreEqual(MathF.PI / 2, rig.Pose.Heading, 0.1f);
        Assert.IsTrue(rig.Flight.Speed < 0.5f, "nearly still");
    }

    [TestMethod]
    public void AShipBeyondItsRangeDrivesAtFullThrottleAndInsideItAtOneThousandth()
    {
        // the attacker's radius of 100 puts the dogfight's start at a distance of range + 50
        var far = Build(V(0, 0), V(140, 0), radius: 100, heading: MathF.PI / 2);
        far.Tick(2);
        Assert.AreEqual(1f, far.Flight.Controls.Throttle, 1e-4f, "140 is beyond the range of 100");
        var near = Build(V(0, 0), V(90, 0), radius: 100, heading: MathF.PI / 2);
        near.Tick(2);
        Assert.AreEqual(0.001f, near.Flight.Controls.Throttle, 1e-4f, "90 is inside the range");
    }

    // The attacker's throttle after the target has drifted along +X by `perTick` each tick, in the direct move (the gap
    // of 60 is beyond the range less 50 and the distance within 400).
    static float ThrottleWhileTheTargetDrifts(float perTick)
    {
        var rig = Build(V(0, 0), V(0.5f, 0), [Weapon(range: 40)], heading: MathF.PI / 2);
        rig.Tick();
        for (var i = 1; i <= 3; i++)
        {
            rig.Poses.Record(rig.T, FlightFixtures.Cruiser(V(0.5f + perTick * i, 0)));
            rig.Tick();
        }

        Assert.AreEqual(MoveKind.MoveTo, rig.World.State(rig.A)!.Intent.Kind);

        return rig.Flight.Controls.Throttle;
    }

    [TestMethod]
    public void TheDirectMoveOfAnAttackTakesTheTargetsVelocityIntoTheSpring()
    {
        // 0x42fb90 gives 0x428e90 the ordered unit's velocity, which it hands to 0x42e4a0: a target pulling away
        // raises the throttle, one closing on the ship lowers it
        var away = ThrottleWhileTheTargetDrifts(0.005f);
        var still = ThrottleWhileTheTargetDrifts(0);
        var closing = ThrottleWhileTheTargetDrifts(-0.005f);
        Assert.IsTrue(away - still > 0.02f, $"away {away}, still {still}");
        Assert.IsTrue(still - closing > 0.02f, $"still {still}, closing {closing}");
    }

    [TestMethod]
    public void TheAttacksPathEndsWhenTheDogfightTakesOver()
    {
        var rig = Build(V(0, 0), V(0, 700), heading: 0);
        rig.Tick(10);
        Assert.IsNotNull(rig.Flight.Order, "it closes by path first");
        rig.Tick(3500);
        Assert.IsNull(rig.Flight.Order);
    }

    [TestMethod]
    public void AShipFarFromItsTargetClosesByPathThenStopsInsideItsRangeFacingIt()
    {
        // it starts bow to +X, a quarter turn off its target at +Z; its radius of 100 puts the dogfight's start at a
        // distance of 150, beyond the range of 100, so the drive beyond the range and the turn both run
        var rig = Build(V(0, 0), V(0, 700), radius: 100, heading: MathF.PI / 2);
        var drove = false;
        for (var i = 0; i < 3500; i++)
        {
            rig.Tick();
            if (rig.World.State(rig.A)!.Intent.Kind == MoveKind.Dogfight && rig.Distance > 100)
            {
                drove = true;
                Assert.IsNull(rig.Flight.Order, "the dogfight drives it, not a path");
            }
        }

        Assert.IsTrue(drove, "the dogfight drove it beyond the range");
        Assert.IsTrue(rig.Distance <= 100, $"within the range, at {rig.Distance}");
        Assert.IsTrue(rig.Flight.Speed < 0.5f, "nearly still");
        Assert.AreEqual(0f, rig.Pose.Heading, 0.1f);
    }
}
