using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Assets;
using OpenArmada.Sandbox;
using static OpenArmada.Tests.CombatFixtures;

namespace OpenArmada.Tests;

// The pure combat rules of docs/rules/combat-rules.md, with values built in code.
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
        var hit = new TargetValue
        {
            Default = 0.5f,
            Overrides = new Dictionary<string, float> { ["zmama.odf"] = 0.9f },
        };
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
    public void ARollBelowZeroDealsNoneAndNeverRaisesTheShields()
    {
        var ordnance = Beam(damage: 2, variance: 4);
        var target = State(Profile(maxHealth: 100));
        target.Shields = 50;
        var hit = CombatRules.Apply(target, ordnance, CombatRules.Damage(ordnance, "t", 0), HitLocation.Hull, 1);
        Assert.AreEqual(0, hit.Damage, 1e-6);
        Assert.AreEqual(50, target.Shields, 1e-6);
    }

    [TestMethod]
    public void DamageTakesTheOverrideOfTheTargetsDefinitionName()
    {
        var ordnance = Beam() with
        {
            DamageBase = new TargetValue
            {
                Default = 30,
                Overrides = new Dictionary<string, float> { ["zmama.odf"] = 2 },
            },
        };
        Assert.AreEqual(2, CombatRules.Damage(ordnance, "zmama.odf", 0.5), 1e-6);
        Assert.AreEqual(30, CombatRules.Damage(ordnance, "zmama", 0.5), 1e-6);
    }

    [TestMethod]
    public void TheLocationRollIsReducedByEachHitPercentInOrderAndOvershootingIsCritical()
    {
        var profile = Profile(hull: 30) with
        {
            Systems = new List<SystemProfile>
            {
                new()
                {
                    Name = "Engines",
                    HitPoints = 5,
                    HitPercent = 10,
                    CrewLoss = 0,
                    RepairTime = 0,
                },
                new()
                {
                    Name = "LifeSupport",
                    HitPoints = 5,
                    HitPercent = 20,
                    CrewLoss = 0,
                    RepairTime = 0,
                },
            },
        };
        // (the roll is read through a state, so the shares of destroyed systems apply)
        var target = State(profile);
        Assert.AreEqual(HitLocation.Engines, CombatRules.Location(target, 9));
        Assert.AreEqual(HitLocation.LifeSupport, CombatRules.Location(target, 10));
        Assert.AreEqual(HitLocation.LifeSupport, CombatRules.Location(target, 29));
        Assert.AreEqual(HitLocation.Hull, CombatRules.Location(target, 30));
        Assert.AreEqual(HitLocation.Hull, CombatRules.Location(target, 59));
        Assert.AreEqual(HitLocation.Critical, CombatRules.Location(target, 60));
    }

    static UnitCombatState WithSystems(params SystemProfile[] systems) =>
        State(Profile(systems: systems, hull: 10, maxHealth: 100));

    [TestMethod]
    public void ADestroyedSystemsPercentIsSharedEvenlyAmongTheSystemsStillWorking()
    {
        // engines 20, life support 10, weapons 10, hull 10; destroying engines gives each of the 2 others 10 more
        var target = WithSystems(
            Sys("Engines", percent: 20),
            Sys("LifeSupport", percent: 10),
            Sys("Weapons", percent: 10)
        );
        target.DestroySystem("Engines");
        Assert.AreEqual(HitLocation.LifeSupport, CombatRules.Location(target, 0));
        Assert.AreEqual(HitLocation.LifeSupport, CombatRules.Location(target, 19));
        Assert.AreEqual(HitLocation.Weapons, CombatRules.Location(target, 20));
        Assert.AreEqual(HitLocation.Weapons, CombatRules.Location(target, 39));
        Assert.AreEqual(HitLocation.Hull, CombatRules.Location(target, 40));
        Assert.AreEqual(HitLocation.Critical, CombatRules.Location(target, 50));
    }

    [TestMethod]
    public void OnADerelictUnitADestroyedSystemsWholePercentGoesToTheCriticalShare()
    {
        // life support 10 went to engines and weapons before the unit went derelict; engines now holds 25, all for critical
        var target = WithSystems(
            Sys("LifeSupport", percent: 10),
            Sys("Engines", percent: 20),
            Sys("Weapons", percent: 10)
        );
        target.DestroySystem("LifeSupport");
        target.Derelict = true;
        target.DestroySystem("Engines");
        Assert.AreEqual(HitLocation.Weapons, CombatRules.Location(target, 14));
        Assert.AreEqual(HitLocation.Hull, CombatRules.Location(target, 15));
        Assert.AreEqual(HitLocation.Hull, CombatRules.Location(target, 24));
        Assert.AreEqual(HitLocation.Critical, CombatRules.Location(target, 25));
    }

    [TestMethod]
    public void ADestroyedSystemKeepsItsSharesWithTheSystemsThatTookThemUntilThoseAreDestroyed()
    {
        var target = WithSystems(Sys("Engines", percent: 20), Sys("Weapons", percent: 10), Sys("Sensors", percent: 10));
        target.DestroySystem("Engines");
        target.Derelict = true;
        Assert.AreEqual(HitLocation.Weapons, CombatRules.Location(target, 19));
        Assert.AreEqual(HitLocation.Sensors, CombatRules.Location(target, 20));
        target.DestroySystem("Weapons");
        Assert.AreEqual(HitLocation.Sensors, CombatRules.Location(target, 0));
        Assert.AreEqual(HitLocation.Hull, CombatRules.Location(target, 20));
    }

    [TestMethod]
    public void WithNoSystemWorkingTheDestroyedPercentsGoToTheCriticalShare()
    {
        var target = WithSystems(Sys("Engines", percent: 20), Sys("Weapons", percent: 10));
        target.DestroySystem("Engines");
        target.DestroySystem("Weapons");
        Assert.AreEqual(HitLocation.Hull, CombatRules.Location(target, 9));
        Assert.AreEqual(HitLocation.Critical, CombatRules.Location(target, 10));
    }

    [TestMethod]
    public void ASystemRepairedToFullTakesItsPercentBack()
    {
        var target = WithSystems(Sys("Engines", percent: 20), Sys("Weapons", percent: 10));
        target.DestroySystem("Engines");
        Assert.AreEqual(HitLocation.Weapons, CombatRules.Location(target, 29));
        target.RestoreSystem("Engines");
        Assert.AreEqual(HitLocation.Engines, CombatRules.Location(target, 19));
        Assert.AreEqual(HitLocation.Weapons, CombatRules.Location(target, 20));
    }

    [TestMethod]
    public void RestoringEveryDestroyedSystemInAnyOrderReturnsEverySystemToItsOwnPercent()
    {
        var target = WithSystems(Sys("Engines"), Sys("LifeSupport"), Sys("Weapons"), Sys("Sensors"));
        target.DestroySystem("Engines");
        target.DestroySystem("LifeSupport");
        target.RestoreSystem("LifeSupport");
        target.RestoreSystem("Engines");
        foreach (var s in target.Systems.Values)
        {
            Assert.AreEqual(s.Profile.HitPercent, s.HitPercent, 1e-6, s.Profile.Name);
        }
    }

    static double Live(UnitCombatState unit, string name) => unit.Systems[name].HitPercent;

    [TestMethod]
    public void RestoringAWorkingSystemLeavesEveryLivePercentUnchanged()
    {
        var target = WithSystems(Sys("Engines", percent: 20), Sys("Weapons", percent: 10), Sys("Sensors", percent: 10));
        target.DestroySystem("Engines");
        target.RestoreSystem("Weapons");
        Assert.AreEqual(0, Live(target, "Engines"), 1e-6);
        Assert.AreEqual(20, Live(target, "Weapons"), 1e-6);
        Assert.AreEqual(20, Live(target, "Sensors"), 1e-6);
    }

    [TestMethod]
    public void UnequalPercentsGiveBackInProportionToTheExcessWhateverTheRestoreOrder()
    {
        var target = WithSystems(
            Sys("Engines", percent: 30),
            Sys("LifeSupport", percent: 10),
            Sys("Weapons", percent: 10),
            Sys("Sensors", percent: 10)
        );
        target.DestroySystem("Engines");
        target.DestroySystem("LifeSupport");
        Assert.AreEqual(30, Live(target, "Weapons"), 1e-6);
        Assert.AreEqual(30, Live(target, "Sensors"), 1e-6);
        // engines first: weapons and sensors hold 20 excess each, engines take 30 of the 40
        target.RestoreSystem("Engines");
        Assert.AreEqual(30, Live(target, "Engines"), 1e-6);
        Assert.AreEqual(15, Live(target, "Weapons"), 1e-6);
        Assert.AreEqual(15, Live(target, "Sensors"), 1e-6);
        target.RestoreSystem("LifeSupport");
        Assert.AreEqual(30, Live(target, "Engines"), 1e-6);
        Assert.AreEqual(10, Live(target, "LifeSupport"), 1e-6);
        Assert.AreEqual(10, Live(target, "Weapons"), 1e-6);
        Assert.AreEqual(10, Live(target, "Sensors"), 1e-6);
    }

    [TestMethod]
    public void UnequalPercentsGiveBackInProportionWhenTheOtherOrderRestores()
    {
        var target = WithSystems(
            Sys("Engines", percent: 30),
            Sys("LifeSupport", percent: 10),
            Sys("Weapons", percent: 10),
            Sys("Sensors", percent: 10)
        );
        target.DestroySystem("Engines");
        target.DestroySystem("LifeSupport");
        // life support first: it takes 10 of the 40 excess, a quarter from each donor
        target.RestoreSystem("LifeSupport");
        Assert.AreEqual(0, Live(target, "Engines"), 1e-6);
        Assert.AreEqual(10, Live(target, "LifeSupport"), 1e-6);
        Assert.AreEqual(25, Live(target, "Weapons"), 1e-6);
        Assert.AreEqual(25, Live(target, "Sensors"), 1e-6);
        target.RestoreSystem("Engines");
        Assert.AreEqual(30, Live(target, "Engines"), 1e-6);
        Assert.AreEqual(10, Live(target, "LifeSupport"), 1e-6);
        Assert.AreEqual(10, Live(target, "Weapons"), 1e-6);
        Assert.AreEqual(10, Live(target, "Sensors"), 1e-6);
    }

    [TestMethod]
    public void DonorsHoldingUnequalExcessGiveBackInProportionAndNotEvenly()
    {
        var target = WithSystems(Sys("Engines"), Sys("LifeSupport"), Sys("Weapons"), Sys("Sensors"));
        target.DestroySystem("Engines");
        target.DestroySystem("LifeSupport");
        target.RestoreSystem("LifeSupport");
        target.DestroySystem("Weapons");
        // life support holds 10 + 7.5 and sensors 15 + 7.5, so the excess is 7.5 against 12.5
        Assert.AreEqual(17.5, Live(target, "LifeSupport"), 1e-6);
        Assert.AreEqual(22.5, Live(target, "Sensors"), 1e-6);
        // engines take 10 of the 20 excess, half from each donor: 3.75 and 6.25 (an even split would give 5 each)
        target.RestoreSystem("Engines");
        Assert.AreEqual(10, Live(target, "Engines"), 1e-6);
        Assert.AreEqual(13.75, Live(target, "LifeSupport"), 1e-6);
        Assert.AreEqual(16.25, Live(target, "Sensors"), 1e-6);
    }

    [TestMethod]
    public void ASystemRestoredWhileAnotherIsStillDestroyedTakesItsPercentFromTheCriticalShareWhenNoSystemHoldsExcess()
    {
        var target = WithSystems(Sys("Engines", percent: 20), Sys("Weapons", percent: 10));
        target.DestroySystem("Engines");
        target.DestroySystem("Weapons");
        target.RestoreSystem("Engines");
        Assert.AreEqual(20, Live(target, "Engines"), 1e-6);
        Assert.AreEqual(0, Live(target, "Weapons"), 1e-6);
        Assert.AreEqual(HitLocation.Engines, CombatRules.Location(target, 19));
        Assert.AreEqual(HitLocation.Hull, CombatRules.Location(target, 20));
        Assert.AreEqual(HitLocation.Critical, CombatRules.Location(target, 30));
        target.RestoreSystem("Weapons");
        Assert.AreEqual(20, Live(target, "Engines"), 1e-6);
        Assert.AreEqual(10, Live(target, "Weapons"), 1e-6);
    }

    [TestMethod]
    public void ADestroyingHitOnADerelictUnitFreesItsPercentForCriticalAndACriticalHitDestroysTheUnit()
    {
        // percents sum to 100: engines 40 and hull 60
        var target = State(Profile(systems: new[] { Sys("Engines", hitPoints: 3, percent: 40) }, hull: 60));
        target.Shields = 0;
        target.Crew = 0;
        target.Derelict = true;
        Assert.AreEqual(HitLocation.Hull, CombatRules.Location(target, 99));
        CombatRules.Apply(target, Beam(), 3, HitLocation.Engines, 1);
        Assert.AreEqual(HitLocation.Hull, CombatRules.Location(target, 59));
        Assert.AreEqual(HitLocation.Critical, CombatRules.Location(target, 60));
        Assert.IsTrue(CombatRules.Apply(target, Beam(), 1, HitLocation.Critical, 1).Destroyed);
        Assert.IsTrue(target.Dying);
    }

    [TestMethod]
    public void RepairToFullGivesTheDestroyedSystemItsPercentBackAndConservesTheTotal()
    {
        var target = Damaged(0);
        target.Shields = 0;
        CombatRules.Apply(target, Beam(), 5, HitLocation.Engines, 1);
        // engines 10 and hull 100: destroyed engines leave their 10 to the critical share
        Assert.AreEqual(HitLocation.Hull, CombatRules.Location(target, 99));
        Assert.AreEqual(HitLocation.Critical, CombatRules.Location(target, 100));
        CombatRules.Repair(target, 100);
        Assert.AreEqual(HitLocation.Engines, CombatRules.Location(target, 9));
        Assert.AreEqual(HitLocation.Hull, CombatRules.Location(target, 109));
        Assert.AreEqual(HitLocation.Critical, CombatRules.Location(target, 110));
    }

    [TestMethod]
    public void ASystemWithNoHitPointsInItsDefinitionIsNeverHit()
    {
        var target = WithSystems(Sys("Engines", hitPoints: 0, percent: 50), Sys("Weapons", percent: 10));
        for (var roll = 0; roll < 100; roll++)
        {
            Assert.AreNotEqual(HitLocation.Engines, CombatRules.Location(target, roll));
        }
    }

    [TestMethod]
    public void ASystemBroughtToZeroIsDestroyedAndCostsCrewOnePlusItsCrewLossPercent()
    {
        var target = WithSystems(Sys("Weapons", hitPoints: 3, crewLoss: 5));
        target.Shields = 0;
        var hit = CombatRules.Apply(target, Beam(), 3, HitLocation.Weapons, 1);
        Assert.IsTrue(target.Systems["Weapons"].Destroyed);
        Assert.AreEqual(0, target.Systems["Weapons"].HitPoints, 1e-6);
        Assert.AreEqual(1 + 100 * 5 / 100.0, hit.CrewLoss, 1e-6);
        Assert.AreEqual(94, target.Crew, 1e-6);
    }

    [TestMethod]
    public void ASystemStillAboveZeroCostsNoCrewAndIsNotDestroyed()
    {
        var target = WithSystems(Sys("Weapons", hitPoints: 4, crewLoss: 5));
        target.Shields = 0;
        var hit = CombatRules.Apply(target, Beam(), 3, HitLocation.Weapons, 1);
        Assert.IsFalse(target.Systems["Weapons"].Destroyed);
        Assert.AreEqual(0, hit.CrewLoss, 1e-6);
    }

    [TestMethod]
    public void TheSystemHitPointsBonusAddsToEverySystemThatHasHitPoints()
    {
        var profile = Profile(systems: new[] { Sys("Weapons", hitPoints: 3) });
        var target = new UnitCombatState(Placed("t", 1, 0), profile, "t.odf", 0, true, 2);
        Assert.AreEqual(5, target.Systems["Weapons"].Max, 1e-6);
        Assert.AreEqual(5, target.Systems["Weapons"].HitPoints, 1e-6);
    }

    [TestMethod]
    public void ADestroyedSystemOfADerelictUnitCostsNoCrew()
    {
        var target = WithSystems(Sys("Weapons", hitPoints: 3, crewLoss: 5));
        target.Shields = 0;
        target.Crew = 0;
        target.Derelict = true;
        var hit = CombatRules.Apply(target, Beam(), 3, HitLocation.Weapons, 1);
        Assert.IsTrue(target.Systems["Weapons"].Destroyed);
        Assert.AreEqual(0, hit.CrewLoss, 1e-6);
    }

    [TestMethod]
    public void CrewFallingBelowOneSetsItToZeroAndMakesTheUnitDerelict()
    {
        var target = Target(shields: 0, crew: 5);
        CombatRules.Apply(target, Beam(hullCrew: 100), 1, HitLocation.Hull, 1);
        Assert.AreEqual(0, target.Crew, 1e-6);
        Assert.IsTrue(target.Derelict);
    }

    [TestMethod]
    public void ATurningDerelictUnitLosesItsShields()
    {
        var target = Target(shields: 50, crew: 5);
        CombatRules.Apply(target, Beam(hullCrew: 100), 1, HitLocation.Hull, 0); // protection 0: shields untouched
        Assert.IsTrue(target.Derelict);
        Assert.AreEqual(0, target.Shields, 1e-6);
    }

    [TestMethod]
    public void ASystemWithRepairTimeZeroDoesNotRepair()
    {
        // stand-in: a repair time of zero is read as "this system does not repair"; the game's reading is not known.
        var zero = State(
            Profile(
                systems: new[] { Sys("Engines", hitPoints: 5, repairTime: 0) },
                crew: 100,
                maximumCrew: 100,
                race: RepairRace
            )
        );
        zero.Systems["Engines"].HitPoints = 0;
        zero.DestroySystem("Engines");
        CombatRules.Repair(zero, 100);
        Assert.IsTrue(zero.Systems["Engines"].Destroyed);
    }

    [TestMethod]
    public void AUnitThatStartsWithCrewBelowOneIsDerelictFromTheStartWithNoCrewAndNoShields()
    {
        var target = Target(shields: 50, crew: 0.5f);
        Assert.IsTrue(target.Derelict);
        Assert.AreEqual(0, target.Crew, 1e-6);
        Assert.AreEqual(0, target.Shields, 1e-6);
    }

    [TestMethod]
    public void ARaceWithRepairRedZeroDoesNotRepairAtRedCrewStatus()
    {
        var target = State(
            Profile(
                systems: new[] { Sys("Engines", hitPoints: 5, repairTime: 10) },
                crew: 100,
                maximumCrew: 100,
                race: RepairRace with
                {
                    RepairRed = 0,
                }
            )
        );
        target.Systems["Engines"].HitPoints = 1;
        target.Crew = 25;
        CombatRules.Repair(target, 100);
        Assert.AreEqual(1, target.Systems["Engines"].HitPoints, 1e-6);
    }

    [TestMethod]
    public void CrewStayingAtOrAboveOneLeavesTheUnitAlive()
    {
        var target = Target(shields: 0, crew: 3);
        CombatRules.Apply(target, Beam(), 1, HitLocation.Hull, 1);
        Assert.AreEqual(2, target.Crew, 1e-6);
        Assert.IsFalse(target.Derelict);
    }

    [TestMethod]
    public void ACriticalHitDestroysADerelictUnit()
    {
        var target = Target(shields: 0, crew: 0);
        target.Derelict = true;
        Assert.IsTrue(CombatRules.Apply(target, Beam(), 1, HitLocation.Critical, 1).Destroyed);
    }

    static readonly RaceValues RepairRace = new()
    {
        CrewYellowStatus = 0.5f,
        CrewRedStatus = 0.25f,
        WeaponYellow = 2,
        WeaponRed = 3,
        RepairYellow = 2,
        RepairRed = 4,
        RepairStrength = 1.5f,
    };

    static UnitCombatState Damaged(double hitPoints, double crew = 100, bool destroyed = false)
    {
        var target = State(
            Profile(
                systems: new[] { Sys("Engines", hitPoints: 5, repairTime: 10) },
                crew: 100,
                maximumCrew: 100,
                race: RepairRace
            )
        );
        target.Systems["Engines"].HitPoints = hitPoints;
        if (destroyed)
        {
            target.DestroySystem("Engines");
        }
        target.Crew = crew;

        return target;
    }

    [TestMethod]
    public void ADamagedSystemRegainsAHitPointPerRepairTimeOverMaximumTimesRepairStrength()
    {
        // 10 s / 5 hit points = 2 s per point, times strength 1.5 = 3 s per point; crew ratio 1 is above yellow
        var target = Damaged(1);
        CombatRules.Repair(target, 6);
        Assert.AreEqual(3, target.Systems["Engines"].HitPoints, 1e-6);
    }

    [TestMethod]
    public void RepairIsSlowerOrFasterByTheCrewStatusFactors()
    {
        var yellow = Damaged(1, crew: 50);
        var red = Damaged(1, crew: 25);
        CombatRules.Repair(yellow, 6);
        CombatRules.Repair(red, 6);
        Assert.AreEqual(1 + 6 / 6.0, yellow.Systems["Engines"].HitPoints, 1e-6); // 3 s * 2 = 6 s per point
        Assert.AreEqual(1 + 6 / 12.0, red.Systems["Engines"].HitPoints, 1e-6); // 3 s * 4 = 12 s per point
    }

    [TestMethod]
    public void RepairStopsAtTheMaximum()
    {
        var target = Damaged(4);
        CombatRules.Repair(target, 100);
        Assert.AreEqual(5, target.Systems["Engines"].HitPoints, 1e-6);
    }

    [TestMethod]
    public void ADestroyedSystemWorksAgainOnlyAtFullHitPoints()
    {
        var target = Damaged(0, destroyed: true);
        CombatRules.Repair(target, 14.9);
        Assert.IsTrue(target.Systems["Engines"].Destroyed);
        CombatRules.Repair(target, 0.2);
        Assert.IsFalse(target.Systems["Engines"].Destroyed);
    }

    [TestMethod]
    public void ADerelictUnitRepairsNothing()
    {
        var target = Damaged(1);
        target.Derelict = true;
        CombatRules.Repair(target, 100);
        Assert.AreEqual(1, target.Systems["Engines"].HitPoints, 1e-6);
    }

    static UnitCombatState Suffocating(double crew, float loss = 10)
    {
        var target = State(Profile(systems: new[] { Sys("LifeSupport") }, crew: (float)crew, lifeSupportLoss: loss));
        target.DestroySystem("LifeSupport");

        return target;
    }

    [TestMethod]
    public void DestroyedLifeSupportCostsOnePlusItsLossPercentOfTheCrewPerSecond()
    {
        var target = Suffocating(100);
        CombatRules.Sustain(target, 1);
        Assert.AreEqual(100 - (1 + 10), target.Crew, 1e-6);
    }

    [TestMethod]
    public void WorkingLifeSupportCostsNoCrew()
    {
        var target = Suffocating(100);
        target.RestoreSystem("LifeSupport");
        CombatRules.Sustain(target, 5);
        Assert.AreEqual(100, target.Crew, 1e-6);
    }

    [TestMethod]
    public void LifeSupportLossEndsInADerelictUnitWithNoCrew()
    {
        var target = Suffocating(1.5);
        CombatRules.Sustain(target, 1);
        Assert.AreEqual(0, target.Crew, 1e-6);
        Assert.IsTrue(target.Derelict);
    }

    [TestMethod]
    public void ADerelictUnitLosesNoCrewToLifeSupport()
    {
        var target = Suffocating(0);
        target.Derelict = true;
        CombatRules.Sustain(target, 5);
        Assert.AreEqual(0, target.Crew, 1e-6);
    }

    [TestMethod]
    public void ADestroyedShieldGeneratorLeavesTheShieldsAsTheyAre()
    {
        var target = State(Profile(maxHealth: 100, shieldRate: 10, systems: new[] { Sys("ShieldGenerator") }));
        target.Shields = 50;
        target.DestroySystem("ShieldGenerator");
        CombatRules.RegainShields(target, 5);
        Assert.AreEqual(50, target.Shields, 1e-6);
    }

    [TestMethod]
    public void ADerelictUnitRegainsNoShields()
    {
        var target = Regaining(50, 0);
        target.Derelict = true;
        CombatRules.RegainShields(target, 5);
        Assert.AreEqual(50, target.Shields, 1e-6);
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

    static TargetValue Overridden(float value, float forTarget) =>
        new()
        {
            Default = value,
            Overrides = new Dictionary<string, float> { ["t.odf"] = forTarget },
        };

    [TestMethod]
    public void TheDamageThresholdTakesTheOverrideOfTheTargetsDefinitionName()
    {
        var target = Target(); // definition name t.odf
        var ordnance = Beam(shieldCrew: 0.5f) with { DamageThreshold = Overridden(99, 4) };
        CombatRules.Apply(target, ordnance, 10, HitLocation.Critical, 1);
        Assert.AreEqual(100 - (1 + 100 * 0.5 * 6 / 100), target.Crew, 1e-6);
    }

    [TestMethod]
    public void TheShieldCrewModifierTakesTheOverrideOfTheTargetsDefinitionName()
    {
        var target = Target();
        var ordnance = Beam(threshold: 4) with { ShieldCrewModifier = Overridden(0, 0.5f) };
        CombatRules.Apply(target, ordnance, 10, HitLocation.Critical, 1);
        Assert.AreEqual(100 - (1 + 100 * 0.5 * 6 / 100), target.Crew, 1e-6);
    }

    [TestMethod]
    public void TheHullCrewModifierTakesTheOverrideOfTheTargetsDefinitionName()
    {
        var target = Target(shields: 0);
        var ordnance = Beam() with { HullCrewModifier = Overridden(0, 10) };
        var hit = CombatRules.Apply(target, ordnance, 3, HitLocation.Hull, 1);
        Assert.AreEqual(1 + 100 * 10 * 3 / 100.0, hit.CrewLoss, 1e-6);
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
        var target = WithSystems(Sys("Engines", 9));
        var hit = CombatRules.Apply(target, Beam(threshold: 99), 10, HitLocation.Engines, 0.6);
        Assert.AreEqual(6, hit.ShieldLoss, 1e-6);
        Assert.AreEqual(94, target.Shields, 1e-6);
        Assert.AreEqual(4, hit.Points);
        Assert.AreEqual(5, target.Systems["Engines"].HitPoints, 1e-6);
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
    public void ACriticalHitOfOneWholePointDoomsTheUnitButDoesNotRemoveIt()
    {
        var target = Target(shields: 0);
        var hit = CombatRules.Apply(target, Beam(), 1, HitLocation.Critical, 1);
        Assert.IsTrue(hit.Destroyed);
        Assert.IsTrue(target.Dying);
        Assert.IsFalse(target.Destroyed);
    }

    [TestMethod]
    public void ACriticalHitDoomsAUnitForThreeSecondsOrFiveWhenFastOrHero()
    {
        foreach (
            var (speed, hero, seconds) in new[]
            {
                (0.0, false, 3.0),
                (1.0, false, 3.0),
                (1.5, false, 5.0),
                (0.0, true, 5.0),
            }
        )
        {
            var target = State(Profile() with { IsHero = hero });
            target.Shields = 0;
            target.Velocity = new System.Numerics.Vector3(0, 0, (float)speed);
            CombatRules.Apply(target, Beam(), 1, HitLocation.Critical, 1);
            Assert.AreEqual(seconds, target.DyingRemaining, 1e-9, $"speed {speed}, hero {hero}");
        }
    }

    [TestMethod]
    public void AFurtherHitOnADyingUnitCutsHalfASecondOnlyWithDamageAndDealsNone()
    {
        var target = State(Profile());
        target.Shields = 0;
        CombatRules.Apply(target, Beam(), 1, HitLocation.Critical, 1);
        var crew = target.Crew;
        var none = CombatRules.Apply(target, Beam(), 0, HitLocation.Hull, 1);
        Assert.AreEqual(3, target.DyingRemaining, 1e-9);
        Assert.AreEqual(0, none.Damage);
        var some = CombatRules.Apply(target, Beam(hullCrew: 50), 5, HitLocation.Hull, 1);
        Assert.AreEqual(2.5, target.DyingRemaining, 1e-9);
        Assert.AreEqual(0, some.Damage);
        Assert.AreEqual(0, some.Points);
        Assert.AreEqual(crew, target.Crew);
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
