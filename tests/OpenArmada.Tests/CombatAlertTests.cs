using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Sandbox;
using static OpenArmada.Tests.CombatFixtures;

namespace OpenArmada.Tests;

// Alert stances and target selection without orders, over units built in code.
[TestClass]
public class CombatAlertTests
{
    sealed record Spec
    {
        public required PlacedObject Object { get; init; }
        public required CombatProfile Profile { get; init; }
        public bool Mobile { get; init; } = true;
    }

    static Spec Ship(string label, int team, float x, float range = 100, bool green = false, bool canAttack = true) =>
        new()
        {
            Object = Placed(label, team, x),
            Profile = Profile(new[] { Weapon(range: range) }, defaultGreen: green, canAttack: canAttack),
        };

    static Spec Target(string label, int team, float x) =>
        new() { Object = Placed(label, team, x), Profile = Profile() };

    static CombatWorld World(params Spec[] specs) => WorldWithPoses(specs).World;

    static (CombatWorld World, UnitPoses Poses) WorldWithPoses(params Spec[] specs)
    {
        var poses = new UnitPoses(specs.Select(s => s.Object));
        var world = new CombatWorld(
            poses,
            specs.Select(s => new CombatUnit
            {
                Unit = s.Object,
                Profile = s.Profile,
                DefinitionName = s.Object.Label + ".odf",
                Radius = 0,
                Mobile = s.Mobile,
            }),
            1,
            () => 0
        );

        return (world, poses);
    }

    // Moves the unit to (x, 0) as its own motion would, so the world sees it there.
    static void Place(UnitPoses poses, PlacedObject unit, float x)
    {
        var motion = new ShipMotion(
            new PhysicsValues
            {
                ImpulseSpeed = 100,
                YawRotationRate = 100,
                MaxRoll = null,
            },
            unit.Position,
            0
        );
        motion.SetTarget(x, 0);
        poses.Advance(unit, motion, 100);
    }

    static string[] Targets(IEnumerable<CombatEvent> events, string shooter) =>
        events
            .OfType<ShotFired>()
            .Where(e => e.Shot.Shooter.Unit.Label == shooter)
            .Select(e => e.Shot.Target.Unit.Label)
            .Distinct()
            .ToArray();

    static int Fired(IEnumerable<CombatEvent> events, string shooter) =>
        events.OfType<ShotFired>().Count(e => e.Shot.Shooter.Unit.Label == shooter);

    static string? Aimed(IEnumerable<CombatEvent> events, string shooter) =>
        events.OfType<ShotFired>().FirstOrDefault(e => e.Shot.Shooter.Unit.Label == shooter)?.Shot.Target.Unit.Label;

    [TestMethod]
    public void AUnitStartsAtRedUnlessItsDefinitionSetsDefaultGreenAlert()
    {
        var red = Ship("r", 1, 0);
        var green = Ship("g", 1, 10, green: true);
        var world = World(red, green);
        Assert.AreEqual(AlertStance.Red, world.State(red.Object)!.Alert);
        Assert.AreEqual(AlertStance.Green, world.State(green.Object)!.Alert);
    }

    [TestMethod]
    public void UnitsOfDifferentTeamsInRangeFireOnTheirOwn()
    {
        var world = World(Ship("a", 1, 0), Ship("b", 2, 50));
        var events = world.Step(0.02);
        Assert.AreEqual(1, Fired(events, "a"));
        Assert.AreEqual(1, Fired(events, "b"));
    }

    [TestMethod]
    public void UnitsOfOneTeamAndTeamZeroObjectsNeverDrawFire()
    {
        var world = World(Ship("a", 1, 0), Target("friend", 1, 10), Target("rock", 0, 20));
        Assert.AreEqual(0, Fired(world.Step(2), "a"));
    }

    [TestMethod]
    public void ATargetBeyondTheLongestRangeLessTwentyFiveIsNotPicked()
    {
        var world = World(Ship("a", 1, 0), Target("t", 2, 90));
        Assert.AreEqual(0, Fired(world.Step(0.02), "a"));
    }

