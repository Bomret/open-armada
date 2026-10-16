using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Assets;
using OpenArmada.Sandbox;
using static OpenArmada.Tests.CombatFixtures;

namespace OpenArmada.Tests;

// The pure combat rules of docs/combat-rules.md, with values built in code.
[TestClass]
public class CombatRulesTests
{
    [TestMethod]
    public void UnitsOfDifferentTeamsAreHostile() => Assert.IsTrue(CombatRules.Hostile(1, 2));

    [TestMethod]
    public void UnitsOfOneTeamAreNotHostile() => Assert.IsFalse(CombatRules.Hostile(3, 3));

    [TestMethod]
    public void TeamZeroIsHostileToNoOneAndNoOneToIt()
    {
        Assert.IsFalse(CombatRules.Hostile(0, 2));
        Assert.IsFalse(CombatRules.Hostile(2, 0));
        Assert.IsFalse(CombatRules.Hostile(0, 0));
    }

    [TestMethod]
    public void ToHitChanceTakesTheOverrideOfTheTargetsDefinitionName()
    {
        var hit = new TargetValue(0.5f, new Dictionary<string, float> { ["zmama.odf"] = 0.9f });
        var weapon = Weapon() with { HitChance = hit };
        Assert.AreEqual(0.9, CombatRules.ToHitChance(weapon, "zmama.odf", false, false, false, false), 1e-6);
        Assert.AreEqual(0.5, CombatRules.ToHitChance(weapon, "other.odf", false, false, false, false), 1e-6);
    }

    [TestMethod]
    public void ToHitChanceAddsTheWarpAndStopModifiersThatApply()
    {
        var weapon = Weapon(hitChance: 0.5f, stopModifier: 0.01f, toHitStopModifier: 0.02f);
        Assert.AreEqual(
            0.5 + 0.1 + 0.2 + 0.01 + 0.02,
            CombatRules.ToHitChance(weapon, "t", true, true, true, true),
            1e-6
        );
        Assert.AreEqual(0.5, CombatRules.ToHitChance(weapon, "t", false, false, false, false), 1e-6);
        Assert.AreEqual(0.5 + 0.2, CombatRules.ToHitChance(weapon, "t", false, true, false, false), 1e-6);
    }

    static UnitCombatState WithCrew(double crew)
    {
        var state = State(Profile());
        state.Crew = crew;
        return state;
    }

    [TestMethod]
    public void ReloadIsShotDelayWithoutACrewEffect() =>
        Assert.AreEqual(4, CombatRules.ReloadDelay(Weapon(delay: 4), WithCrew(10)), 1e-6);

    [TestMethod]
    public void ReloadIsScaledByTheRacesFactorWhenCrewAffectsIt()
    {
        var weapon = Weapon(delay: 4, crewAffects: true);
        Assert.AreEqual(4, CombatRules.ReloadDelay(weapon, WithCrew(100)), 1e-6);
        Assert.AreEqual(8, CombatRules.ReloadDelay(weapon, WithCrew(40)), 1e-6); // yellow x2
        Assert.AreEqual(12, CombatRules.ReloadDelay(weapon, WithCrew(20)), 1e-6); // red x3
    }

    [TestMethod]
    public void DamageIsTheBaseWithTheVarianceEitherWay()
    {
        var ordnance = Beam(damage: 10, variance: 4);
        Assert.AreEqual(6, CombatRules.Damage(ordnance, "t", 0), 1e-6);
        Assert.AreEqual(10, CombatRules.Damage(ordnance, "t", 0.5), 1e-6);
        Assert.AreEqual(14, CombatRules.Damage(ordnance, "t", 1), 1e-6);
    }

    [TestMethod]
    public void DamageTakesTheOverrideOfTheTargetsDefinitionName()
    {
        var ordnance = Beam() with
        {
            DamageBase = new TargetValue(30, new Dictionary<string, float> { ["zmama.odf"] = 2 }),
        };
        Assert.AreEqual(2, CombatRules.Damage(ordnance, "zmama.odf", 0.5), 1e-6);
        Assert.AreEqual(30, CombatRules.Damage(ordnance, "zmama", 0.5), 1e-6);
    }

    [TestMethod]
    public void TheLocationRollIsReducedByEachHitPercentInOrderAndOvershootingIsCritical()
    {
        var profile = Profile(hull: 30) with
        {
            Systems = new List<SystemProfile> { new("Engines", 5, 10, 0, 0), new("LifeSupport", 5, 20, 0, 0) },
        };
        Assert.AreEqual(HitLocation.Engines, CombatRules.Location(profile, 9));
        Assert.AreEqual(HitLocation.LifeSupport, CombatRules.Location(profile, 10));
        Assert.AreEqual(HitLocation.LifeSupport, CombatRules.Location(profile, 29));
        Assert.AreEqual(HitLocation.Hull, CombatRules.Location(profile, 30));
        Assert.AreEqual(HitLocation.Hull, CombatRules.Location(profile, 59));
        Assert.AreEqual(HitLocation.Critical, CombatRules.Location(profile, 60));
    }

    static UnitCombatState Target(float shields = 100, float crew = 100, float delay = 0) =>
        State(Profile(maxHealth: shields, crew: crew, shieldDelay: delay));

