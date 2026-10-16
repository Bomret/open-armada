using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace OpenArmada.Sandbox;

// What the combat world asks a unit's motion to do this tick: nothing (leave the motion alone), stop, move to a
// point, or turn in place to face one. Points are (X, Z) in the pose source's frame.
public enum MoveKind
{
    None,
    Stop,
    MoveTo,
    Face,
}

public readonly record struct MoveIntent(MoveKind Kind, float X = 0, float Z = 0);

// One weapon of a unit and the seconds until it has reloaded (at or below zero it is ready).
public sealed class WeaponState(WeaponProfile profile)
{
    public WeaponProfile Profile { get; } = profile;
    public double Cooldown { get; set; }
}

// The combat state of one unit: its shields and crew, the weapons' reloads, and the order and target it holds.
// The state is plain data so later slices extend it; CombatRules and CombatWorld change it.
public sealed class UnitCombatState(
    PlacedObject unit,
    CombatProfile profile,
    string definitionName,
    float radius,
    bool mobile
)
{
    public PlacedObject Unit { get; } = unit;
    public CombatProfile Profile { get; } = profile;

    // The unit's definition name as per-target values name it (`zmama.odf`).
    public string DefinitionName { get; } = definitionName;

    // The unit's radius, for distances surface to surface.
    public float Radius { get; } = radius;

    // Whether the unit moves at all: false for stations and ships without a physics definition.
    public bool Mobile { get; } = mobile;
    public int Team => Unit.Team;
    public double Shields { get; set; } = profile.MaxHealth;
    public double Crew { get; set; } = profile.CrewCost;

    // Seconds the shields stay down after reaching zero.
    public double ShieldDown { get; set; }

    // The part of damage that has not yet made a whole point.
    public double SystemsRemainder { get; set; }

    // Whole points of damage each ship system (by SystemProfile.Name) has taken; they have no effect yet.
    public Dictionary<string, int> SystemDamage { get; } = new();
    public List<WeaponState> Weapons { get; } = profile.Weapons.Select(w => new WeaponState(w)).ToList();

    // The unit the attack order names; null without an order.
    public UnitCombatState? Order { get; set; }

    // The unit the weapons fire at this tick.
    public UnitCombatState? Target { get; set; }
    public MoveIntent Intent { get; set; }
    public bool Destroyed { get; set; }

    // The position at the previous tick, to tell a stopped unit from a moving one.
    public Vector3? LastPosition { get; set; }
}
