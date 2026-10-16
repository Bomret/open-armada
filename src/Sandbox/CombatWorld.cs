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
    const double Epsilon = 1e-9;

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
        Func<double> random
    )
    {
        _poses = poses;
        _protection = shieldDamageProtection;
        _random = random;
        foreach (var u in units)
            _units[u.Unit] = new UnitCombatState(u.Unit, u.Profile, u.DefinitionName, u.Radius, u.Mobile);
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
        a.Intent = default;
        return true;
    }

    // Ends the unit's attack order; a move order replaces it.
    public void ClearOrder(PlacedObject unit)
    {
        if (State(unit) is not { } state)
            return;
        state.Order = null;
        state.Intent = default;
    }

    // Advances the fight by `seconds` and returns the events of the ticks that ran.
    public IReadOnlyList<CombatEvent> Step(double seconds)
    {
        var events = new List<CombatEvent>();
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
        foreach (var u in _units.Values)
        {
            var at = Position(u);
            u.Stopped = u.LastPosition == null || u.LastPosition == at;
            u.LastPosition = at;
        }
        AdvanceShots(events);
        foreach (var u in _units.Values.ToList())
        {
            if (u.Destroyed)
                continue;
            Think(u);
            Fire(u, events);
            CombatRules.RegainShields(u, Tick);
        }
    }

    Vector3 Position(UnitCombatState u) => _poses.Pose(u.Unit).Position;

    // The distance between the units' surfaces.
    float Surface(UnitCombatState a, UnitCombatState b) =>
        Vector3.Distance(Position(a), Position(b)) - a.Radius - b.Radius;

    static float LongestRange(UnitCombatState u) => u.Profile.Weapons.Max(w => w.Range);

    // Sets the unit's movement and the target its weapons fire at.
    void Think(UnitCombatState u)
    {
        u.Target = null;
        if (u.Weapons.Count == 0)
            return;
        if (u.Order is { Destroyed: true })
        {
            u.Order = null;
            u.Intent = u.Mobile ? new MoveIntent(MoveKind.Stop) : default;
        }
        if (u.Order is not { } order)
            return;
        var range = LongestRange(u);
        var distance = Surface(u, order);
        if (u.Mobile)
        {
            var there = Position(order);
            // stand-in: the game's attack maneuver was not traced; the unit follows its target to the closing
            // range, then holds position and turns toward it.
            u.Intent = new MoveIntent(distance > range - 50 ? MoveKind.MoveTo : MoveKind.Face, there.X, there.Z);
        }
        u.Target = distance <= range ? order : NearestHostile(u, range);
    }

    // The nearest hostile unit whose surface is within `range`; null when none is.
    UnitCombatState? NearestHostile(UnitCombatState u, float range) =>
        _units
            .Values.Where(t => !t.Destroyed && CombatRules.Hostile(u.Team, t.Team))
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
            if (u.Target is not { } target)
            {
                weapon.Cooldown = Math.Max(0, weapon.Cooldown); // an idle weapon does not store shots
                continue;
            }
            if (weapon.Cooldown > Epsilon)
                continue;
            for (var i = 0; i < Math.Max(1, weapon.Profile.Hardpoints.Count); i++)
                events.Add(new ShotFired(Launch(u, weapon.Profile, target)));
            weapon.Cooldown += CombatRules.ReloadDelay(weapon.Profile, u);
        }
    }

    // One shot of the weapon at the target, with its to-hit rolled now. A shot that missed is reported and not kept.
    Shot Launch(UnitCombatState shooter, WeaponProfile weapon, UnitCombatState target)
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
        var hits = _random() <= chance;
        var from = Position(shooter);
        var toward = Position(target) - from;
        var kind = weapon.Ordnance.ClassLabel switch
        {
            "phaser" => ShotKind.Beam,
            "pphaser" => ShotKind.Pulse,
            _ => ShotKind.Torpedo, // stand-in: poleron torpedoes and unknown classes fly as torpedoes
        };
        var shot = new Shot(
            kind,
            shooter,
            target,
            weapon,
            hits,
            from,
            toward == Vector3.Zero ? Vector3.UnitZ : Vector3.Normalize(toward)
        );
        if (hits)
            _shots.Add(shot);
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
                if (shot.Age + Epsilon < ordnance.LifeSpan)
                    continue;
                _shots.Remove(shot);
                if (!shot.Shooter.Destroyed && Surface(shot.Shooter, shot.Target) <= 2 * shot.Weapon.Range)
                    Land(shot, events);
                continue;
            }
            if (shot.Kind == ShotKind.Torpedo && shot.Age <= ordnance.SeekTime)
                shot.Direction = Steer(shot, Tick);
            var step = shot.Direction * ordnance.ShotSpeed * (float)Tick;
            var reach = shot.Target.Shields > 0 ? shot.Target.Radius * BubbleRadii : shot.Target.Radius;
            if (SegmentDistance(shot.Position, shot.Position + step, Position(shot.Target)) <= reach)
            {
                _shots.Remove(shot);
                Land(shot, events);
            }
            else if (shot.Age + Epsilon >= ordnance.LifeSpan)
                _shots.Remove(shot); // expires without damage
            else
                shot.Position += step;
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
        var location = CombatRules.Location(target.Profile, Math.Min(99, (int)(_random() * 100)));
        var hit = CombatRules.Apply(target, shot.Weapon.Ordnance, damage, location, _protection);
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
