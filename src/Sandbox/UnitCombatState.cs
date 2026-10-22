using System;
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

    /// <summary>
    /// Destroys the system <c>name</c> and hands its live hit percent, shares included, evenly to the systems still
    /// working (0x467620); with none working, or on a derelict unit where no system counts as working (0x466ca0),
    /// the percent goes to the critical share (0x467b70), which is the part of the roll no location takes.
    /// </summary>
    public void DestroySystem(string name)
    {
        var system = Systems[name];
        system.Destroyed = true;
        var heirs = Derelict ? [] : Systems.Values.Where(s => !s.Destroyed).ToList();
        foreach (var s in heirs)
        {
            s.HitPercent += system.HitPercent / heirs.Count;
        }

        system.HitPercent = 0;
    }

    /// <summary>
    /// Restores the system <c>name</c> and gives it its own hit percent back (0x467600), so the total is conserved.
    /// The other working systems give it up in proportion to their excess over their own percents; what their
    /// excess does not cover comes out of the critical share. A system that is not destroyed keeps its live percent.
    /// </summary>
    public void RestoreSystem(string name)
    {
        var system = Systems[name];
        if (!system.Destroyed)
        {
            return;
        }

        system.Destroyed = false;
        system.HitPercent = system.Profile.HitPercent;
        var donors = Systems.Values.Where(s => s != system && !s.Destroyed).ToList();
        var totalExcess = donors.Sum(s => Math.Max(0, s.HitPercent - s.Profile.HitPercent));
        if (totalExcess <= 0)
        {
            return;
        }

        var factor = Math.Min(1, system.HitPercent / totalExcess);
        foreach (var s in donors)
        {
            s.HitPercent -= Math.Max(0, s.HitPercent - s.Profile.HitPercent) * factor;
        }
    }

    /// <summary>
    /// Whether the unit has no thrust and no order moves it: derelict, with its engines destroyed, or dying. The one
    /// place that decides it.
    /// </summary>
    public bool Immobile => Derelict || SystemDestroyed(SystemNames.Engines) || Dying;

    /// <summary>Whether the unit coasts (ADR 0008): a unit that moves at all and has no thrust.</summary>
    public bool Coasting => Mobile && Immobile;

    /// <summary>The world tick that found the unit without thrust; null while the unit has thrust.</summary>
    public long? CoastTick { get; set; }

    /// <summary>
    /// How many seconds of the latest Step call lie after <see cref="CoastTick"/>; null while the unit has thrust
    /// at the world's ticks. The view hands it to ShipMotion.Coast, so the coast starts at that tick and not at the
    /// frame that follows it.
    /// </summary>
    public double? CoastOvershoot { get; set; }

    /// <summary>The state of each weapon of the profile, in profile order.</summary>
    public List<WeaponState> Weapons { get; } = profile.Weapons.Select(w => new WeaponState(w)).ToList();

    /// <summary>The unit's alert stance.</summary>
    public AlertStance Alert { get; set; } = profile.DefaultGreenAlert ? AlertStance.Green : AlertStance.Red;

    /// <summary>
    /// The tether point: an idle unit at red closes on its pick only within 300 units of it, and returns to it
    /// otherwise (0x42a170). stand-in: it starts at the placed position; the game's point at creation was not found.
    /// </summary>
    public Vector3 Tether { get; set; } = unit.Position;

    /// <summary>
    /// The world tick a shot last landed on the unit (0x46e340 records the game time of a hit); none before the
    /// first hit. stand-in: only a landed shot counts.
    /// </summary>
    public long? LastHit { get; set; }

    /// <summary>The unit the attack order names; null without an order.</summary>
    public UnitCombatState? Order { get; set; }

    /// <summary>
    /// Whether the unit's move intent is red's (closing on its pick, or the return to its tether point) and not a
    /// player's move order.
    /// </summary>
    public bool AutoMove { get; set; }

    /// <summary>The point (X, Z) of the player's move order, until the unit arrives; null without one.</summary>
    public (float X, float Z)? PlayerMove { get; set; }

    /// <summary>The unit the weapons fire at this tick.</summary>
    public UnitCombatState? Target { get; set; }

    /// <summary>What the world asks the unit's motion to do; the default leaves the motion alone.</summary>
    public MoveIntent Intent { get; set; }

    /// <summary>
    /// Whether the unit has left the world: its dying phase ended. A critical hit makes it a dying unit first
    /// (<see cref="Dying"/>).
    /// </summary>
    public bool Destroyed { get; set; }

    /// <summary>
    /// Whether a critical hit doomed the unit and it has not yet left the world (a dying unit): it coasts, is no
    /// valid target, fires, repairs, regains and loses nothing, and its timer runs (0x468990).
    /// </summary>
    public bool Dying { get; set; }

    /// <summary>
    /// The seconds of the dying phase left; each further damaging hit cuts 0.5 s (0x465100). At or below zero the
    /// unit leaves on the next tick. Meaningful only while <see cref="Dying"/>.
    /// </summary>
    public double DyingRemaining { get; set; }

    /// <summary>The simulated seconds since the last small explosion on the dying unit, kept on the world's ticks.</summary>
    public double ExplosionClock { get; set; }

    /// <summary>
    /// The unit's speed in world units per second as of the latest Step call: its position change over that call.
    /// The dying time depends on it (0x466090). stand-in: the world has no velocity of its own, so the speed is
    /// read from the positions.
    /// </summary>
    public double Speed { get; set; }

    /// <summary>
    /// The position at the latest Step call, read once per call: the ticks of the call use it, and the next call
    /// compares it with the new one to tell a stopped unit from a moving one. Null before the first call.
    /// </summary>
    public Vector3? Position { get; set; }

    /// <summary>Whether the unit did not move since the previous Step call (a unit the world has not yet seen is stopped).</summary>
    public bool Stopped { get; set; } = true;
}
