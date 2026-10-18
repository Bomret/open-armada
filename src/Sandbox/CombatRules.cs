using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenArmada.Sandbox;

/// <summary>
/// The pure combat rules of docs/combat-rules.md (ADR 0004): hostility, to-hit, reload, damage, hit location, the
/// application of a hit, and the shield regain. Randomness comes in as arguments; nothing here reads a clock.
/// </summary>
public static class CombatRules
{
    /// <summary>Units of different teams are hostile; team 0 is neutral and hostile to no one.</summary>
    public static bool Hostile(int a, int b) => a != b && a != 0 && b != 0;

    /// <summary>
    /// The chance that a shot at <c>target</c> hits: hitChance plus the modifiers that apply. The slow modifiers are
    /// not used.
    /// </summary>
    public static double ToHitChance(
        WeaponProfile weapon,
        string target,
        bool shooterWarp,
        bool targetWarp,
        bool shooterStopped,
        bool targetStopped
    ) =>
        weapon.HitChance.For(target)
        + (shooterWarp ? weapon.WarpToHitModifier : 0)
        + (targetWarp ? weapon.ToHitWarpModifier : 0)
        + (shooterStopped ? weapon.StopToHitModifier : 0)
        + (targetStopped ? weapon.ToHitStopModifier : 0);

    /// <summary>
    /// The seconds until the weapon fires again: shotDelay, times the race's crew factor when the weapon sets
    /// crewAffectsWeaponDelay.
    /// </summary>
    public static double ReloadDelay(WeaponProfile weapon, UnitCombatState shooter)
    {
        if (!weapon.CrewAffectsWeaponDelay)
            return weapon.ShotDelay;
        var race = shooter.Profile.Race;

        return weapon.ShotDelay * CrewFactor(shooter, race.WeaponYellow, race.WeaponRed);
    }

    /// <summary>
    /// The unit's crew status by its crew ratio (current crew over maximumCrew): red at or below the race's
    /// crewRedStatus, else yellow at or below its crewYellowStatus, else green.
    /// </summary>
    public static CrewStatus CrewStatusOf(UnitCombatState unit)
    {
        var race = unit.Profile.Race;
        var ratio = unit.Crew / unit.Profile.MaximumCrew;

        return ratio <= race.CrewRedStatus ? CrewStatus.Red
            : ratio <= race.CrewYellowStatus ? CrewStatus.Yellow
            : CrewStatus.Green;
    }

    // The race factor for the unit's crew status: `red`, `yellow`, or 1 for green.
    static float CrewFactor(UnitCombatState unit, float yellow, float red) =>
        CrewStatusOf(unit) switch
        {
            CrewStatus.Red => red,
            CrewStatus.Yellow => yellow,
            _ => 1,
        };

    /// <summary>
    /// The damage of a landing shot: damageBase plus up to damageVariance either way, for a <c>random</c> in [0, 1].
    /// stand-in: the random part is uniform; the game's distribution is not known.
    /// stand-in: a roll below zero (a variance above the base) deals none; the game's handling is not known.
    /// </summary>
    public static double Damage(OrdnanceProfile ordnance, string target, double random) =>
        Math.Max(0, ordnance.DamageBase.For(target) + ordnance.DamageVariance.For(target) * (2 * random - 1));

    // In the order of HitLocation, so the index of a system is its location.
    static readonly string[] SystemOrder = SystemNames.All;

    /// <summary>
    /// The location a roll of 0 to 99 hits: the roll is reduced by the hit percent of engines, life support,
    /// weapons, shield generator, sensors, and hull in turn; the location that takes it below zero is hit, and a
    /// roll that no location takes is a critical hit. A system the unit lacks has no percent. The roll reads each
    /// system's live percent (<see cref="ShipSystemState.HitPercent"/>), never its working flag; see
    /// <see cref="UnitCombatState.DestroySystem"/> for where a destroyed system's percent goes.
    /// </summary>
    public static HitLocation Location(UnitCombatState target, int roll)
    {
        var left = (double)roll;
        for (var i = 0; i < SystemOrder.Length; i++)
        {
            if (target.Systems.TryGetValue(SystemOrder[i], out var s))
            {
                left -= s.HitPercent;
            }

            if (left < 0)
                return (HitLocation)i;
        }

        return left - target.Profile.HullHitPercent < 0 ? HitLocation.Hull : HitLocation.Critical;
    }

