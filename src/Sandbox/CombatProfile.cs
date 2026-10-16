using System.Collections.Generic;
using OpenArmada.Assets;

namespace OpenArmada.Sandbox;

// What one shot is: the ordnance definition a weapon names. ClassLabel is the definition's own label
// (`phaser`, `photontorpedo`, ...) and tells how the shot travels.
public sealed record OrdnanceProfile(
    string ClassLabel,
    TargetValue DamageBase,
    TargetValue DamageVariance,
    TargetValue DamageThreshold,
    TargetValue ShieldCrewModifier,
    TargetValue HullCrewModifier,
    float LifeSpan,
    float ShotSpeed,
    float OmegaTurn,
    float SeekTime,
    string Sprite
);

// One weapon slot: a beam weapon (`cannon`) or a torpedo launcher (`launcher`) with the hardpoints it
// fires from. The to-hit modifiers are added to HitChance as the combat rules say.
public sealed record WeaponProfile(
    int Slot,
    string Name,
    IReadOnlyList<string> Hardpoints,
    float ShotDelay,
    float Range,
    TargetValue HitChance,
    float WarpToHitModifier,
    float ToHitWarpModifier,
    float StopToHitModifier,
    float ToHitStopModifier,
    bool CrewAffectsWeaponDelay,
    string FireSound,
    OrdnanceProfile Ordnance
);

// One of the five ship systems that a unit has. Absent from a profile when its definition gives it no hit points.
public sealed record SystemProfile(string Name, float HitPoints, float HitPercent, float CrewLoss, float RepairTime);

// The race values the combat rules read: the crew-ratio thresholds, their weapon-delay and repair factors,
// and the repair strength.
public sealed record RaceValues(
    float CrewYellowStatus,
    float CrewRedStatus,
    float WeaponYellow,
    float WeaponRed,
    float RepairYellow,
    float RepairRed,
    float RepairStrength
);

// Everything the combat rules read from a unit's definitions. Every number is finite and MaximumCrew is
// above zero, whatever the install lacks.
public sealed record CombatProfile(
    IReadOnlyList<WeaponProfile> Weapons,
    float MaxHealth,
    float ShieldRate,
    float ShieldDelay,
    float CrewCost,
    float MaximumCrew,
    IReadOnlyList<SystemProfile> Systems,
    float HullHitPercent,
    float LifeSupportLoss,
    bool DefaultGreenAlert,
    bool CanAttack,
    string Fireball,
    RaceValues Race
);