    [TestMethod]
    public void ARedUnitPicksTheNearestHostileUnitInRange()
    {
        var world = World(Ship("a", 1, 0), Target("far", 2, 70), Target("near", 3, 30));
        Assert.AreEqual("near", Aimed(world.Step(0.02), "a"));
    }

    [TestMethod]
    public void ARedUnitClosesOnItsPickWithoutTakingAnOrder()
    {
        var a = Ship("a", 1, 0);
        var t = Target("t", 2, 70);
        var world = World(a, t);
        world.Step(0.02);
        Assert.IsNull(world.State(a.Object)!.Order);
        Assert.AreEqual(
            new MoveIntent
            {
                Kind = MoveKind.MoveTo,
                X = 70,
                Z = 0,
            },
            world.State(a.Object)!.Intent
        );
    }

    [TestMethod]
    public void ARedUnitThatCannotAttackFiresButGetsNoOrderAndNeverMoves()
    {
        var a = Ship("a", 1, 0, canAttack: false);
        var world = World(a, Target("t", 2, 50));
        Assert.AreEqual(1, Fired(world.Step(0.02), "a"));
        Assert.IsNull(world.State(a.Object)!.Order);
        Assert.AreEqual(MoveKind.None, world.State(a.Object)!.Intent.Kind);
    }

    [TestMethod]
    public void ARedUnitHitFromBeyondItsRangeMovesTowardTheAttacker()
    {
        var a = Ship("a", 1, 0, range: 100);
        var shooter = new Spec
        {
            Object = Placed("s", 2, 150),
            Profile = Profile(new[] { Weapon(range: 200) }),
            Mobile = false,
        };
        var world = World(a, shooter);
        world.Step(1.2); // the beam lands after its life of one second
        Assert.AreEqual(
            new MoveIntent
            {
                Kind = MoveKind.MoveTo,
                X = 150,
                Z = 0,
            },
            world.State(a.Object)!.Intent
        );
    }

    [TestMethod]
    public void AYellowUnitFiresAtTheNearestHostileUnitInRangeAndNeverMoves()
    {
        var a = Ship("a", 1, 0);
        var world = World(a, Target("far", 2, 70), Target("near", 3, 30));
        world.State(a.Object)!.Alert = AlertStance.Yellow;
        Assert.AreEqual("near", Aimed(world.Step(0.02), "a"));
        Assert.IsNull(world.State(a.Object)!.Order);
        Assert.AreEqual(MoveKind.None, world.State(a.Object)!.Intent.Kind);
    }

    [TestMethod]
    public void AGreenUnitFiresAtNothingUntilItIsOrderedToAttack()
    {
        var a = Ship("a", 1, 0, green: true);
        var t = Target("t", 2, 50);
        var world = World(a, t);
        Assert.AreEqual(0, Fired(world.Step(2), "a"));
        world.Attack(a.Object, t.Object);
        Assert.AreEqual("t", Aimed(world.Step(0.02), "a"));
    }

    [TestMethod]
    public void AGreenUnitOrderedToAttackAFarTargetFiresAtNothingElse()
    {
        var a = Ship("a", 1, 0, green: true);
        var far = Target("far", 2, 1000);
        var world = World(a, far, Target("near", 3, 30));
        world.Attack(a.Object, far.Object);
        Assert.AreEqual(0, Fired(world.Step(2), "a"));
    }

    [TestMethod]
    public void ARedUnitWithAMoveOrderKeepsItEvenWhenItsFirstStepTurnsInPlace()
    {
        var a = Ship("a", 1, 0);
        var world = World(a, Target("t", 2, 70));
        world.Move(a.Object, -500, 0); // the position stays while the ship turns, so the unit looks stopped
        world.Step(0.02);
        Assert.AreEqual(MoveKind.None, world.State(a.Object)!.Intent.Kind); // no closing on the hostile; the player's move is the sandbox's
        Assert.IsFalse(world.State(a.Object)!.AutoMove);
    }

