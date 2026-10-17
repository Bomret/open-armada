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

// The names of the five ship systems: SystemProfile.Name and the keys of UnitCombatState.Systems. Each is the name
// of the system's hit location, so a hit location's name is the system it damages.
public static class SystemNames
{
    public const string Engines = nameof(HitLocation.Engines);
    public const string LifeSupport = nameof(HitLocation.LifeSupport);
    public const string Weapons = nameof(HitLocation.Weapons);
    public const string ShieldGenerator = nameof(HitLocation.ShieldGenerator);
    public const string Sensors = nameof(HitLocation.Sensors);
}

// The outcome of one hit on a unit: where it landed, the damage rolled, the shield points lost, the crew lost,
// the whole points of the systems part that passed on, and whether it destroyed the unit.
public sealed record Hit
{
    public required HitLocation Location { get; init; }
    public required double Damage { get; init; }
    public required double ShieldLoss { get; init; }
    public required double CrewLoss { get; init; }
    public required int Points { get; init; }
    public required bool Destroyed { get; init; }
}

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
    string hardpoint
)
{
    // The name of the hardpoint node the weapon fires this shot from.
    public string Hardpoint { get; } = hardpoint;

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

public sealed record ShotFired : CombatEvent
{
    public required Shot Shot { get; init; }
}

public sealed record ShotLanded : CombatEvent
{
    public required Shot Shot { get; init; }
    public required Hit Hit { get; init; }
}

public sealed record UnitDestroyed : CombatEvent
{
    public required PlacedObject Unit { get; init; }
}
