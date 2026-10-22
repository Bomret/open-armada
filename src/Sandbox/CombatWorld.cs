using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using OpenArmada.Formats;

namespace OpenArmada.Sandbox;

/// <summary>
/// One unit as the combat world takes it: the placed object, its combat values, the name per-target values know
/// it by, its radius, and whether it can move at all.
/// </summary>
public sealed record CombatUnit
{
    /// <summary>The placed object.</summary>
    public required PlacedObject Unit { get; init; }

    /// <summary>The unit's combat values.</summary>
    public required CombatProfile Profile { get; init; }

    /// <summary>The name per-target values know the unit by (<c>zmama.odf</c>).</summary>
    public required string DefinitionName { get; init; }

    /// <summary>The unit's radius in world units, for distances surface to surface; 0 for a model without meshes.</summary>
    public required float Radius { get; init; }

    /// <summary>Whether the unit can move at all.</summary>
    public required bool Mobile { get; init; }
}

/// <summary>
/// The fight of a map in plain C#: every unit's combat state, the orders, and the shots in flight. Step advances
/// it and returns what happened. Unit positions come from the pose source; randomness from the injected source, a
/// number in [0, 1) per call.
/// </summary>
public sealed class CombatWorld
{
    // stand-in: the world runs on fixed ticks of this many seconds, and a step carries the time left over to the
    // next, so one long step does what many short ones over the same time do; the game's own update rate is not
    // known.
    /// <summary>The length of one world tick in seconds.</summary>
    public const double Tick = 0.02;

    // stand-in: a shield bubble reaches this many radii from a unit's center; the game's shape is not known.
    const float BubbleRadii = 1.25f;

    // stand-in: a flying shot touches a unit of radius 0 (a model without meshes) within this many world units of
    // its center, so such a unit can be hit; the game's collision radius is not known.
    const float ZeroRadiusContactReach = 5f;
    const double Epsilon = 1e-9;
    const float ArrivedWithin = 1e-3f;

    // An idle unit at red closes on its pick only while its center is within 300 units of its tether point, in
    // 3D (0x42a3a9, the constant at 0x5ca7ac).
    const float TetherSquared = 90000f;

    // A hit counts as recent for less than this many seconds (0x46e640, the 1.0 at 0x42a14a).
    const double HitWindow = 1.0;

    // A hit on a unit of the same team within this many units on X and on Z counts too (0x42a000); stand-in: the
    // edge counts.
    const float TeammateBox = 400f;

    readonly UnitPoses _poses;
    readonly double _protection;
    readonly Func<double> _random;
    readonly Func<PlacedObject, string, ShipPose, Vector3> _hardpointPosition;
    readonly Dictionary<PlacedObject, UnitCombatState> _units = new(ReferenceEqualityComparer.Instance);
    readonly List<Shot> _shots = new();
    double _pending;
    long _ticks;

    /// <summary>
    /// A world of the units, without orders. <c>shieldDamageProtection</c> and
    /// <c>systemHitpointsBonus</c> are the RTS_CFG.h constants; <c>random</c> returns a number in [0, 1) per call.
    /// <c>hardpointPosition</c> gives the world position of a unit's named hardpoint at a pose.
    /// A later unit with the same placed object replaces an earlier one.
    /// </summary>
    public CombatWorld(
        UnitPoses poses,
        IEnumerable<CombatUnit> units,
        double shieldDamageProtection,
        Func<double> random,
        Func<PlacedObject, string, ShipPose, Vector3> hardpointPosition,
        double systemHitpointsBonus = 0
    )
    {
        _hardpointPosition = hardpointPosition;
        _poses = poses;
        _protection = shieldDamageProtection;
        _random = random;
        foreach (var u in units)
        {
            var state = new UnitCombatState(
                u.Unit,
                u.Profile,
                u.DefinitionName,
                u.Radius,
                u.Mobile,
                systemHitpointsBonus
            );
            if (u.Unit.Start is { } start)
            {
                state.Begin(start);
            }

            _units[u.Unit] = state;
        }
    }

    /// <summary>The shots in flight.</summary>
    public IReadOnlyList<Shot> Shots => _shots;

    /// <summary>The units still in the fight.</summary>
    public IEnumerable<UnitCombatState> Units => _units.Values;