    [TestMethod]
    public void DamageAboveTheThresholdWhileShieldsAreUpCostsCrew()
    {
        var target = Target();
        CombatRules.Apply(target, Beam(threshold: 4, shieldCrew: 0.5f), 10, HitLocation.Critical, 1);
        Assert.AreEqual(100 - (1 + 100 * 0.5 * 6 / 100), target.Crew, 1e-6);
    }

    [TestMethod]
    public void DamageBelowTheThresholdCostsNoCrew()
    {
        var target = Target();
        CombatRules.Apply(target, Beam(threshold: 12, shieldCrew: 0.5f), 10, HitLocation.Critical, 1);
        Assert.AreEqual(100, target.Crew, 1e-6);
    }

    [TestMethod]
    public void TheShieldPartIsTheDamageTimesTheProtectionAndTheRestIsTheSystemsPart()
    {
        var target = Target();
        var hit = CombatRules.Apply(target, Beam(threshold: 99), 10, HitLocation.Engines, 0.6);
        Assert.AreEqual(6, hit.ShieldLoss, 1e-6);
        Assert.AreEqual(94, target.Shields, 1e-6);
        Assert.AreEqual(4, hit.Points);
        Assert.AreEqual(4, target.SystemDamage["Engines"]);
    }

    [TestMethod]
    public void DamageThatTakesTheShieldsBelowZeroPassesIntoTheSystemsPart()
    {
        var target = Target(shields: 5);
        var hit = CombatRules.Apply(target, Beam(threshold: 99), 10, HitLocation.Engines, 1);
        Assert.AreEqual(0, target.Shields, 1e-6);
        Assert.AreEqual(5, hit.Points);
    }

    [TestMethod]
    public void WithShieldsDownTheWholeDamageIsTheSystemsPart()
    {
        var target = Target(shields: 0);
        var hit = CombatRules.Apply(target, Beam(threshold: 0, shieldCrew: 9), 10, HitLocation.Engines, 0.5);
        Assert.AreEqual(10, hit.Points);
        Assert.AreEqual(100, target.Crew, 1e-6); // no threshold crew loss without shields
    }

    [TestMethod]
    public void AHullHitCostsCrewByTheHullCrewModifier()
    {
        var target = Target(shields: 0);
        var hit = CombatRules.Apply(target, Beam(hullCrew: 10), 3, HitLocation.Hull, 1);
        Assert.AreEqual(1 + 100 * 10 * 3 / 100.0, hit.CrewLoss, 1e-6);
        Assert.AreEqual(100 - 31, target.Crew, 1e-6);
    }

    [TestMethod]
    public void ACriticalHitOfOneWholePointDestroysTheUnit()
    {
        var target = Target(shields: 0);
        var hit = CombatRules.Apply(target, Beam(), 1, HitLocation.Critical, 1);
        Assert.IsTrue(hit.Destroyed);
        Assert.IsTrue(target.Destroyed);
    }

    [TestMethod]
    public void AFractionalSystemsPartCarriesOverUntilItMakesAWholePoint()
    {
        var target = Target(shields: 0);
        var first = CombatRules.Apply(target, Beam(), 0.6, HitLocation.Critical, 1);
        Assert.IsFalse(first.Destroyed);
        Assert.AreEqual(0, first.Points);
        var second = CombatRules.Apply(target, Beam(), 0.6, HitLocation.Critical, 1);
        Assert.IsTrue(second.Destroyed);
        Assert.AreEqual(0.2, target.SystemsRemainder, 1e-6);
    }

    [TestMethod]
    public void ReachingZeroTakesTheShieldsDownForTheDelay()
    {
        var target = Target(shields: 10, delay: 3);
        CombatRules.Apply(target, Beam(threshold: 99), 10, HitLocation.Engines, 1);
        Assert.AreEqual(3, target.ShieldDown, 1e-6);
    }

    [TestMethod]
    public void AHitThatLeavesTheShieldsAboveZeroDoesNotStartTheDelay()
    {
        var target = Target(shields: 10, delay: 3);
        CombatRules.Apply(target, Beam(threshold: 99), 4, HitLocation.Engines, 1);
        Assert.AreEqual(0, target.ShieldDown, 1e-6);
    }

    [TestMethod]
    public void AHitOnShieldsAlreadyDownDoesNotRestartTheDelay()
    {
        var target = Target(shields: 0, delay: 3);
        target.ShieldDown = 1;
        CombatRules.Apply(target, Beam(), 4, HitLocation.Engines, 1);
        Assert.AreEqual(1, target.ShieldDown, 1e-6);
    }

    static UnitCombatState Regaining(double shields, double down)
    {
        var target = State(Profile(maxHealth: 100, shieldRate: 10));
        target.Shields = shields;
        target.ShieldDown = down;
        return target;
    }

    [TestMethod]
    public void ShieldsRegainTheRatePerSecondUpToTheMaximum()
    {
        var target = Regaining(50, 0);
        CombatRules.RegainShields(target, 1);
        Assert.AreEqual(60, target.Shields, 1e-6);
        CombatRules.RegainShields(target, 100);
        Assert.AreEqual(100, target.Shields, 1e-6);
    }

    [TestMethod]
    public void ShieldsRegainNothingWhileDownAndTheRestOfAStepAfterwards()
    {
        var target = Regaining(0, 2);
        CombatRules.RegainShields(target, 1.5);
        Assert.AreEqual(0, target.Shields, 1e-6);
        CombatRules.RegainShields(target, 1.5); // 0.5 s down, 1 s regaining
        Assert.AreEqual(10, target.Shields, 1e-6);
    }
}
