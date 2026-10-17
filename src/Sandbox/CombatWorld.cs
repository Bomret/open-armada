using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace OpenArmada.Sandbox;

// One unit as the combat world takes it: the placed object, its combat values, the name per-target values know
// it by, its radius, and whether it can move at all.
public sealed record CombatUnit(
    PlacedObject Unit,
    CombatProfile Profile,
    string DefinitionName,
    float Radius,
    bool Mobile
);

// The fight of a map in plain C#: every unit's combat state, the orders, and the shots in flight. Step advances
// it and returns what happened. Unit positions come from the pose source; randomness from the injected source, a
// number in [0, 1) per call.
public sealed class CombatWorld
{
    // stand-in: the world runs on fixed ticks of this many seconds, and a step carries the time left over to the
    // next, so one long step does what many short ones over the same time do; the game's own update rate is not
    // known.
    public const double Tick = 0.02;

    // stand-in: a shield bubble reaches this many radii from a unit's center; the game's shape is not known.
    const float BubbleRadii = 1.25f;

    // stand-in: a flying shot touches a unit of radius 0 (a model without meshes) within this many world units of
    // its center, so such a unit can be hit; the game's collision radius is not known.
    const float ZeroRadiusContactReach = 5f;
    const double Epsilon = 1e-9;
    const float ArrivedWithin = 1e-3f;

    readonly UnitPoses _poses;
    readonly double _protection;
    readonly Func<double> _random;
    readonly Dictionary<PlacedObject, UnitCombatState> _units = new(ReferenceEqualityComparer.Instance);
    readonly List<Shot> _shots = new();
    double _pending;

    public CombatWorld(
        UnitPoses poses,
        IEnumerable<CombatUnit> units,
        double shieldDamageProtection,
        Func<double> random,
        double systemHitpointsBonus = 0
    )
    {
        _poses = poses;
        _protection = shieldDamageProtection;
        _random = random;
        foreach (var u in units)
        {
            _units[u.Unit] = new UnitCombatState(
                u.Unit,
                u.Profile,
                u.DefinitionName,
                u.Radius,
                u.Mobile,
                systemHitpointsBonus
            );
        }
    }

    // The shots in flight.
    public IReadOnlyList<Shot> Shots => _shots;

    // The units still in the fight.
    public IEnumerable<UnitCombatState> Units => _units.Values;

    // The state of a unit; null once it is destroyed or when it is no unit of the world.
    public UnitCombatState? State(PlacedObject unit) => _units.GetValueOrDefault(unit);

    // Gives `attacker` an attack order on `target`, replacing its order. Refused (false) when either is gone, the
    // target is not hostile to the attacker, or the attacker has no weapons.
    public bool Attack(PlacedObject attacker, PlacedObject target)
    {
        if (State(attacker) is not { } a || State(target) is not { } t)
            return false;
        if (!CombatRules.Hostile(a.Team, t.Team) || a.Weapons.Count == 0)
            return false;
        a.Order = t;
        a.AutoOrder = false;
        a.AutoMove = false;
        a.PlayerMove = null;
        a.Intent = default;

        return true;
    }

    // Ends the unit's attack order and its move order.
    public void ClearOrder(PlacedObject unit)
    {
        if (State(unit) is not { } state)
            return;
        state.Order = null;
        state.AutoOrder = false;
        state.AutoMove = false;
        state.PlayerMove = null;
        state.Intent = default;
    }

    // Gives the unit a move order to the point (X, Z), replacing its attack order. The unit is not idle until it
    // arrives, however the motion gets there (a ship turns in place first without changing its position).
    public void Move(PlacedObject unit, float x, float z)
    {
        ClearOrder(unit);
        if (State(unit) is { } state)
        {
            state.PlayerMove = (x, z);
        }
    }

    // Advances the fight by `seconds` and returns the events of the ticks that ran.
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
        AdvanceShots(events);
        foreach (var u in _units.Values.ToList())
        {
            if (u.Destroyed)
                continue;
            Think(u);
            if (u.Mobile && u.Immobile)
            {
                u.Intent = new MoveIntent(MoveKind.Stop); // no order moves a derelict ship or one with dead engines
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
            u.AutoOrder = false;
            u.Intent = u.Mobile ? new MoveIntent(MoveKind.Stop) : default;
        }

        // stand-in: what red gave the unit ends with red, the auto order and the walk toward an attacker (a
        // player's order stays); the game's behavior is not known.
        if (u.Alert != AlertStance.Red && (u.AutoOrder || u.AutoMove))
        {
            if (u.AutoOrder)
            {
                u.Order = null;
            }

            u.AutoOrder = u.AutoMove = false;
            u.Intent = u.Mobile ? new MoveIntent(MoveKind.Stop) : default;
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
            if (u.Target != null)
            {
                u.Order = u.Target; // idle at red: attack the pick
                u.AutoOrder = true;
                u.AutoMove = false;
                u.Attacker = null;
            }
            else if (u.Mobile && u.Attacker is { } attacker && Valid(u, attacker))
            {
                var at = Position(attacker); // else go to where the unit that hit it is
                u.Intent = new MoveIntent(MoveKind.MoveTo, at.X, at.Z);
                u.AutoMove = true;

                return;
            }
            else
            {
                u.Attacker = null;
                if (u.AutoMove)
                {
                    u.Intent = new MoveIntent(MoveKind.Stop);
                }

                u.AutoMove = false;

                return;
            }
        }

        var order = u.Order!;
        var distance = Surface(u, order);
        if (u.Mobile)
        {
            var there = Position(order);
            // stand-in: the game's attack maneuver was not traced; the unit follows its target to the closing
            // range, then holds position and turns toward it.
            u.Intent = new MoveIntent(distance > range - 50 ? MoveKind.MoveTo : MoveKind.Face, there.X, there.Z);
        }

        if (distance <= range)
        {
            u.Target = order;

            return;
        }

        // An ordered target out of range: the weapons fire at the unit's own pick (none at green).
        u.Target = u.Alert == AlertStance.Green ? null : Select(u, held, radius);
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
            if (u.Derelict || u.SystemDestroyed(SystemNames.Weapons) || u.Target is not { } target)
            {
                weapon.Cooldown = Math.Max(0, weapon.Cooldown); // an idle weapon does not store shots

                continue;
            }

            var delay = CombatRules.ReloadDelay(weapon.Profile, u);
            // A delay below a tick fires several rounds in one tick; a delay of zero fires one round per tick. A
            // round is one shot per hardpoint, so a weapon without hardpoints fires nothing.
            while (weapon.Cooldown <= Epsilon)
            {
                foreach (var hardpoint in weapon.Profile.Hardpoints)
                {
                    events.Add(new ShotFired(Launch(u, weapon.Profile, target, hardpoint)));
                }

                weapon.Cooldown += delay;
                if (delay <= Epsilon)
                {
                    weapon.Cooldown = 0;

                    break;
                }
            }
        }
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
        // stand-in: the world's shot flies from the shooter's center (it knows no model); the view offsets what it
        // draws to the shot's hardpoint.
        var from = Position(shooter);
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
        target.Attacker = shot.Shooter;
        events.Add(new ShotLanded(shot, hit));
        if (!hit.Destroyed)
            return;
        _units.Remove(target.Unit);
        _shots.RemoveAll(s => s.Target == target);
        target.Order = null;
        target.Target = null;
        events.Add(new UnitDestroyed(target.Unit));
    }
}
