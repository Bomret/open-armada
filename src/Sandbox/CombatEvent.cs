using System.Numerics;

namespace OpenArmada.Sandbox;

/// <summary>
/// Where a hit lands: one of the five ship systems, the hull, or no part that takes damage (a critical hit).
/// </summary>
public enum HitLocation
{
    /// <summary>A hit on the engines.</summary>
    Engines,

    /// <summary>A hit on life support.</summary>
    LifeSupport,

    /// <summary>A hit on the weapons system.</summary>
    Weapons,

    /// <summary>A hit on the shield generator.</summary>
    ShieldGenerator,

    /// <summary>A hit on the sensors.</summary>
    Sensors,

    /// <summary>A hit on the hull, which costs crew.</summary>
    Hull,

    /// <summary>A critical hit: no location took the roll. One whole point or more destroys the unit.</summary>
    Critical,
}

/// <summary>
/// The names of the five ship systems: SystemProfile.Name and the keys of UnitCombatState.Systems. Each is the name
/// of the system's hit location, so a hit location's name is the system it damages.
/// </summary>
public static class SystemNames
{
    /// <summary>The name of the engines, whose definition keys start with <c>engines</c>.</summary>
    public const string Engines = nameof(HitLocation.Engines);

    /// <summary>The name of life support, whose definition keys start with <c>lifeSupport</c>.</summary>
    public const string LifeSupport = nameof(HitLocation.LifeSupport);

    /// <summary>The name of the weapons system, whose definition keys start with <c>weapons</c>.</summary>
    public const string Weapons = nameof(HitLocation.Weapons);

    /// <summary>The name of the shield generator, whose definition keys start with <c>shieldGenerator</c>.</summary>
    public const string ShieldGenerator = nameof(HitLocation.ShieldGenerator);

    /// <summary>The name of the sensors, whose definition keys start with <c>sensors</c>.</summary>
    public const string Sensors = nameof(HitLocation.Sensors);

    /// <summary>The five names in the order of <see cref="HitLocation"/>, so the index of a system is its location.</summary>
    public static readonly string[] All = [Engines, LifeSupport, Weapons, ShieldGenerator, Sensors];
}

/// <summary>
/// The outcome of one hit on a unit: where it landed, the damage rolled, the shield points lost, the crew lost,
/// the whole points of the systems part that passed on, and whether it destroyed the unit.
/// </summary>
public sealed record Hit
{
    /// <summary>Where the hit landed.</summary>
    public required HitLocation Location { get; init; }

    /// <summary>The damage rolled for the shot, before the shield part is split off; zero or more.</summary>
    public required double Damage { get; init; }

    /// <summary>
    /// The shield points lost: the shield part, at most the shields the unit had; zero when the shields were at zero.
    /// </summary>
    public required double ShieldLoss { get; init; }

    /// <summary>
    /// The crew lost to the shield excess, a hull hit, and a destroyed system. The value is not clamped, so it can
    /// exceed the crew the unit had.
    /// </summary>
    public required double CrewLoss { get; init; }

    /// <summary>
    /// The whole points of the systems part that passed on in this hit. The fraction stays in the unit's running total
    /// for later hits, so a hit can pass on zero points and leave its location untouched.
    /// </summary>
    public required int Points { get; init; }

    /// <summary>Whether the hit destroyed the unit: a critical hit of at least one whole point.</summary>
    public required bool Destroyed { get; init; }
}

/// <summary>How a shot travels, decided by its ordnance's <c>classLabel</c>.</summary>
public enum ShotKind
{
    /// <summary>
    /// <c>phaser</c>: runs from the hardpoint to the target and deals its damage once, when <c>lifeSpan</c> ends.
    /// </summary>
    Beam,

    /// <summary>
    /// <c>photontorpedo</c>, <c>plasmacannon</c>, and every other class label, <c>polerontorpedo</c> included: flies at
    /// <c>shotSpeed</c>, steers toward its target while its age is within <c>seekTime</c>, and hits on contact.
    /// </summary>
    Torpedo,

    /// <summary><c>pphaser</c>: flies straight at <c>shotSpeed</c> and hits on contact.</summary>
    Pulse,
}

/// <summary>
/// One shot in flight. A beam keeps the chosen hardpoint's position at fire time as its Position; a torpedo and a
/// pulse fly from Position along Direction. Hits is the to-hit roll made at fire time: a shot that missed is reported and then
/// dropped.
/// </summary>
public sealed class Shot(
    ShotKind kind,
    UnitCombatState shooter,
    UnitCombatState target,
    WeaponProfile weapon,
    bool hits,
    Vector3 position,
    Vector3 direction,
    string hardpoint
)
{
    /// <summary>The name of the hardpoint node the world chose to fire this shot from.</summary>
    public string Hardpoint { get; } = hardpoint;

    /// <summary>How the shot travels.</summary>
    public ShotKind Kind { get; } = kind;

    /// <summary>The unit that fired the shot.</summary>
    public UnitCombatState Shooter { get; } = shooter;

    /// <summary>The unit the shot is fired at. The world drops the shot when this unit is destroyed.</summary>
    public UnitCombatState Target { get; } = target;

    /// <summary>The weapon that fired the shot. Its ordnance gives the damage and the flight values.</summary>
    public WeaponProfile Weapon { get; } = weapon;

    /// <summary>
    /// Whether the to-hit roll at fire time hit. The world keeps only a shot that hits; a missed shot appears only in
    /// its <see cref="ShotFired"/> event.
    /// </summary>
    public bool Hits { get; } = hits;

    /// <summary>
    /// Where a torpedo or a pulse is, in world units. It starts at the world position of the chosen
    /// <see cref="Hardpoint"/>; a beam keeps its start position.
    /// </summary>
    public Vector3 Position { get; set; } = position;

    /// <summary>
    /// The unit vector a torpedo or a pulse flies along. It starts toward the target's center (+Z when the centers
    /// coincide). A torpedo turns toward its target by up to <c>omegaTurn</c> radians per second while its age is
    /// within <c>seekTime</c>.
    /// </summary>
    public Vector3 Direction { get; set; } = direction;

    /// <summary>
    /// The seconds since the shot was fired. The world advances it by <see cref="CombatWorld.Tick"/> per step, and only
    /// for a shot it keeps; the shot ends at its ordnance's <c>lifeSpan</c>.
    /// </summary>
    public double Age { get; set; }
}

/// <summary>What happened in one world step.</summary>
public abstract record CombatEvent;

/// <summary>A weapon fired a shot, hit or miss: one event per round, from one hardpoint.</summary>
public sealed record ShotFired : CombatEvent
{
    /// <summary>The shot fired. <see cref="Shot.Hits"/> tells whether it can land.</summary>
    public required Shot Shot { get; init; }
}

/// <summary>A shot reached its target, and its hit was applied to the target.</summary>
public sealed record ShotLanded : CombatEvent
{
    /// <summary>The shot that landed.</summary>
    public required Shot Shot { get; init; }

    /// <summary>The outcome of the hit on the target.</summary>
    public required Hit Hit { get; init; }
}

/// <summary>
/// A hit destroyed a unit. The event follows that hit's <see cref="ShotLanded"/>; the world removes the unit and
/// every shot at it.
/// </summary>
public sealed record UnitDestroyed : CombatEvent
{
    /// <summary>The map object of the destroyed unit.</summary>
    public required PlacedObject Unit { get; init; }
}
