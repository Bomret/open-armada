using System.Numerics;
using OpenArmada.Formats;

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

    /// <summary>A critical hit: no location took the roll. One whole point or more dooms the unit (its dying phase).</summary>
    Critical,
}

/// <summary>
/// The outcome of one hit on a unit: where it landed, the damage rolled, the shield points lost, the crew lost,
/// the whole points of the systems part that passed on, and whether it doomed the unit.
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
    /// The systems part of this hit before the running total: the damage the shields did not take, with the damage
    /// past zero shields; zero for a hit on a dying unit.
    /// </summary>
    public double SystemsPart { get; init; }

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

    /// <summary>
    /// Whether the hit doomed the unit and started its dying phase: a critical hit of at least one whole point. The
    /// unit leaves the world when its dying time runs out (<see cref="UnitDestroyed"/>).
    /// </summary>
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

    /// <summary>Whether a beam has raised its shield hit effect or hull hit sprite; it raises one only.</summary>
    public bool EffectRaised { get; internal set; }

    /// <summary>
    /// A beam's latest shield contact direction (<see cref="ShieldHitEffect.Direction"/>): the world sets it on each
    /// tick the beam's line enters the target's shield ellipsoid, and its shield hit effect turns to it (0x516c40).
    /// Null before the first such tick.
    /// </summary>
    public Vector3? ShieldDirection { get; internal set; }
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
/// A shield hit effect: the shield geometry flashes around the target. A projectile raises it as it lands (after the
/// hit is applied); a beam raises it once a tenth of its lifetime has passed and the effect then follows the beam's
/// contact point while the beam lives (0x516c40). Either ends <see cref="Duration"/> seconds after it was raised
/// (0x516fa0), whether the beam ended before or not.
/// </summary>
public sealed record ShieldHitEffect : CombatEvent
{
    /// <summary>The shot that raised the effect.</summary>
    public required Shot Shot { get; init; }

    /// <summary>The unit the effect sits on.</summary>
    public required PlacedObject Unit { get; init; }

    /// <summary>The shield geometry's model name, without <c>.sod</c>.</summary>
    public required string Geometry { get; init; }

    /// <summary>Seconds the effect lasts: the ordnance's <c>shieldDuration</c> (0x4eb3c3, 0x4ec299, 0x4ed451).</summary>
    public required double Duration { get; init; }

    /// <summary>The world time of the tick that raised the effect; the effect ends at this plus <see cref="Duration"/>.</summary>
    public required double Raised { get; init; }

    /// <summary>
    /// The target's shield fraction (shields over the maximum) when the effect was raised: after the damage for a
    /// projectile, before the beam's damage for a beam. The effect's color is red 1 minus the fraction, green the
    /// fraction, blue 0 (0x5170e0).
    /// </summary>
    public required double Fraction { get; init; }

    /// <summary>The contact point on the shield ellipsoid, in world units, when the effect was raised.</summary>
    public required Vector3 Contact { get; init; }

    /// <summary>
    /// The contact direction when the effect was raised: where the shot's segment entered the unit sphere of the
    /// target's shield test ellipsoid, a unit vector along the model's own SOD axes (0x4ee860). The effect's own +Z
    /// axis points along it (0x55d840).
    /// </summary>
    public required Vector3 Direction { get; init; }

    /// <summary>Whether a beam raised it, so the effect follows the beam's contact point while the beam lives.</summary>
    public bool FollowsBeam => Shot.Kind == ShotKind.Beam;
}

/// <summary>
/// A beam on a target whose shields are down (or whose ordnance ignores shields) shows a hull hit sprite at the
/// target instead of a shield hit effect, from a tenth of the beam's lifetime until the beam ends.
/// </summary>
public sealed record HullHitSprite : CombatEvent
{
    /// <summary>The beam that raised it.</summary>
    public required Shot Shot { get; init; }

    /// <summary>The unit the sprite sits on.</summary>
    public required PlacedObject Unit { get; init; }

    /// <summary>The sprite's name in the sprite tables.</summary>
    public const string SpriteName = "explosion1";

    /// <summary>The sprite's scale, 2.0 times the ordnance's <see cref="OrdnanceProfile.Size"/>.</summary>
    public required float Scale { get; init; }
}

