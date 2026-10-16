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
    public static double Damage(OrdnanceProfile ordnance, string target, double random) =>
        ordnance.DamageBase.For(target) + ordnance.DamageVariance.For(target) * (2 * random - 1);

    static readonly string[] SystemOrder = { "Engines", "LifeSupport", "Weapons", "ShieldGenerator", "Sensors" };

    // The location a roll of 0 to 99 hits: the roll is reduced by the hit percent of engines, life support,
    // weapons, shield generator, sensors, and hull in turn; the location that takes it below zero is hit, and a
    // roll that no location takes is a critical hit. A system the unit lacks has no percent.
    public static HitLocation Location(CombatProfile profile, int roll)
    {
        var left = (double)roll;
        for (var i = 0; i < SystemOrder.Length; i++)
        {
            left -= profile.Systems.FirstOrDefault(s => s.Name == SystemOrder[i])?.HitPercent ?? 0;
            if (left < 0)
                return (HitLocation)i;
        }
        return left - profile.HullHitPercent < 0 ? HitLocation.Hull : HitLocation.Critical;
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
                    target.SystemDamage[location.ToString()] =
                        target.SystemDamage.GetValueOrDefault(location.ToString()) + points;
                    break;
            }
        // simplicity: crew below one does nothing yet; a later slice makes the unit derelict
        target.Crew = Math.Max(0, target.Crew - crewLoss);
        target.Destroyed |= destroyed;
        return new Hit(location, damage, shieldLoss, crewLoss, points, destroyed);
    }

    // Regains shield points for `seconds`: nothing while the shields are down after reaching zero, then the
    // rate per second up to the maximum.
    public static void RegainShields(UnitCombatState unit, double seconds)
    {
        var down = Math.Min(unit.ShieldDown, seconds);
        unit.ShieldDown -= down;
        unit.Shields = Math.Min(unit.Profile.MaxHealth, unit.Shields + unit.Profile.ShieldRate * (seconds - down));
    }
}
