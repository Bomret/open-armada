using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace OpenArmada.Sandbox;

/// <summary>
/// What the combat world asks a unit's motion to do this tick: nothing (leave the motion alone), stop, move to a
/// point, or turn in place to face one. Points are (X, Z) in the pose source's frame.
/// </summary>
public enum MoveKind
{
    /// <summary>Leave the motion alone.</summary>
    None,

    /// <summary>Stop where the unit is.</summary>
    Stop,

    /// <summary>Move to the point.</summary>
    MoveTo,

    /// <summary>Turn in place to face the point.</summary>
    Face,
}

/// <summary>What the combat world asks a unit's motion to do this tick; X and Z are the point for MoveTo and Face.</summary>
public readonly record struct MoveIntent
{
    /// <summary>What the motion is to do; <c>default</c> holds <see cref="MoveKind.None"/>.</summary>
    public required MoveKind Kind { get; init; }

    /// <summary>The point's X in the pose source's frame.</summary>
    public float X { get; init; }

    /// <summary>The point's Z in the pose source's frame.</summary>
    public float Z { get; init; }
}

/// <summary>One weapon of a unit and the seconds until it has reloaded (at or below zero it is ready).</summary>
public sealed class WeaponState(WeaponProfile profile)
{
    /// <summary>The weapon's profile.</summary>
    public WeaponProfile Profile { get; } = profile;

    /// <summary>Seconds until the weapon has reloaded; at or below zero it is ready.</summary>
    public double Cooldown { get; set; }
}

/// <summary>
/// The combat state of one unit: its shields and crew, the weapons' reloads, and the order and target it holds.
/// The state is plain data so later slices extend it; CombatRules and CombatWorld change it.
/// </summary>
public sealed class UnitCombatState(
    PlacedObject unit,
    CombatProfile profile,
    string definitionName,
    float radius,
    bool mobile,
    double systemHitpointsBonus = 0
)
{
    /// <summary>The placed object.</summary>
    public PlacedObject Unit { get; } = unit;

    /// <summary>The unit's combat values.</summary>
    public CombatProfile Profile { get; } = profile;

    /// <summary>The unit's definition name as per-target values name it (<c>zmama.odf</c>).</summary>
    public string DefinitionName { get; } = definitionName;

    /// <summary>The unit's radius, for distances surface to surface.</summary>
    public float Radius { get; } = radius;

    /// <summary>Whether the unit moves at all: false for stations and ships without a physics definition.</summary>
    public bool Mobile { get; } = mobile;

    /// <summary>The placed object's team.</summary>
    public int Team => Unit.Team;

    /// <summary>A unit that starts with crew below one is derelict from the start: no crew, shields down.</summary>
    public double Shields { get; set; } = profile.CrewCost < 1 ? 0 : profile.MaxHealth;

    /// <summary>The crew left; starts at <c>crewCost</c>, or 0 for a unit derelict from the start.</summary>
    public double Crew { get; set; } = profile.CrewCost < 1 ? 0 : profile.CrewCost;

    /// <summary>Seconds the shields stay down after reaching zero.</summary>
    public double ShieldDown { get; set; }

    /// <summary>The part of damage that has not yet made a whole point.</summary>
    public double SystemsRemainder { get; set; }

    /// <summary>The ship systems the unit has, by SystemProfile.Name (a system with no hit points is absent).</summary>
    public Dictionary<string, ShipSystemState> Systems { get; } =
        profile
            .Systems.Where(s => s.HitPoints > 0)
            .ToDictionary(s => s.Name, s => new ShipSystemState(s, systemHitpointsBonus));

    /// <summary>A derelict unit has no crew: it never moves, fires nothing, regains nothing, and stays on the map.</summary>
    public bool Derelict { get; set; } = profile.CrewCost < 1;

    /// <summary>Whether the unit has the system <c>name</c> and it is destroyed; false for a system the unit lacks.</summary>
    public bool SystemDestroyed(string name) => Systems.TryGetValue(name, out var s) && s.Destroyed;

    /// <summary>Whether the unit cannot move: derelict, or with its engines destroyed.</summary>
    public bool Immobile => Derelict || SystemDestroyed(SystemNames.Engines);

    /// <summary>The state of each weapon of the profile, in profile order.</summary>
    public List<WeaponState> Weapons { get; } = profile.Weapons.Select(w => new WeaponState(w)).ToList();

    /// <summary>The unit's alert stance.</summary>
    public AlertStance Alert { get; set; } = profile.DefaultGreenAlert ? AlertStance.Green : AlertStance.Red;

    /// <summary>
    /// The unit that last hit this one; a red idle unit moves toward it until it picks a target.
    /// stand-in: the last attacker counts until it is invalid or the unit picks a target; the game's time window for
    /// "recently" is not known.
    /// </summary>
    public UnitCombatState? Attacker { get; set; }

    /// <summary>The unit the attack order names; null without an order.</summary>
    public UnitCombatState? Order { get; set; }

    /// <summary>
    /// Whether the unit's move intent is red's (closing on its pick, or the walk toward an attacker) and not a player's
    /// move order.
    /// </summary>
    public bool AutoMove { get; set; }

    /// <summary>The point (X, Z) of the player's move order, until the unit arrives; null without one.</summary>
    public (float X, float Z)? PlayerMove { get; set; }

    /// <summary>The unit the weapons fire at this tick.</summary>
    public UnitCombatState? Target { get; set; }

    /// <summary>What the world asks the unit's motion to do; the default leaves the motion alone.</summary>
    public MoveIntent Intent { get; set; }

    /// <summary>Whether a critical hit destroyed the unit; the world then removes it.</summary>
    public bool Destroyed { get; set; }

    /// <summary>
    /// The position at the latest Step call, read once per call: the ticks of the call use it, and the next call
    /// compares it with the new one to tell a stopped unit from a moving one. Null before the first call.
    /// </summary>
    public Vector3? Position { get; set; }

    /// <summary>Whether the unit did not move since the previous Step call (a unit the world has not yet seen is stopped).</summary>
    public bool Stopped { get; set; } = true;
}
