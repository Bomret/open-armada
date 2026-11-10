using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using OpenArmada.Formats;

namespace OpenArmada.Sandbox;

/// <summary>
/// What the combat world asks a unit's flight to do this tick: nothing (follow the move order it has, or brake where it
/// is), brake where it is, move to a point directly or by path, or fight the dogfight at one. Points are (X, Y, Z) in the pose source's frame.
/// </summary>
public enum MoveKind
{
    /// <summary>Leave the flight alone: the unit follows the move order it has, or brakes where it is.</summary>
    None,

    /// <summary>Stop where the unit is: it brakes to rest and levels out.</summary>
    Stop,

    /// <summary>Move straight to the point.</summary>
    MoveTo,

    /// <summary>Move by path to the point: an attack's closing from beyond the replan distance.</summary>
    Path,

    /// <summary>Fight the dogfight with the target whose origin is the point (<see cref="FlightModel.Dogfight"/>).</summary>
    Dogfight,

    /// <summary>Turn in place toward the point at no thrust (<see cref="FlightModel.Pivot"/>).</summary>
    Pivot,
}

/// <summary>What the combat world asks a unit's flight to do this tick; X, Y, and Z are the point for every kind but None and Stop.</summary>
public readonly record struct MoveIntent
{
    /// <summary>What the motion is to do; <c>default</c> holds <see cref="MoveKind.None"/>.</summary>
    public required MoveKind Kind { get; init; }

    /// <summary>The point's X in the pose source's frame.</summary>
    public float X { get; init; }

    /// <summary>The point's height.</summary>
    public float Y { get; init; }

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

    /// <summary>
    /// The unit's current team (INV-0012): the placed object's team at the start, then whatever a team change set.
    /// Every reader of the running sandbox reads this and never the placed object's team.
    /// </summary>
    public int Team { get; set; } = unit.Team;

    /// <summary>
    /// The unit's current race values: the definition's race at the start, then the race a team change set. Crew
    /// status, weapon reload, and repair read these and never <see cref="Profile"/>'s race.
    /// </summary>
    public RaceValues Race { get; set; } = profile.Race;

    /// <summary>
    /// The name of the unit's current race (INV-0014): the definition's <c>race</c> at the start, then the race a
    /// team change set. The <c>borg</c> overlay and the state dump read this, never <see cref="Profile"/>'s race name.
    /// </summary>
    public string RaceName { get; set; } = profile.RaceName;

    /// <summary>
    /// The unit's current special energy; starts at the profile's maximum (stand-in: the exe's start value is not
    /// recorded) and never exceeds it.
    /// </summary>
    public double SpecialEnergy { get; set; } = profile.MaxSpecialEnergy;

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
    /// The unit's flight, which the combat world steps on its ticks and takes its orders into; null for a unit
    /// whose pose something else drives (a station, or a ship without a physics definition).
    /// </summary>
    public FlightModel? Motion { get; init; }

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
    /// The team relation between the unit and the order's target as of the order (0x4275ee, 0x4275f3); the order
    /// holds its target only while <see cref="CombatRules.RelationOf"/> still returns it. Meaningful with an order.
    /// </summary>
    public TeamRelation OrderRelation { get; set; }

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

    /// <summary>Whether the unit is transporting (0x466f70 sets it, 0x467000 and 0x467050 clear it).</summary>
    public bool Transporting { get; set; }

    /// <summary>The unit the transport aims at; null while the unit is not transporting.</summary>
    public UnitCombatState? TransportTarget { get; set; }

    /// <summary>
    /// The seconds until the transporter's next cycle (0x4cc); at or below zero the cycle runs. It counts down on every
    /// tick, transporting or not.
    /// </summary>
    public double TransportTimer { get; set; }

    /// <summary>
    /// The teams that started boarding this unit and have not since ordered an attack on it (the boarding block,
    /// 0x466f70, 0x427616): their automatic selection rejects the unit and their attack orders drop it. A team change of
    /// the unit clears it.
    /// </summary>
    public HashSet<int> BoardedBy { get; } = new();

    /// <summary>The unit the weapons fire at this tick.</summary>
    public UnitCombatState? Target { get; set; }

    /// <summary>What the world asks the unit's motion to do; the default leaves the motion alone.</summary>
    public MoveIntent Intent { get; set; }

    /// <summary>The path an attack's closing built; the world ends it when the attack no longer asks for a path move.</summary>
    public MovePath? AttackPath { get; set; }

    /// <summary>The order list and replanning of the player's move order, until it ends; null without one.</summary>
    internal PlannedMove? Planned { get; set; }

    /// <summary>The order list and replanning of the attack's path move; null while the attack asks for none.</summary>
    internal PlannedMove? AttackMove { get; set; }

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
    /// The unit's world velocity in units per second: its flight's velocity as of the latest tick, 0 for a unit
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

    /// <summary>
    /// Whether the unit is stopped: its speed is below 0.1, the game's own rest threshold of the no-thrust brake (squared 0.01,
    /// 0x459c46). A unit the world has not yet seen is stopped.
    /// stand-in: the game's test for the to-hit stop modifiers was not traced.
    /// </summary>
    public bool Stopped { get; set; } = true;
}

/// <summary>The team relation between an attacker and its target that an attack order stores (<see cref="CombatRules.RelationOf"/>).</summary>
public enum TeamRelation
{
    /// <summary>The target is derelict.</summary>
    Derelict,

    /// <summary>The target is live and hostile to the attacker.</summary>
    Enemy,

    /// <summary>The target is live and no enemy of the attacker.</summary>
    NotEnemy,
}
