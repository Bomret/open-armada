using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenArmada.Sandbox;

// The pure combat rules of docs/combat-rules.md (ADR 0004): hostility, to-hit, reload, damage, hit location, the
// application of a hit, and the shield regain. Randomness comes in as arguments; nothing here reads a clock.
public static class CombatRules
{
    // Units of different teams are hostile; team 0 is neutral and hostile to no one.
    public static bool Hostile(int a, int b) => a != b && a != 0 && b != 0;

    // The chance that a shot at `target` hits: hitChance plus the modifiers that apply. The slow modifiers are
    // not used.
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

    // The seconds until the weapon fires again: shotDelay, times the race's crew factor when the weapon sets
    // crewAffectsWeaponDelay.
    public static double ReloadDelay(WeaponProfile weapon, UnitCombatState shooter)
    {
        if (!weapon.CrewAffectsWeaponDelay)
            return weapon.ShotDelay;
        var race = shooter.Profile.Race;
        var ratio = shooter.Crew / shooter.Profile.MaximumCrew;
        var factor =
            ratio <= race.CrewRedStatus ? race.WeaponRed
            : ratio <= race.CrewYellowStatus ? race.WeaponYellow
            : 1;
        return weapon.ShotDelay * factor;
    }

    // The damage of a landing shot: damageBase plus up to damageVariance either way, for a `random` in [0, 1].
    // stand-in: the random part is uniform; the game's distribution is not known.
    // stand-in: a roll below zero (a variance above the base) deals none; the game's handling is not known.
    public static double Damage(OrdnanceProfile ordnance, string target, double random) =>
        Math.Max(0, ordnance.DamageBase.For(target) + ordnance.DamageVariance.For(target) * (2 * random - 1));

    static readonly string[] SystemOrder = { "Engines", "LifeSupport", "Weapons", "ShieldGenerator", "Sensors" };

    // The location a roll of 0 to 99 hits: the roll is reduced by the hit percent of engines, life support,
    // weapons, shield generator, sensors, and hull in turn; the location that takes it below zero is hit, and a
    // roll that no location takes is a critical hit. A system the unit lacks has no percent; a destroyed system's
    // percent is shared evenly among the systems still working, and goes to the critical share (the part no
    // location takes) when none works.
    public static HitLocation Location(UnitCombatState target, int roll)
    {
        var present = SystemOrder.Where(target.Systems.ContainsKey).ToList();
        var working = present.Count(n => !target.Systems[n].Destroyed);
        var share =
            working == 0
                ? 0
                : present.Where(target.SystemDestroyed).Sum(n => target.Systems[n].Profile.HitPercent) / working;
        var left = (double)roll;
        for (var i = 0; i < SystemOrder.Length; i++)
        {
            if (target.Systems.TryGetValue(SystemOrder[i], out var s) && !s.Destroyed)
                left -= s.Profile.HitPercent + share;
            if (left < 0)
                return (HitLocation)i;
        }
        return left - target.Profile.HullHitPercent < 0 ? HitLocation.Hull : HitLocation.Critical;
    }

    // Repairs the unit's damaged systems for `seconds` (the rules' "Repair"). A derelict unit repairs nothing.
    // stand-in: the officer factor is 1.
    public static void Repair(UnitCombatState unit, double seconds)
    {
        if (unit.Derelict)
            return;
        var race = unit.Profile.Race;
        var ratio = unit.Crew / unit.Profile.MaximumCrew;
        var factor =
            ratio <= race.CrewRedStatus ? race.RepairRed
            : ratio <= race.CrewYellowStatus ? race.RepairYellow
            : 1;
        foreach (var s in unit.Systems.Values.Where(s => s.HitPoints < s.Max))
        {
            // seconds one hit point takes
            var perPoint = s.Profile.RepairTime / s.Max * race.RepairStrength * factor;
            // stand-in: a zero repair time (or race factor) is read as "this system does not repair"; the game's
            // reading is not known.
            if (perPoint <= 0)
                continue;
            s.HitPoints = Math.Min(s.Max, s.HitPoints + seconds / perPoint);
            if (s.HitPoints >= s.Max)
                s.Destroyed = false;
        }
    }

    // Life support loss for `seconds`: the crew of a unit with destroyed life support falls by 1 plus
    // lifeSupportLoss percent of the crew per second. A derelict unit loses none.
    public static void Sustain(UnitCombatState unit, double seconds)
    {
        if (unit.Derelict || !unit.SystemDestroyed("LifeSupport"))
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

    // Applies a landed hit of `damage` at `location` to the unit (the rules' "Applying a hit"). The crew loss
    // percents are read as percent, so a modifier times the crew times the points is divided by 100.
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
                crewLoss += Math.Min(excess, 1) + target.Crew * ordnance.ShieldCrewModifier.For(name) * excess / 100;
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
            systemsPart = damage;

        target.SystemsRemainder += systemsPart;
        var points = (int)Math.Floor(target.SystemsRemainder);
        target.SystemsRemainder -= points;
        var destroyed = false;
        if (points >= 1)
            switch (location)
            {
                case HitLocation.Hull:
                    crewLoss += Math.Min(points, 1) + target.Crew * ordnance.HullCrewModifier.For(name) * points / 100;
                    break;
                case HitLocation.Critical:
                    destroyed = true;
                    break;
                default:
                    if (target.Systems.TryGetValue(location.ToString(), out var system) && !system.Destroyed)
                    {
                        system.HitPoints = Math.Max(0, system.HitPoints - points);
                        if (system.HitPoints <= 0)
                        {
                            system.Destroyed = true;
                            if (!target.Derelict)
                                crewLoss += 1 + target.Crew * system.Profile.CrewLoss / 100;
                        }
                    }
                    break;
            }
        target.Crew = Math.Max(0, target.Crew - crewLoss);
        if (crewLoss > 0)
            CheckCrew(target);
        target.Destroyed |= destroyed;
        return new Hit(location, damage, shieldLoss, crewLoss, points, destroyed);
    }

    // Regains shield points for `seconds`: nothing while the shields are down after reaching zero, then the
    // rate per second up to the maximum.
    public static void RegainShields(UnitCombatState unit, double seconds)
    {
        if (unit.Derelict || unit.SystemDestroyed("ShieldGenerator"))
            return;
        var down = Math.Min(unit.ShieldDown, seconds);
        unit.ShieldDown -= down;
        unit.Shields = Math.Min(unit.Profile.MaxHealth, unit.Shields + unit.Profile.ShieldRate * (seconds - down));
    }
}
