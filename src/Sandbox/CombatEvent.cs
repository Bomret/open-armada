using System.Numerics;

namespace OpenArmada.Sandbox;

// Where a hit lands: one of the five ship systems, the hull, or no part that takes damage (a critical hit).
public enum HitLocation
{
    Engines,
    LifeSupport,
    Weapons,
    ShieldGenerator,
    Sensors,
    Hull,
    Critical,
}

// The outcome of one hit on a unit: where it landed, the damage rolled, the shield points lost, the crew lost,
// the whole points of the systems part that passed on, and whether it destroyed the unit.
public sealed record Hit(
    HitLocation Location,
    double Damage,
    double ShieldLoss,
    double CrewLoss,
    int Points,
    bool Destroyed
);

public enum ShotKind
{
    Beam,
    Torpedo,
    Pulse,
}

// One shot in flight. A beam holds no position; a torpedo and a pulse fly from Position along Direction. Hits is
// the to-hit roll made at fire time: a shot that missed is reported and then dropped.
public sealed class Shot(
    ShotKind kind,
    UnitCombatState shooter,
    UnitCombatState target,
    WeaponProfile weapon,
    bool hits,
    Vector3 position,
    Vector3 direction,
    string? hardpoint = null
)
{
    // The name of the hardpoint node the weapon fires this shot from; null for a hardpoint entry without a name.
    public string? Hardpoint { get; } = hardpoint;

    public ShotKind Kind { get; } = kind;
    public UnitCombatState Shooter { get; } = shooter;
    public UnitCombatState Target { get; } = target;
    public WeaponProfile Weapon { get; } = weapon;
    public bool Hits { get; } = hits;
    public Vector3 Position { get; set; } = position;
    public Vector3 Direction { get; set; } = direction;
    public double Age { get; set; }
}

// What happened in one world step.
public abstract record CombatEvent;

public sealed record ShotFired(Shot Shot) : CombatEvent;

public sealed record ShotLanded(Shot Shot, Hit Hit) : CombatEvent;

public sealed record UnitDestroyed(PlacedObject Unit) : CombatEvent;