/// <summary>What ended a unit.</summary>
public enum DestroyCause
{
    /// <summary>Its dying time ran out after a hit doomed it: its fireball plays (0x4663d0).</summary>
    Fireball,

    /// <summary>A black hole swallowed it: no fireball, no sound, no small explosions (0x450a40).</summary>
    BlackHole,
}

/// <summary>
/// A dying unit's time ran out and the unit left the world. The world raises it in the tick the unit leaves, not with
/// the hit that doomed it; the shots still at the unit are dropped later, as the world advances its shots.
/// </summary>
public sealed record UnitDestroyed : CombatEvent
{
    /// <summary>The map object of the destroyed unit.</summary>
    public required PlacedObject Unit { get; init; }

    /// <summary>What ended the unit; the view plays the fireball for <see cref="DestroyCause.Fireball"/> only.</summary>
    public DestroyCause Cause { get; init; } = DestroyCause.Fireball;
}

/// <summary>
/// A repair spark: the <c>xspark</c> sprite shows for <see cref="Seconds"/> at a target hardpoint of a system that
/// repairs, and follows that hardpoint as the unit moves (0x4683d0 raises it through 0x515d50; 0x515f40 ages it and
/// 0x515fb0 draws it at the hardpoint's node).
/// </summary>
public sealed record RepairSpark : CombatEvent
{
    /// <summary>The sprite's name in the sprite tables.</summary>
    public const string SpriteName = "xspark";

    /// <summary>How long the spark shows: its queue entry ends after 1 second (0x4683d0 passes 1.0, 0x515f40).</summary>
    public const double Seconds = 1.0;

    /// <summary>The map object of the unit.</summary>
    public required PlacedObject Unit { get; init; }

    /// <summary>The system that repairs, one of <see cref="SystemNames"/>.</summary>
    public required string System { get; init; }

    /// <summary>The hardpoint the spark shows at, one of the system's target hardpoints.</summary>
    public required string Hardpoint { get; init; }

    /// <summary>
    /// The sprite's size: 0.14 of the unit's model radius (0x600794). The sprite is drawn this far out from its
    /// center on each side, so it is twice this wide (0x5975e0).
    /// </summary>
    public required float Size { get; init; }
}

/// <summary>
/// A hit's explosion: one of <c>systemexpl1</c> to <c>systemexpl7</c> shows for <see cref="Seconds"/> at the hit's
/// node of the target, and follows it as the unit moves (0x465100 raises it through 0x515d50; 0x515f40 ages it and
/// 0x515fb0 draws it).
/// </summary>
public sealed record HitExplosion : CombatEvent
{
    /// <summary>How long the explosion shows: its queue entry ends after 1 second (0x465100 passes 1.0, 0x515f40).</summary>
    public const double Seconds = 1.0;

    /// <summary>The map object of the unit that was hit.</summary>
    public required PlacedObject Unit { get; init; }

    /// <summary>The sprite's name in the sprite tables: <c>systemexpl1</c> to <c>systemexpl7</c>.</summary>
    public required string Name { get; init; }

    /// <summary>The target hardpoint the explosion shows at; null for the model's root node.</summary>
    public required string? Hardpoint { get; init; }

    /// <summary>
    /// The sprite's size: 0.14 of the unit's model radius, at least 4 (0x60076c, 0x600770). The sprite is drawn this
    /// far out from its center on each side, so it is twice this wide (0x5975e0).
    /// </summary>
    public required float Size { get; init; }
}

/// <summary>
/// A small explosion on a dying unit: one of <c>systemexpl1</c> to <c>systemexpl7</c> plays at a point on the unit
/// (0x466320, 0x466180).
/// </summary>
public sealed record SmallExplosion : CombatEvent
{
    /// <summary>The map object of the dying unit.</summary>
    public required PlacedObject Unit { get; init; }

    /// <summary>The explosion definition, without <c>.odf</c>: <c>systemexpl1</c> to <c>systemexpl7</c>.</summary>
    public required string Name { get; init; }

    /// <summary>The point of the explosion as an offset from the unit's center, in world axes.</summary>
    public required Vector3 Offset { get; init; }
}
