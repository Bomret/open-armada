using System.Collections.Generic;
using OpenArmada.Assets;
using OpenArmada.Formats;

namespace OpenArmada.Sandbox;

/// <summary>
/// What one shot is: the ordnance definition a weapon names. ClassLabel is the definition's own label
/// (<c>phaser</c>, <c>photontorpedo</c>, ...) and tells how the shot travels.
/// </summary>
public sealed record OrdnanceProfile
{
    /// <summary>
    /// The ordnance definition's <c>classLabel</c>, which decides the <see cref="ShotKind"/>. Empty when the key is
    /// missing.
    /// </summary>
    public required string ClassLabel { get; init; }

    /// <summary>The <c>damageBase</c>: the damage of a landing shot before the variance.</summary>
    public required TargetValue DamageBase { get; init; }

    /// <summary>The <c>damageVariance</c>: the most the damage roll moves away from the base, either way.</summary>
    public required TargetValue DamageVariance { get; init; }

    /// <summary>The <c>damageThreshold</c>: a shield part above it costs crew while the shields are up.</summary>
    public required TargetValue DamageThreshold { get; init; }

    /// <summary>
    /// The <c>shieldCrewModifier</c>: the percent of the crew lost per point of the shield part above the threshold.
    /// </summary>
    public required TargetValue ShieldCrewModifier { get; init; }

    /// <summary>The <c>hullCrewModifier</c>: the percent of the crew lost per whole point of a hull hit.</summary>
    public required TargetValue HullCrewModifier { get; init; }

    /// <summary>
    /// The <c>lifeSpan</c> in seconds. A beam deals its damage when it ends; a torpedo or a pulse that has not landed
    /// by then expires without damage.
    /// </summary>
    public required float LifeSpan { get; init; }

    /// <summary>The <c>shotSpeed</c> of a torpedo or a pulse, in world units per second. A beam ignores it.</summary>
    public required float ShotSpeed { get; init; }

    /// <summary>
    /// The <c>omegaTurn</c>: the most a torpedo turns toward its target, read as radians per second (stand-in; the
    /// game's unit is not known).
    /// </summary>
    public required float OmegaTurn { get; init; }

    /// <summary>
    /// The <c>seekTime</c> in seconds: a torpedo steers toward its target only while its age is within it.
    /// </summary>
    public required float SeekTime { get; init; }

    /// <summary>
    /// The ordnance definition's <c>Sprite</c>: the sprite-table row the view draws the shot with. Empty when the key
    /// is missing.
    /// </summary>
    public required string Sprite { get; init; }

    /// <summary>The geometry of the shield hit effect when the definition names none (0x516b80).</summary>
    public const string DefaultShieldGeometry = "xshldx01";

    /// <summary>
    /// The models a shield effect can show, in the game's table order (names at 0x5d2a80, loaded at 0x516a70). A
    /// <c>shieldGeometryName</c> picks one by name, ignoring case (0x516b90); the game reads before the table for any
    /// other name.
    /// </summary>
    public static readonly IReadOnlyList<string> ShieldGeometries =
    [
        "xshldx01",
        "xshlddie",
        "wshldadd",
        "wshldsub",
        "wcorbrfr",
        "wshldrmd",
        "wmanhiem",
    ];

    /// <summary>The seconds of the shield hit effect when the definition names none (0x4ea15a).</summary>
    public const float DefaultShieldDuration = 0.8f;

    /// <summary>The <c>ignoreShield</c> flag: a hit of this ordnance raises no shield hit effect (0x4ec22a).</summary>
    public bool IgnoreShield { get; init; }

    /// <summary>The <c>shieldGeometryName</c>: the model of the shield hit effect (0x4ea4e4).</summary>
    public string ShieldGeometryName { get; init; } = DefaultShieldGeometry;

    /// <summary>The <c>shieldDuration</c> in seconds: how long a shield hit effect lasts, a beam's too (0x4ea521, 0x4eb3c3).</summary>
    public float ShieldDuration { get; init; } = DefaultShieldDuration;

    /// <summary>
    /// The ordnance definition's <c>size</c>: a beam's hull hit sprite is drawn at twice this (0x6173dc, 0x6173e0).
    /// stand-in: the key is read from the ordnance definition and is 1 when it is missing; the game reads the field of
    /// the weapon, and which definition holds it is not confirmed.
    /// </summary>
    public float Size { get; init; } = 1;
}

/// <summary>
/// One weapon slot: a beam weapon (<c>cannon</c>) or a torpedo launcher (<c>launcher</c>) with the hardpoints it
/// fires from. The to-hit modifiers are added to HitChance as the combat rules say.
/// </summary>
public sealed record WeaponProfile
{
    /// <summary>
    /// The <c>N</c> of the unit's <c>weaponN</c> key, which also names its <c>weaponHardpointsN</c> key.
    /// </summary>
    public required int Slot { get; init; }

    /// <summary>The weapon definition's name without <c>.odf</c>, as the unit's <c>weaponN</c> key gives it.</summary>
    public required string Name { get; init; }