    /// <summary>The state of a unit; null once it is destroyed or when it is no unit of the world.</summary>
    public UnitCombatState? State(PlacedObject unit) => _units.GetValueOrDefault(unit);

    /// <summary>
    /// Gives <c>attacker</c> an attack order on <c>target</c>, replacing its order. Refused (false) when either is gone, the
    /// target is not hostile to the attacker, or the attacker has no weapons.
    /// </summary>
    public bool Attack(PlacedObject attacker, PlacedObject target)
    {
        if (State(attacker) is not { } a || State(target) is not { } t)
            return false;
        if (!CombatRules.Hostile(a.Team, t.Team) || a.Weapons.Count == 0)
            return false;
        a.Order = t;
        a.AutoMove = false;
        a.PlayerMove = null;
        a.Intent = default;

        return true;
    }

    /// <summary>
    /// Ends the unit's attack order and its move order. The tether point becomes where the unit stands (an order
    /// without a position, 0x46eac0).
    /// </summary>
    public void ClearOrder(PlacedObject unit)
    {
        if (State(unit) is not { } state)
            return;
        state.Order = null;
        state.AutoMove = false;
        state.PlayerMove = null;
        state.Intent = default;
        state.Tether = Position(state);
    }

    /// <summary>
    /// Gives the unit a move order to the point (X, Z), replacing its attack order. The unit is not idle until it
    /// arrives, however the motion gets there (a ship turns in place first without changing its position). The
    /// destination becomes the tether point (0x42704e).
    /// </summary>
    public void Move(PlacedObject unit, float x, float z)
    {
        ClearOrder(unit);
        if (State(unit) is { } state)
        {
            state.PlayerMove = (x, z);
            // stand-in: a move names no height, so the tether point keeps the unit's own
            state.Tether = new Vector3(x, Position(state).Y, z);
        }
    }

    /// <summary>Advances the fight by <c>seconds</c> and returns the events of the ticks that ran.</summary>
    public IReadOnlyList<CombatEvent> Step(double seconds)
    {
        var events = new List<CombatEvent>();
        // Positions change once per Step call (a frame), so a unit is stopped when it did not move since the
        // previous call; every tick of the call shares that, and the to-hit does not depend on the frame rate.
        foreach (var u in _units.Values)
        {
            var at = _poses.Pose(u.Unit).Position;
            u.Stopped = u.Position == null || u.Position == at;
            u.Position = at;
            // a ship that arrives stands exactly on its target (ShipMotion), which ends the move order
            if (
                u.PlayerMove is { } to
                && Math.Abs(at.X - to.X) < ArrivedWithin
                && Math.Abs(at.Z - to.Z) < ArrivedWithin
            )
            {
                u.PlayerMove = null;
            }
        }

        _pending += Math.Max(0, seconds);
        while (_pending + Epsilon >= Tick)
        {
            _pending -= Tick;
            RunTick(events);
        }

        return events;
    }

    void RunTick(List<CombatEvent> events)
    {
        _ticks++;
        AdvanceShots(events);
        foreach (var u in _units.Values.ToList())
        {
            if (u.Destroyed)
                continue;
            Think(u);
            if (u.Mobile && u.Immobile)
            {
                u.Intent = new MoveIntent { Kind = MoveKind.Stop }; // no order moves a derelict ship or one with dead engines
                u.PlayerMove = null;
            }

            Fire(u, events);
            CombatRules.RegainShields(u, Tick);
            CombatRules.Sustain(u, Tick);
            CombatRules.Repair(u, Tick);
        }
    }

    // The unit's position as of the current Step call (positions change only between calls).
    Vector3 Position(UnitCombatState u) => u.Position ?? _poses.Pose(u.Unit).Position;

    // The distance between the units' surfaces.
    float Surface(UnitCombatState a, UnitCombatState b) =>
        Vector3.Distance(Position(a), Position(b)) - a.Radius - b.Radius;

    static float LongestRange(UnitCombatState u) => u.Profile.Weapons.Max(w => w.Range);

