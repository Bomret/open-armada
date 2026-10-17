using System.Collections.Generic;
using OpenArmada.Assets;

namespace OpenArmada.Sandbox;

// What one shot is: the ordnance definition a weapon names. ClassLabel is the definition's own label
// (`phaser`, `photontorpedo`, ...) and tells how the shot travels.
public sealed record OrdnanceProfile
{
    public required string ClassLabel { get; init; }
    public required TargetValue DamageBase { get; init; }
    public required TargetValue DamageVariance { get; init; }
    public required TargetValue DamageThreshold { get; init; }
    public required TargetValue ShieldCrewModifier { get; init; }
    public required TargetValue HullCrewModifier { get; init; }
    public required float LifeSpan { get; init; }
    public required float ShotSpeed { get; init; }
    public required float OmegaTurn { get; init; }
    public required float SeekTime { get; init; }
    public required string Sprite { get; init; }
}

// One weapon slot: a beam weapon (`cannon`) or a torpedo launcher (`launcher`) with the hardpoints it
// fires from. The to-hit modifiers are added to HitChance as the combat rules say.
public sealed record WeaponProfile
{
    public required int Slot { get; init; }
    public required string Name { get; init; }
    public required IReadOnlyList<string> Hardpoints { get; init; }
    public required float ShotDelay { get; init; }
    public required float Range { get; init; }
    public required TargetValue HitChance { get; init; }
    public required float WarpToHitModifier { get; init; }
    public required float ToHitWarpModifier { get; init; }
    public required float StopToHitModifier { get; init; }
    public required float ToHitStopModifier { get; init; }
    public required bool CrewAffectsWeaponDelay { get; init; }
    public required string FireSound { get; init; }
    public required OrdnanceProfile Ordnance { get; init; }
}

// One of the five ship systems that a unit has. Absent from a profile when its definition gives it no hit points.
public sealed record SystemProfile
{
    public required string Name { get; init; }
    public required float HitPoints { get; init; }
    public required float HitPercent { get; init; }
    public required float CrewLoss { get; init; }
    public required float RepairTime { get; init; }
}

// The race values the combat rules read: the crew-ratio thresholds, their weapon-delay and repair factors,
// and the repair strength.
public sealed record RaceValues
{
    public required float CrewYellowStatus { get; init; }
    public required float CrewRedStatus { get; init; }
    public required float WeaponYellow { get; init; }
    public required float WeaponRed { get; init; }
    public required float RepairYellow { get; init; }
    public required float RepairRed { get; init; }
    public required float RepairStrength { get; init; }
}

// Everything the combat rules read from a unit's definitions. Every number is finite and MaximumCrew is
// above zero, whatever the install lacks.
public sealed record CombatProfile
{
    public required IReadOnlyList<WeaponProfile> Weapons { get; init; }
    public required float MaxHealth { get; init; }
    public required float ShieldRate { get; init; }
    public required float ShieldDelay { get; init; }
    public required float CrewCost { get; init; }
    public required float MaximumCrew { get; init; }
    public required IReadOnlyList<SystemProfile> Systems { get; init; }
    public required float HullHitPercent { get; init; }
    public required float LifeSupportLoss { get; init; }
    public required bool DefaultGreenAlert { get; init; }
    public required bool CanAttack { get; init; }
    public required string Fireball { get; init; }
    public required RaceValues Race { get; init; }
}