    /// <summary>
    /// The hardpoint names of the unit's <c>weaponHardpointsN</c> key; never empty. A round fires one shot from the nearest one within the weapon's range of the target's center (the first listed on a tie); the weapon fires nothing while none is in range.
    /// </summary>
    public required IReadOnlyList<string> Hardpoints { get; init; }

    /// <summary>
    /// The <c>shotDelay</c>: the reload delay in seconds, times the crew factor when <see
    /// cref="CrewAffectsWeaponDelay"/> is set.
    /// </summary>
    public required float ShotDelay { get; init; }

    /// <summary>
    /// The <c>range</c> in world units, measured surface to surface. A unit's longest range sets its search radius and
    /// its closing range, and a beam deals no damage when its target is beyond twice this range.
    /// </summary>
    public required float Range { get; init; }

    /// <summary>The <c>hitChance</c>: the to-hit chance from 0 to 1 before the modifiers.</summary>
    public required TargetValue HitChance { get; init; }

    /// <summary>
    /// The <c>warpToHitModifier</c>, added when the firing unit is at warp. No unit in the engine is at warp, so it
    /// does not apply.
    /// </summary>
    public required float WarpToHitModifier { get; init; }

    /// <summary>
    /// The <c>toHitWarpModifier</c>, added when the target is at warp. No unit in the engine is at warp, so it does not
    /// apply.
    /// </summary>
    public required float ToHitWarpModifier { get; init; }

    /// <summary>The <c>stopToHitModifier</c>, added when the firing unit is stopped.</summary>
    public required float StopToHitModifier { get; init; }

    /// <summary>The <c>toHitStopModifier</c>, added when the target is stopped.</summary>
    public required float ToHitStopModifier { get; init; }

    /// <summary>
    /// The <c>crewAffectsWeaponDelay</c> flag: when set, the race's crew factor multiplies <see cref="ShotDelay"/>.
    /// </summary>
    public required bool CrewAffectsWeaponDelay { get; init; }

    /// <summary>The <c>fireSound</c> the view plays when the weapon fires. Empty when the key is missing.</summary>
    public required string FireSound { get; init; }

    /// <summary>The ordnance definition the weapon's <c>ordName</c> names.</summary>
    public required OrdnanceProfile Ordnance { get; init; }

    /// <summary>
    /// The <c>special</c> flag (0x5053a0). The dogfight's range and its choice of movement leave a special weapon
    /// out (0x506e30): its range is not the ship's closing range.
    /// </summary>
    public bool Special { get; init; }

    /// <summary>
    /// The <c>targetLocation</c> flag (0x5053a0): the weapon is aimed at a point, so its ship turns in place toward
    /// the point and never closes by the dogfight (0x42c9d0).
    /// </summary>
    public bool TargetLocation { get; init; }
}

/// <summary>
/// One of the five ship systems that a unit has. Absent from a profile when its definition gives it no hit points.
/// </summary>
public sealed record SystemProfile
{
    /// <summary>The system's name, one of <see cref="SystemNames"/>.</summary>
    public required string Name { get; init; }

    /// <summary>
    /// The definition's <c>&lt;system&gt;HitPoints</c>, above zero. The unit's maximum adds
    /// <c>SYSTEM_HITPOINTS_BONUS</c>.
    /// </summary>
    public required float HitPoints { get; init; }

    /// <summary>
    /// The <c>&lt;system&gt;HitPercent</c>: the system's share of the hit-location roll of 0 to 99, in percent.
    /// </summary>
    public required float HitPercent { get; init; }

    /// <summary>
    /// The <c>&lt;system&gt;CrewLoss</c>: when the system is destroyed, the crew loses 1 plus this percent of the crew.
    /// </summary>
    public required float CrewLoss { get; init; }

    /// <summary>
    /// The <c>&lt;system&gt;RepairTime</c> in seconds: one hit point takes this over the maximum hit points, times the
    /// race factors. A repair time of zero means the system does not repair.
    /// </summary>
    public required float RepairTime { get; init; }

    /// <summary>
    /// The <c>&lt;system&gt;SparkTime</c> in seconds: while the system repairs, it throws a repair spark each time its
    /// spark clock passes this (0x4683d0). 0.5 when the definition does not set it (0x45fbe0).
    /// </summary>
    public float SparkTime { get; init; } = 0.5f;

    /// <summary>The <c>&lt;system&gt;TargetHardpoints</c>: the hardpoints a repair spark of the system shows at (0x460f30).</summary>
    public IReadOnlyList<string> TargetHardpoints { get; init; } = [];
}

/// <summary>
/// The race values the combat rules read: the crew-ratio thresholds, their weapon-delay and repair factors,
/// and the repair strength.
/// </summary>
public sealed record RaceValues
{
    /// <summary>
    /// The <c>crewYellowStatus</c>: at or below this crew ratio (crew over maximum crew), the yellow factors apply.
    /// </summary>
    public required float CrewYellowStatus { get; init; }