    // Sets the unit's movement and the target its weapons fire at.
    void Think(UnitCombatState u)
    {
        var held = u.Target; // the fallback target of the previous tick, if any
        u.Target = null;
        if (u.Weapons.Count == 0)
            return;
        if (u.Order is { Destroyed: true })
        {
            u.Order = null;
            u.Intent = u.Mobile ? new MoveIntent { Kind = MoveKind.Stop } : default;
            u.Tether = Position(u); // stand-in: where an attack order leaves the tether point was not traced
        }

        // stand-in: what red started ends with red, the closing on a pick and the return to the tether point (a
        // player's order stays); the game's behavior is not known.
        if (u.Alert != AlertStance.Red && u.AutoMove)
        {
            u.AutoMove = false;
            u.Intent = u.Mobile ? new MoveIntent { Kind = MoveKind.Stop } : default;
        }

        var range = LongestRange(u);
        var radius = range - 25;
        if (u.Order == null)
        {
            if (u.Alert == AlertStance.Green)
                return; // green picks nothing by itself
            u.Target = Select(u, held, radius);
            // stand-in: a unit is idle when it has no order of the player (no attack order, no move order until it
            // arrives); the game's own idle test was not traced.
            var idle = u.PlayerMove == null;
            if (u.Alert != AlertStance.Red || !u.Profile.CanAttack || u.Derelict || !idle)
                return; // yellow never moves on its own
            // a recent hit on the unit or a teammate nearby moves the tether point to where the unit stands (0x42a370
            // posts an order without a position; inferred that it takes that order's path, 0x46eac0)
            if (HitNearby(u))
            {
                u.Tether = Position(u);
            }

            // idle at red: close on the pick, which is chosen again every tick, while within the tether; else go
            // back to the tether point (0x42a3e4, 0x42a3f4), keeping the pick. No team of the sandbox is an AI team,
            // so the walk toward a recent attacker (0x42a298) never runs.
            if (u.Target != null && Vector3.DistanceSquared(Position(u), u.Tether) <= TetherSquared)
            {
                u.AutoMove = true;
            }
            else
            {
                ReturnToTether(u);

                return;
            }
        }

        var order = u.Order ?? u.Target!;
        var distance = Surface(u, order);
        if (u.Mobile)
        {
            var there = Position(order);
            // stand-in: the game's attack maneuver was not traced; the unit follows its target to the closing
            // range, then holds position and turns toward it once a weapon has a hardpoint within its range of the
            // target's center. Until then it keeps closing, so a target with a large radius is not out of reach.
            u.Intent = new MoveIntent
            {
                Kind =
                    distance > range - 50 || !u.Weapons.Any(w => NearestHardpoint(u, w.Profile, order) != null)
                        ? MoveKind.MoveTo
                        : MoveKind.Face,
                X = there.X,
                Z = there.Z,
            };
        }

        if (distance <= range)
        {
            u.Target = order;

            return;
        }

        // An ordered target out of range: the weapons fire at the unit's own pick (none at green).
        u.Target = u.Alert == AlertStance.Green ? null : Select(u, held, radius);
    }

    // Whether a shot landed less than HitWindow ago on the unit or on a unit of its team within TeammateBox.
    // stand-in: a teammate the hit destroyed has left _units (Land), so that hit counts for no one; whether the game
    // counts it is not known.
    // ponytail: every idle red unit scans every unit each tick (O(n²)); skip the scan while no hit is newer than
    // HitWindow, or a spatial index, when maps grow large.
    bool HitNearby(UnitCombatState u)
    {
        var at = Position(u);

        return _units.Values.Any(v =>
            v.Team == u.Team
            && v.LastHit is { } hit
            && (_ticks - hit) * Tick + Epsilon < HitWindow
            && Math.Abs(Position(v).X - at.X) <= TeammateBox
            && Math.Abs(Position(v).Z - at.Z) <= TeammateBox
        );
    }

    // Moves the unit back to its tether point, and stops it once it is there.
    void ReturnToTether(UnitCombatState u)
    {
        var at = Position(u);
        if (!u.Mobile || (Math.Abs(at.X - u.Tether.X) < ArrivedWithin && Math.Abs(at.Z - u.Tether.Z) < ArrivedWithin))
        {
            if (u.AutoMove)
            {
                u.Intent = u.Mobile ? new MoveIntent { Kind = MoveKind.Stop } : default;
            }

            u.AutoMove = false;

            return;
        }

        u.Intent = new MoveIntent
        {
            Kind = MoveKind.MoveTo,
            X = u.Tether.X,
            Z = u.Tether.Z,
        };
        u.AutoMove = true;
    }