    [TestMethod]
    public void AMoveOrderReplacedByAnAttackOrderLeavesTheUnitIdleOnceTheTargetIsDestroyed()
    {
        var a = Ship("a", 1, 0);
        var far = Target("far", 2, 1000);
        var near = Target("near", 3, 70);
        var world = World(a, far, near);
        world.Move(a.Object, -500, 0);
        world.Attack(a.Object, far.Object);
        world.State(far.Object)!.Destroyed = true;
        world.Step(0.02);
        Assert.AreEqual("near", world.State(a.Object)!.Target!.Unit.Label);
    }

    [TestMethod]
    public void AMoveOrderLostToDestroyedEnginesDoesNotKeepTheRepairedUnitBusy()
    {
        var a = new Spec
        {
            Object = Placed("a", 1, 0),
            Profile = Profile(new[] { Weapon(range: 100) }, systems: new[] { Sys("Engines") }),
        };
        var t = Target("t", 2, 70);
        var world = World(a, t);
        var engines = world.State(a.Object)!.Systems["Engines"];
        world.Move(a.Object, -500, 0);
        engines.Destroyed = true;
        world.Step(0.02);
        engines.HitPoints = engines.Max;
        engines.Destroyed = false;
        world.Step(0.02);
        Assert.AreEqual(
            new MoveIntent
            {
                Kind = MoveKind.MoveTo,
                X = 70,
                Z = 0,
            },
            world.State(a.Object)!.Intent
        );
    }

    [TestMethod]
    public void AMoveOrderEndsOnArrivalAndTheUnitIsIdleAgain()
    {
        var a = Ship("a", 1, 0);
        var t = Target("t", 2, 70);
        var (world, poses) = WorldWithPoses(a, t);
        world.Move(a.Object, 10, 0);
        Place(poses, a.Object, 10);
        world.Step(0.02);
        Assert.AreEqual(
            new MoveIntent
            {
                Kind = MoveKind.MoveTo,
                X = 70,
                Z = 0,
            },
            world.State(a.Object)!.Intent
        );
    }

    [TestMethod]
    public void ARedUnitSwitchedToYellowStopsChasingAnAttackerAndFiresOnlyWithinTheRadius()
    {
        var a = Ship("a", 1, 0, range: 100);
        var shooter = new Spec
        {
            Object = Placed("s", 2, 150),
            Profile = Profile(new[] { Weapon(range: 200) }),
            Mobile = false,
        };
        var world = World(a, shooter);
        world.Step(1.2);
        Assert.AreEqual(MoveKind.MoveTo, world.State(a.Object)!.Intent.Kind);
        world.State(a.Object)!.Alert = AlertStance.Yellow;
        var events = world.Step(0.02);
        Assert.AreNotEqual(MoveKind.MoveTo, world.State(a.Object)!.Intent.Kind);
        Assert.AreEqual(0, Fired(events, "a"));
    }

    [TestMethod]
    public void ARedUnitSwitchedToYellowStopsClosingOnItsPick()
    {
        var a = Ship("a", 1, 0);
        var world = World(a, Target("t", 2, 70));
        world.Step(0.02);
        Assert.AreEqual(MoveKind.MoveTo, world.State(a.Object)!.Intent.Kind);
        world.State(a.Object)!.Alert = AlertStance.Yellow;
        world.Step(0.02);
        Assert.AreNotEqual(MoveKind.MoveTo, world.State(a.Object)!.Intent.Kind);
    }

    [TestMethod]
    public void ARedUnitSwitchedToGreenFiresAtNothingItWasNotOrderedToAttack()
    {
        var a = Ship("a", 1, 0);
        var world = World(a, Target("t", 2, 70));
        world.Step(0.02);
        world.State(a.Object)!.Alert = AlertStance.Green;
        Assert.AreEqual(0, Fired(world.Step(2), "a"));
    }

    [TestMethod]
    public void APlayersAttackOrderStaysWhenTheAlertChanges()
    {
        var a = Ship("a", 1, 0);
        var t = Target("t", 2, 70);
        var world = World(a, t);
        world.Attack(a.Object, t.Object);
        world.State(a.Object)!.Alert = AlertStance.Green;
        Assert.AreEqual("t", Aimed(world.Step(0.02), "a"));
    }

