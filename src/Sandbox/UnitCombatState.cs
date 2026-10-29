using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using OpenArmada.Formats;

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

    /// <summary>The unit class's shield ellipsoid; null for a model that has none.</summary>
    public ShieldEllipsoid? Shield { get; init; }

    /// <summary>The radius of the unit's model's bounding sphere, which sizes its repair sparks.</summary>
    public float ModelRadius { get; init; }

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
    /// Puts the unit in its starting state, in this order: each listed system the unit has is destroyed at 0 hit
    /// points by <see cref="DestroySystem"/>; the crew is set to the fraction of crewCost exactly, and a crew below
    /// one makes the unit derelict with crew 0 and shields 0, as a unit derelict from the start is; then the shields
    /// are set to the fraction of maxHealth. From there the combat rules run unchanged.
    /// </summary>
    public void Begin(StartingState start)
    {
        // stand-in: a destroyed system costs no crew here, so the starting crew is the fraction exactly; the game
        // starts every unit at full strength.
        foreach (var name in start.DestroyedSystems.Where(Systems.ContainsKey))
        {
            Systems[name].HitPoints = 0;
            DestroySystem(name);
        }

        if (start.Crew is { } crew)
        {
            Crew = crew * Profile.CrewCost;
        }

        if (Crew < 1)
        {
            Derelict = true;
            Crew = 0;
            Shields = 0;

            return;
        }

        if (start.Shields is { } shields)
        {
            // stand-in: no shield-down timer, so shields of 0 regain from the first tick.
            Shields = shields * Profile.MaxHealth;
        }
    }

    /// <summary>
    /// Whether the unit has no thrust and no order moves it: derelict, with its engines destroyed, or dying. The one
    /// place that decides it.
    /// </summary>
    public bool Immobile => Derelict || SystemDestroyed(SystemNames.Engines) || Dying;

    /// <summary>Whether the unit coasts (ADR 0008): a unit that moves at all and has no thrust.</summary>
    public bool Coasting => Mobile && Immobile;

    /// <summary>
    /// Whether the unit shows the shield-down overlay: its shield generator is destroyed and it is not derelict
    /// (created 0x467b70, 0x4679b0; removed 0x467e06 when the generator is back, 0x466e7e when the unit turns derelict;
    /// created again 0x466f07 when the crew recovers while the generator is still offline). One flag, so a unit never
    /// has more than one overlay.
    /// </summary>
    public bool ShieldDownOverlay => !Derelict && SystemDestroyed(SystemNames.ShieldGenerator);

    /// <summary>
    /// The unit's motion, which the combat world steps on its ticks and takes its orders into; null for a unit
    /// whose pose something else drives (a station, or a ship without a physics definition).
    /// </summary>
    public ShipMotion? Motion { get; init; }

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
    /// Whether the unit's move intent is red's closing on its pick and not a player's move order or the hold's return
    /// to the tether point (<see cref="Returning"/>).
    /// </summary>
    public bool AutoMove { get; set; }

    /// <summary>
    /// Whether the unit drives back to its tether point after something moved it farther than its tooCloseToTurn
    /// from it (the hold, at every alert stance). It ends on reaching the point, or by a push inside the radius.
    /// </summary>
    public bool Returning { get; set; }

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
    /// Whether a critical hit doomed the unit and it has not yet left the world (a dying unit): a dying ship coasts;
    /// the unit is no valid target, fires, repairs, regains and loses nothing, and its timer runs (0x468990).
    /// </summary>
    public bool Dying { get; set; }

    /// <summary>
    /// The seconds of the dying phase left; each further damaging hit cuts 0.5 s (0x465100). At or below zero the
    /// unit leaves on the next tick. Meaningful only while <see cref="Dying"/>.
    /// </summary>
    public double DyingRemaining { get; set; }

    /// <summary>
    /// Whether a black hole swallowed the unit (0x450a40 sets its black-hole death flag). A swallowed unit is dying
    /// with a timer of 0 and ends without a fireball and without small explosions.
    /// </summary>
    public bool Swallowed { get; set; }

    /// <summary>The simulated seconds since the last small explosion on the dying unit, kept on the world's ticks.</summary>
    public double ExplosionClock { get; set; }

    /// <summary>
    /// The unit's world velocity in units per second: its motion's velocity as of the latest tick, 0 for a unit
    /// without motion that has not moved. For a unit whose pose something else drives, its position change over
    /// the latest Step call (stand-in: the world has no motion of it to read).
    /// </summary>
    public Vector3 Velocity { get; set; }

    /// <summary>
    /// The unit's speed: the length of its <see cref="Velocity"/>, which the dying time (0x466090) and the
    /// to-hit modifiers read. A push changes no speed; the game reads the velocity (+0x140).
    /// </summary>
    public double Speed => Velocity.Length();

    /// <summary>
    /// The unit's position as of the latest tick of its motion, or for a unit without motion the latest Step
    /// call. Null before the first Step call.
    /// </summary>
    public Vector3? Position { get; set; }

    /// <summary>Whether the unit has no velocity (a unit the world has not yet seen is stopped).</summary>
    public bool Stopped { get; set; } = true;
}