    // A valid target is hostile, not being destroyed, and not derelict (0x42faa0).
    // stand-in: every unit is visible to every team until fog of war exists, and the extra check the game makes on
    // yellow candidates (meaning unknown) is skipped.
    static bool Valid(UnitCombatState u, UnitCombatState t) =>
        !t.Destroyed && !t.Derelict && CombatRules.Hostile(u.Team, t.Team);

    // Target selection (0x42fd6f): the current target while it stays valid and within `radius`, else the nearest
    // valid one within it.
    UnitCombatState? Select(UnitCombatState u, UnitCombatState? held, float radius) =>
        held != null && Valid(u, held) && Surface(u, held) <= radius ? held : NearestHostile(u, radius);

    // The nearest valid target whose surface is within `range`; null when none is.
    // ponytail: every unit scans every other unit each tick (O(n²)); a spatial index when maps grow large.
    UnitCombatState? NearestHostile(UnitCombatState u, float range) =>
        _units
            .Values.Where(t => Valid(u, t))
            .Select(t => (Unit: t, Distance: Surface(u, t)))
            .Where(x => x.Distance <= range)
            .OrderBy(x => x.Distance)
            .Select(x => x.Unit)
            .FirstOrDefault();

    void Fire(UnitCombatState u, List<CombatEvent> events)
    {
        foreach (var weapon in u.Weapons)
        {
            weapon.Cooldown -= Tick;
            // a derelict unit and one with destroyed weapons fire nothing
            var target = u.Derelict || u.SystemDestroyed(SystemNames.Weapons) ? null : u.Target;
            // A weapon fires from the nearest hardpoint within its own range of the target's center (0x509bd0,
            // 0x50a520, 0x50a290), the first listed on a tie; with none in range it fires nothing and stays ready.
            // Units do not move within a step, so every round of the tick makes the same check.
            var hardpoint = target == null ? null : NearestHardpoint(u, weapon.Profile, target);
            if (target == null || hardpoint == null)
            {
                weapon.Cooldown = Math.Max(0, weapon.Cooldown); // an idle weapon does not store shots

                continue;
            }

            var delay = CombatRules.ReloadDelay(weapon.Profile, u);
            // A delay below a tick fires several rounds in one tick; a delay of zero fires one round per tick. A
            // round is one shot from one hardpoint.
            while (weapon.Cooldown <= Epsilon)
            {
                events.Add(new ShotFired { Shot = Launch(u, weapon.Profile, target, hardpoint) });
                weapon.Cooldown += delay;
                if (delay <= Epsilon)
                {
                    weapon.Cooldown = 0;

                    break;
                }
            }
        }
    }

    // The world position of the unit's hardpoint, with the unit at its position as of the current Step call.
    Vector3 HardpointAt(UnitCombatState unit, string name) =>
        _hardpointPosition(unit.Unit, name, _poses.Pose(unit.Unit) with { Position = Position(unit) });

    // The listed hardpoint nearest to the target's center within the weapon's range; null when none is. OrderBy is
    // stable, so the first listed wins a tie.
    string? NearestHardpoint(UnitCombatState shooter, WeaponProfile weapon, UnitCombatState target)
    {
        var center = Position(target);

        return weapon
            .Hardpoints.Select(name => (Name: name, Distance: Vector3.Distance(HardpointAt(shooter, name), center)))
            .Where(x => x.Distance <= weapon.Range)
            .OrderBy(x => x.Distance)
            .Select(x => x.Name)
            .FirstOrDefault();
    }

    // One shot of the weapon at the target, with its to-hit rolled now. A shot that missed is reported and not kept.
    // stand-in: a missed shot hits nothing and flies on to nowhere; the game's miss behavior is not known.
    Shot Launch(UnitCombatState shooter, WeaponProfile weapon, UnitCombatState target, string hardpoint)
    {
        // stand-in: nothing in the engine is at warp yet, so the warp modifiers never apply
        var chance = CombatRules.ToHitChance(
            weapon,
            target.DefinitionName,
            false,
            false,
            shooter.Stopped,
            target.Stopped
        );
        var hits = target.Derelict || target.SystemDestroyed(SystemNames.Engines) || _random() <= chance;
        // The shot leaves from the chosen hardpoint's world position.
        var from = HardpointAt(shooter, hardpoint);
        var toward = Position(target) - from;
        var kind = weapon.Ordnance.ClassLabel switch
        {
            "phaser" => ShotKind.Beam,
            "pphaser" => ShotKind.Pulse, // stand-in: a pulse flies straight; the game's pulse flight is not known
            _ => ShotKind.Torpedo, // stand-in: poleron torpedoes and unknown classes fly as torpedoes
        };
        var shot = new Shot(
            kind,
            shooter,
            target,
            weapon,
            hits,
            from,
            toward == Vector3.Zero ? Vector3.UnitZ : Vector3.Normalize(toward),
            hardpoint
        );
        if (hits)
        {
            _shots.Add(shot);
        }

        return shot;
    }