    /// <summary>
    /// The <c>crewRedStatus</c>: at or below this crew ratio, the red factors apply instead of the yellow ones.
    /// </summary>
    public required float CrewRedStatus { get; init; }

    /// <summary>The <c>weaponYellow</c>: the reload-delay factor at or below the yellow crew status.</summary>
    public required float WeaponYellow { get; init; }

    /// <summary>The <c>weaponRed</c>: the reload-delay factor at or below the red crew status.</summary>
    public required float WeaponRed { get; init; }

    /// <summary>The <c>repairYellow</c>: the repair-time factor at or below the yellow crew status.</summary>
    public required float RepairYellow { get; init; }

    /// <summary>The <c>repairRed</c>: the repair-time factor at or below the red crew status.</summary>
    public required float RepairRed { get; init; }

    /// <summary>The <c>repairStrength</c>: a factor on every system's repair time per hit point.</summary>
    public required float RepairStrength { get; init; }

    /// <summary>
    /// The <c>boardingStrength</c> (0x4442b0): the weight of the race's boarders in a boarding fight; 1 where the
    /// definition gives none, as the game does.
    /// </summary>
    public float BoardingStrength { get; init; } = 1;
}

/// <summary>
/// Everything the combat rules read from a unit's definitions. Every number is finite and MaximumCrew is
/// above zero, whatever the install lacks.
/// </summary>
public sealed record CombatProfile
{
    /// <summary>
    /// The unit's beam weapons and torpedo launchers. A weapon of another class, or one without hardpoints or a
    /// readable definition, is left out.
    /// </summary>
    public required IReadOnlyList<WeaponProfile> Weapons { get; init; }

    /// <summary>The <c>maxHealth</c>: the shield maximum. A unit has no hull hit points.</summary>
    public required float MaxHealth { get; init; }

    /// <summary>The <c>shieldRate</c>: the shield points regained per second.</summary>
    public required float ShieldRate { get; init; }

    /// <summary>The <c>shieldDelay</c>: the seconds the shields regain nothing after they reach zero.</summary>
    public required float ShieldDelay { get; init; }

    /// <summary>The <c>crewCost</c>: the starting crew. A unit with a crew cost below 1 starts derelict.</summary>
    public required float CrewCost { get; init; }

    /// <summary>
    /// The <c>maximumCrew</c> (the crew cost where missing), the base of the crew ratio. Where that is 0, the crew
    /// cost, at least 1, as a stand-in.
    /// </summary>
    public required float MaximumCrew { get; init; }

    /// <summary>
    /// The game's own maximum crew (0x4601d7): the <c>maximumCrew</c>, or the <c>crewCost</c> where it is missing; 0
    /// where that is 0 (no stand-in). The damage nodes read this one: the game treats a unit whose maximum crew is 0
    /// as having a crew ratio of 1 (0x467803).
    /// </summary>
    public required float DefinedMaximumCrew { get; init; }

    /// <summary>
    /// The ship systems with hit points above zero, in the order engines, life support, weapons, shield generator,
    /// sensors.
    /// </summary>
    public required IReadOnlyList<SystemProfile> Systems { get; init; }

    /// <summary>The <c>hullHitPercent</c>: the hull's share of the hit-location roll, in percent.</summary>
    public required float HullHitPercent { get; init; }

    /// <summary>
    /// The <c>lifeSupportLoss</c>: with life support destroyed, the crew loses 1 plus this percent of the crew per
    /// second.
    /// </summary>
    public required float LifeSupportLoss { get; init; }

    /// <summary>The <c>defaultGreenAlert</c> flag: the unit starts at green alert, else at red.</summary>
    public required bool DefaultGreenAlert { get; init; }

    /// <summary>
    /// The <c>canAttack</c> flag, true when the key is missing. Only such a unit at red alert closes on its pick or
    /// returns to its tether point.
    /// </summary>
    public required bool CanAttack { get; init; }

    /// <summary>
    /// The <c>fireball</c>: the explosion definition, without <c>.odf</c>, the view shows when the unit is destroyed.
    /// </summary>
    public required string Fireball { get; init; }

    /// <summary>
    /// The <c>isHero</c> flag, false when the key is missing: a hero unit's dying phase lasts 5.0 s however slow it
    /// is (0x466090).
    /// </summary>
    public bool IsHero { get; init; }

    /// <summary>
    /// The <c>eventTransport</c> voice line (the 8th event key, 0x5ab750): the sound played when the unit is ordered to
    /// transport; empty when the definition names none (only hero units do).
    /// </summary>
    public string TransportVoice { get; init; } = "";

    /// <summary>
    /// The <c>shieldPad</c>: the model units the longest radius of the shield ellipsoid grows by (0x46a74f, 0x46b8f0);
    /// <see cref="ShieldEllipsoid.DefaultShieldPad"/> when the key is missing.
    /// </summary>
    public float ShieldPad { get; init; } = ShieldEllipsoid.DefaultShieldPad;

    /// <summary>
    /// The values of the race the definition's <c>race</c> names; stand-in values when the race is missing.
    /// </summary>
    public required RaceValues Race { get; init; }
}