    [TestMethod]
    public void AYellowUnitKeepsItsHeldTargetWhileANearerOneEntersTheRadius()
    {
        var a = Ship("a", 1, 0);
        var incomer = Target("near", 3, 300);
        var (world, poses) = WorldWithPoses(a, Target("held", 2, 60), incomer);
        world.State(a.Object)!.Alert = AlertStance.Yellow;
        world.Step(0.02);
        Place(poses, incomer.Object, 10);
        CollectionAssert.AreEqual(new[] { "held" }, Targets(world.Step(2), "a"));
    }

    [TestMethod]
    public void AnOrderedUnitKeepsItsHeldFallbackWhileANearerOneEntersTheRadius()
    {
        var a = Ship("a", 1, 0);
        var far = Target("far", 4, 1000);
        var incomer = Target("near", 3, 300);
        var (world, poses) = WorldWithPoses(a, far, Target("held", 2, 60), incomer);
        world.Attack(a.Object, far.Object);
        world.Step(0.02);
        Place(poses, incomer.Object, 10);
        CollectionAssert.AreEqual(new[] { "held" }, Targets(world.Step(2), "a"));
    }

    [TestMethod]
    public void ARedUnitWhosePickTurnsDerelictFiresAtAndClosesOnAnotherHostileNextTick()
    {
        var a = Ship("a", 1, 0);
        var world = World(a, Target("first", 2, 20), Target("second", 3, 60));
        Assert.AreEqual("first", Aimed(world.Step(0.02), "a"));
        world.Units.First(u => u.Unit.Label == "first").Derelict = true;
        world.Step(0.02);
        Assert.AreEqual("second", world.State(a.Object)!.Target!.Unit.Label);
        Assert.AreEqual(60, world.State(a.Object)!.Intent.X);
        Assert.AreEqual("second", Aimed(world.Step(2), "a"));
    }

    [TestMethod]
    public void ARedUnitKeepsAPickThatStaysValidAndDropsOneThatLeavesTheRadius()
    {
        var a = Ship("a", 1, 0);
        var incomer = Target("near", 3, 300);
        var (world, poses) = WorldWithPoses(a, Target("held", 2, 60), incomer);
        world.Step(0.02);
        Place(poses, incomer.Object, 10);
        CollectionAssert.AreEqual(new[] { "held" }, Targets(world.Step(2), "a"));
        Place(poses, incomer.Object, 70);
        var held = world.Units.First(u => u.Unit.Label == "held");
        Place(poses, held.Unit, 500);
        CollectionAssert.AreEqual(new[] { "near" }, Targets(world.Step(2), "a"));
        Assert.AreEqual(70, world.State(a.Object)!.Intent.X);
    }

    [TestMethod]
    public void APlayersAttackOrderKeepsATargetThatTurnsDerelictAndTheUnitKeepsFiring()
    {
        var a = Ship("a", 1, 0);
        var t = Target("t", 2, 20);
        var world = World(a, t, Target("other", 3, 40));
        world.Attack(a.Object, t.Object);
        world.State(t.Object)!.Derelict = true;
        Assert.AreEqual("t", Aimed(world.Step(0.02), "a"));
        Assert.AreSame(world.State(t.Object), world.State(a.Object)!.Order);
    }

    [TestMethod]
    public void ADerelictUnitIsNeverPicked()
    {
        var a = Ship("a", 1, 0);
        var world = World(a, Target("derelict", 2, 20), Target("live", 3, 60));
        world.Units.First(u => u.Unit.Label == "derelict").Derelict = true;
        Assert.AreEqual("live", Aimed(world.Step(0.02), "a"));
    }

    [TestMethod]
    public void AUnitWithOnlyADerelictInRangeFiresAtNothing()
    {
        var world = World(Ship("a", 1, 0), Target("derelict", 2, 20));
        world.Units.First(u => u.Unit.Label == "derelict").Derelict = true;
        Assert.AreEqual(0, Fired(world.Step(2), "a"));
    }
}