    void AdvanceShots(List<CombatEvent> events)
    {
        foreach (var shot in _shots.ToList())
        {
            if (shot.Target.Destroyed)
            {
                _shots.Remove(shot);

                continue;
            }

            shot.Age += Tick;
            var ordnance = shot.Weapon.Ordnance;
            if (shot.Kind == ShotKind.Beam)
            {
                // The beam ends without damage as soon as its shooter is gone or its target is beyond twice the range.
                if (shot.Shooter.Destroyed || Surface(shot.Shooter, shot.Target) > 2 * shot.Weapon.Range)
                {
                    _shots.Remove(shot);
                }
                else if (shot.Age + Epsilon >= ordnance.LifeSpan)
                {
                    _shots.Remove(shot);
                    Land(shot, events);
                }

                continue;
            }

            if (shot.Kind == ShotKind.Torpedo && shot.Age <= ordnance.SeekTime)
            {
                shot.Direction = Steer(shot, Tick);
            }

            var step = shot.Direction * ordnance.ShotSpeed * (float)Tick;
            var reach =
                shot.Target.Radius == 0 ? ZeroRadiusContactReach
                : shot.Target.Shields > 0 ? shot.Target.Radius * BubbleRadii
                : shot.Target.Radius;
            if (SegmentDistance(shot.Position, shot.Position + step, Position(shot.Target)) <= reach)
            {
                _shots.Remove(shot);
                Land(shot, events);
            }
            else if (shot.Age + Epsilon >= ordnance.LifeSpan)
            {
                _shots.Remove(shot); // expires without damage
            }
            else
            {
                shot.Position += step;
            }
        }
    }

    // The shot's direction turned toward its target by at most omegaTurn over `seconds`.
    Vector3 Steer(Shot shot, double seconds)
    {
        var want = Position(shot.Target) - shot.Position;
        if (want == Vector3.Zero)
            return shot.Direction;
        want = Vector3.Normalize(want);
        var angle = MathF.Acos(Math.Clamp(Vector3.Dot(shot.Direction, want), -1, 1));
        // stand-in: omegaTurn is read as radians per second; its unit in the game is not known.
        var most = shot.Weapon.Ordnance.OmegaTurn * (float)seconds;
        if (angle <= most)
            return want;
        var axis = Vector3.Cross(shot.Direction, want);
        axis = axis.LengthSquared() < 1e-12f ? Vector3.UnitY : Vector3.Normalize(axis);

        return Vector3.Transform(shot.Direction, Quaternion.CreateFromAxisAngle(axis, most));
    }

    // The distance from `point` to the segment.
    static float SegmentDistance(Vector3 from, Vector3 to, Vector3 point)
    {
        var along = to - from;
        var length = along.LengthSquared();
        var t = length == 0 ? 0 : Math.Clamp(Vector3.Dot(point - from, along) / length, 0, 1);

        return Vector3.Distance(from + along * t, point);
    }

    // The shot lands: its damage and hit location are rolled and applied to the target.
    void Land(Shot shot, List<CombatEvent> events)
    {
        var target = shot.Target;
        var damage = CombatRules.Damage(shot.Weapon.Ordnance, target.DefinitionName, _random());
        var location = CombatRules.Location(target, Math.Min(99, (int)(_random() * 100)));
        var hit = CombatRules.Apply(target, shot.Weapon.Ordnance, damage, location, _protection);
        target.LastHit = _ticks;
        events.Add(new ShotLanded { Shot = shot, Hit = hit });
        if (!hit.Destroyed)
            return;
        _units.Remove(target.Unit);
        _shots.RemoveAll(s => s.Target == target);
        target.Order = null;
        target.Target = null;
        events.Add(new UnitDestroyed { Unit = target.Unit });
    }
}
