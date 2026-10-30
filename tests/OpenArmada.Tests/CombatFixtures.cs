using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using OpenArmada.Assets;
using OpenArmada.Sandbox;

namespace OpenArmada.Tests;

// Combat values built in code (INV-0001), shared by the combat rule and world tests.
static class CombatFixtures
{
    public static readonly RaceValues Race = new()
    {
        CrewYellowStatus = 0.5f,
        CrewRedStatus = 0.25f,
        WeaponYellow = 2,
        WeaponRed = 3,
        RepairYellow = 1,
        RepairRed = 1,
        RepairStrength = 1,
    };

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
        float seek = 5,
        bool ignoreShield = false,
        string shieldGeometry = OrdnanceProfile.DefaultShieldGeometry,
        float shieldDuration = OrdnanceProfile.DefaultShieldDuration,
        float size = 1
    ) =>
        new()
        {
            IgnoreShield = ignoreShield,
            ShieldGeometryName = shieldGeometry,
            ShieldDuration = shieldDuration,
            Size = size,
            ClassLabel = label,
            DamageBase = TargetValue.Of(damage),
            DamageVariance = TargetValue.Of(variance),
            DamageThreshold = TargetValue.Of(threshold),
            ShieldCrewModifier = TargetValue.Of(shieldCrew),
            HullCrewModifier = TargetValue.Of(hullCrew),
            LifeSpan = life,
            ShotSpeed = speed,
            OmegaTurn = omega,
            SeekTime = seek,
            Sprite = "",
        };

    public static WeaponProfile Weapon(
        OrdnanceProfile? ordnance = null,
        float delay = 1,
        float range = 100,
        float hitChance = 1,
        bool crewAffects = false,
        int hardpoints = 1,
        float stopModifier = 0,
        float toHitStopModifier = 0,
        bool special = false,
        bool targetLocation = false
    ) =>
        new()
        {
            Special = special,
            TargetLocation = targetLocation,
            Slot = 1,
            Name = "w",
            Hardpoints = Enumerable.Range(1, hardpoints).Select(i => $"hp{i}").ToArray(),
            ShotDelay = delay,
            Range = range,
            HitChance = TargetValue.Of(hitChance),
            WarpToHitModifier = 0.1f,
            ToHitWarpModifier = 0.2f,
            StopToHitModifier = stopModifier,
            ToHitStopModifier = toHitStopModifier,
            CrewAffectsWeaponDelay = crewAffects,
            FireSound = "",
            Ordnance = ordnance ?? Beam(),
        };

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
        new()
        {
            Weapons = weapons ?? new List<WeaponProfile>(),
            MaxHealth = maxHealth,
            ShieldRate = shieldRate,
            ShieldDelay = shieldDelay,
            CrewCost = crew,
            MaximumCrew = maximumCrew,
            DefinedMaximumCrew = maximumCrew,
            Systems = systems ?? new List<SystemProfile>(),
            HullHitPercent = hull,
            LifeSupportLoss = lifeSupportLoss,
            DefaultGreenAlert = defaultGreen,
            CanAttack = canAttack,
            Fireball = "",
            Race = race ?? Race,
        };

    // A ship system: hit points, hit percent, crew loss percent, repair time (seconds for the whole system), spark time,
    // and target hardpoints.
    public static SystemProfile Sys(
        string name,
        float hitPoints = 5,
        float percent = 10,
        float crewLoss = 0,
        float repairTime = 10,
        float sparkTime = 0.5f,
        params string[] targetHardpoints
    ) =>
        new()
        {
            Name = name,
            HitPoints = hitPoints,
            HitPercent = percent,
            CrewLoss = crewLoss,
            RepairTime = repairTime,
            SparkTime = sparkTime,
            TargetHardpoints = targetHardpoints,
        };

    public static PlacedObject Placed(string label, int team, float x, float z = 0) =>
        new()
        {
            Label = label,
            Class = "c",
            Model = "m",
            Team = team,
            Slot = 0,
            Position = new Vector3(x, 0, z),
            AxisX = Vector3.UnitX,
            AxisY = Vector3.UnitY,
            AxisZ = Vector3.UnitZ,
        };

    public static UnitCombatState State(CombatProfile profile, string name = "t.odf", int team = 1) =>
        new(Placed(name, team, 0), profile, name, 0, true);
}
