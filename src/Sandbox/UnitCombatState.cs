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
    bool mobile,
    double systemHitpointsBonus = 0
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

    // A unit that starts with crew below one is derelict from the start: no crew, shields down.
    public double Shields { get; set; } = profile.CrewCost < 1 ? 0 : profile.MaxHealth;
    public double Crew { get; set; } = profile.CrewCost < 1 ? 0 : profile.CrewCost;

    // Seconds the shields stay down after reaching zero.
    public double ShieldDown { get; set; }

    // The part of damage that has not yet made a whole point.
    public double SystemsRemainder { get; set; }

    // The ship systems the unit has, by SystemProfile.Name (a system with no hit points is absent).
    public Dictionary<string, ShipSystemState> Systems { get; } =
        profile
            .Systems.Where(s => s.HitPoints > 0)
            .ToDictionary(s => s.Name, s => new ShipSystemState(s, systemHitpointsBonus));

    // A derelict unit has no crew: it never moves, fires nothing, regains nothing, and stays on the map.
    public bool Derelict { get; set; } = profile.CrewCost < 1;

    public bool SystemDestroyed(string name) => Systems.TryGetValue(name, out var s) && s.Destroyed;

    // Whether the unit cannot move: derelict, or with its engines destroyed.
    public bool Immobile => Derelict || SystemDestroyed(SystemNames.Engines);
    public List<WeaponState> Weapons { get; } = profile.Weapons.Select(w => new WeaponState(w)).ToList();

    // The unit's alert stance.
    public AlertStance Alert { get; set; } = profile.DefaultGreenAlert ? AlertStance.Green : AlertStance.Red;

    // The unit that last hit this one; a red idle unit moves toward it until it picks a target.
    // stand-in: the last attacker counts until it is invalid or the unit picks a target; the game's time window for
    // "recently" is not known.
    public UnitCombatState? Attacker { get; set; }

    // The unit the attack order names; null without an order.
    public UnitCombatState? Order { get; set; }

    // Whether the attack order was given by the alert stance (red) and not by the player; it ends with red.
    public bool AutoOrder { get; set; }

    // Whether the unit's move intent is the red walk toward an attacker and not a player's move order.
    public bool AutoMove { get; set; }

    // The point (X, Z) of the player's move order, until the unit arrives; null without one.
    public (float X, float Z)? PlayerMove { get; set; }

    // The unit the weapons fire at this tick.
    public UnitCombatState? Target { get; set; }
    public MoveIntent Intent { get; set; }
    public bool Destroyed { get; set; }

    // The position at the latest Step call, read once per call: the ticks of the call use it, and the next call
    // compares it with the new one to tell a stopped unit from a moving one. Null before the first call.
    public Vector3? Position { get; set; }

    // Whether the unit did not move since the previous Step call (a unit the world has not yet seen is stopped).
    public bool Stopped { get; set; } = true;
}
