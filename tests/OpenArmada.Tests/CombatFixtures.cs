using System.Collections.Generic;
using System.Numerics;
using OpenArmada.Assets;
using OpenArmada.Sandbox;

namespace OpenArmada.Tests;

// Combat values built in code (INV-0001), shared by the combat rule and world tests.
static class CombatFixtures
{
    public static readonly RaceValues Race = new(0.5f, 0.25f, 2, 3, 1, 1, 1);

    public static OrdnanceProfile Beam(
        float damage = 10,
        float variance = 0,
        float threshold = 0,
        float shieldCrew = 0,
        float hullCrew = 0,
        float life = 1,
        string label = "phaser",
        float speed = 100,
        float omega = 1,
        float seek = 5
    ) =>
        new(
            label,
            TargetValue.Of(damage),
            TargetValue.Of(variance),
            TargetValue.Of(threshold),
            TargetValue.Of(shieldCrew),
            TargetValue.Of(hullCrew),
            life,
            speed,
            omega,
            seek,
            ""
        );

    public static WeaponProfile Weapon(
        OrdnanceProfile? ordnance = null,
        float delay = 1,
        float range = 100,
        float hitChance = 1,
        bool crewAffects = false,
        int hardpoints = 1,
        float stopModifier = 0,
        float toHitStopModifier = 0
    ) =>
        new(
            1,
            "w",
            new string[hardpoints],
            delay,
            range,
            TargetValue.Of(hitChance),
            0.1f,
            0.2f,
            stopModifier,
            toHitStopModifier,
            crewAffects,
            "",
            ordnance ?? Beam()
        );

    public static CombatProfile Profile(
        IReadOnlyList<WeaponProfile>? weapons = null,
        float maxHealth = 100,
        float shieldRate = 0,
        float shieldDelay = 0,
        float crew = 100,
        float maximumCrew = 100,
        float hull = 100,
        IReadOnlyList<SystemProfile>? systems = null,
        float lifeSupportLoss = 0,
        RaceValues? race = null,
        bool defaultGreen = false,
        bool canAttack = true
    ) =>
        new(
            weapons ?? new List<WeaponProfile>(),
            maxHealth,
            shieldRate,
            shieldDelay,
            crew,
            maximumCrew,
            systems ?? new List<SystemProfile>(),
            hull,
            lifeSupportLoss,
            defaultGreen,
            canAttack,
            "",
            race ?? Race
        );

    // A ship system: hit points, hit percent, crew loss percent, repair time (seconds for the whole system).
    public static SystemProfile Sys(
        string name,
        float hitPoints = 5,
        float percent = 10,
        float crewLoss = 0,
        float repairTime = 10
    ) => new(name, hitPoints, percent, crewLoss, repairTime);

    public static PlacedObject Placed(string label, int team, float x, float z = 0) =>
        new(label, "c", "m", team, 0, new Vector3(x, 0, z), Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ);

    public static UnitCombatState State(CombatProfile profile, string name = "t.odf", int team = 1) =>
        new(Placed(name, team, 0), profile, name, 0, true);
}
