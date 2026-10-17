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
    sealed record Spec(PlacedObject Object, CombatProfile Profile, bool Mobile = true);

    static Spec Ship(string label, int team, float x, float range = 100, bool green = false, bool canAttack = true) =>
        new(Placed(label, team, x), Profile(new[] { Weapon(range: range) }, defaultGreen: green, canAttack: canAttack));

    static Spec Target(string label, int team, float x) => new(Placed(label, team, x), Profile());

    static CombatWorld World(params Spec[] specs) =>
        new(
            new UnitPoses(specs.Select(s => s.Object)),
            specs.Select(s => new CombatUnit(s.Object, s.Profile, s.Object.Label + ".odf", 0, s.Mobile)),
            1,
            () => 0
        );

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
    public void ARedUnitIsGivenAnAttackOrderOnItsPickAndMovesToAttackIt()
    {
        var a = Ship("a", 1, 0);
        var t = Target("t", 2, 70);
        var world = World(a, t);
        world.Step(0.02);
        Assert.AreSame(world.State(t.Object), world.State(a.Object)!.Order);
        Assert.AreEqual(new MoveIntent(MoveKind.MoveTo, 70, 0), world.State(a.Object)!.Intent);
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
        var shooter = new Spec(Placed("s", 2, 150), Profile(new[] { Weapon(range: 200) }), false);
        var world = World(a, shooter);
        world.Step(1.2); // the beam lands after its life of one second
        Assert.AreEqual(new MoveIntent(MoveKind.MoveTo, 150, 0), world.State(a.Object)!.Intent);
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