    /// <summary>
    /// Repairs the unit's damaged systems for <c>seconds</c> (the rules' "Repair"). A derelict unit repairs nothing.
    /// stand-in: the officer factor is 1.
    /// </summary>
    public static void Repair(UnitCombatState unit, double seconds)
    {
        if (unit.Derelict)
            return;
        var race = unit.Profile.Race;
        var factor = CrewFactor(unit, race.RepairYellow, race.RepairRed);
        foreach (var s in unit.Systems.Values.Where(s => s.HitPoints < s.Max))
        {
            // seconds one hit point takes
            var perPoint = s.Profile.RepairTime / s.Max * race.RepairStrength * factor;
            // stand-in: a zero repair time (or race factor) is read as "this system does not repair"; the game's
            // reading is not known.
            if (perPoint <= 0)
                continue;
            s.HitPoints = Math.Min(s.Max, s.HitPoints + seconds / perPoint);
            if (s.HitPoints >= s.Max && s.Destroyed)
            {
                unit.RestoreSystem(s.Profile.Name);
            }
        }
    }

    /// <summary>
    /// Life support loss for <c>seconds</c>: the crew of a unit with destroyed life support falls by 1 plus
    /// lifeSupportLoss percent of the crew per second. A derelict unit loses none.
    /// </summary>
    public static void Sustain(UnitCombatState unit, double seconds)
    {
        if (unit.Derelict || !unit.SystemDestroyed(SystemNames.LifeSupport))
            return;
        unit.Crew -= (1 + unit.Crew * unit.Profile.LifeSupportLoss / 100) * seconds;
        CheckCrew(unit);
    }

    // The crew below one is set to zero and the unit becomes derelict, its shields down.
    static void CheckCrew(UnitCombatState unit)
    {
        if (unit.Crew >= 1)
            return;
        unit.Crew = 0;
        // stand-in: the game can destroy the unit instead, by a flag whose source is unknown (docs/combat-rules.md).
        unit.Derelict = true;
        unit.Shields = 0;
    }

    /// <summary>
    /// Applies a landed hit of <c>damage</c> at <c>location</c> to the unit (the rules' "Applying a hit"). The crew loss
    /// percents are read as percent, so a modifier times the crew times the points is divided by 100.
    /// </summary>
    public static Hit Apply(
        UnitCombatState target,
        OrdnanceProfile ordnance,
        double damage,
        HitLocation location,
        double shieldDamageProtection
    )
    {
        var name = target.DefinitionName;
        var shieldPart = damage * shieldDamageProtection;
        var systemsPart = damage - shieldPart;
        double crewLoss = 0,
            shieldLoss = 0;
        if (target.Shields > 0)
        {
            var excess = shieldPart - ordnance.DamageThreshold.For(name);
            if (excess > 0)
            {
                crewLoss += Math.Min(excess, 1) + target.Crew * ordnance.ShieldCrewModifier.For(name) * excess / 100;
            }

            shieldLoss = Math.Min(shieldPart, target.Shields);
            target.Shields -= shieldPart;
            if (target.Shields <= 0)
            {
                systemsPart -= target.Shields; // the damage past zero joins the systems part
                target.Shields = 0;
                target.ShieldDown = target.Profile.ShieldDelay;
            }
        }
        else
        {
            systemsPart = damage;
        }

        target.SystemsRemainder += systemsPart;
        var points = (int)Math.Floor(target.SystemsRemainder);
        target.SystemsRemainder -= points;
        var destroyed = false;
        if (points >= 1)
        {
            switch (location)
            {
                case HitLocation.Hull:
                    crewLoss += Math.Min(points, 1) + target.Crew * ordnance.HullCrewModifier.For(name) * points / 100;

                    break;
                case HitLocation.Critical:
                    destroyed = true;

                    break;
                default:
                    if (target.Systems.TryGetValue(SystemOrder[(int)location], out var system) && !system.Destroyed)
                    {
                        system.HitPoints = Math.Max(0, system.HitPoints - points);
                        if (system.HitPoints <= 0)
                        {
                            target.DestroySystem(SystemOrder[(int)location]);
                            if (!target.Derelict)
                            {
                                crewLoss += 1 + target.Crew * system.Profile.CrewLoss / 100;
                            }
                        }
                    }

                    break;
            }
        }

        target.Crew = Math.Max(0, target.Crew - crewLoss);
        if (crewLoss > 0)
        {
            CheckCrew(target);
        }

        target.Destroyed |= destroyed;

        return new Hit
        {
            Location = location,
            Damage = damage,
            ShieldLoss = shieldLoss,
            CrewLoss = crewLoss,
            Points = points,
            Destroyed = destroyed,
        };
    }

    /// <summary>
    /// Regains shield points for <c>seconds</c>: nothing while the shields are down after reaching zero, then the
    /// rate per second up to the maximum.
    /// </summary>
    public static void RegainShields(UnitCombatState unit, double seconds)
    {
        if (unit.Derelict || unit.SystemDestroyed(SystemNames.ShieldGenerator))
            return;
        var down = Math.Min(unit.ShieldDown, seconds);
        unit.ShieldDown -= down;
        unit.Shields = Math.Min(unit.Profile.MaxHealth, unit.Shields + unit.Profile.ShieldRate * (seconds - down));
    }
}
