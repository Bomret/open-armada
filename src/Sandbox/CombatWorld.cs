using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using OpenArmada.Diagnostics;
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

    /// <summary>
    /// The radius of the unit's model's bounding sphere (model +0x24, copied to the object's +0x60 by 0x4510a0), which
    /// sizes its repair sparks; 0 for a unit without a model.
    /// </summary>
    public float ModelRadius { get; init; }

    /// <summary>Whether the unit can move at all.</summary>
    public required bool Mobile { get; init; }

    /// <summary>
    /// The unit's motion, which the world steps on its ticks and gives the unit's orders to; null for a unit whose
    /// pose something else drives. A mobile unit has one from the moment the world opens.
    /// </summary>
    public ShipMotion? Motion { get; init; }

    /// <summary>
    /// The unit's model as the game loads it (at the class's scale), whose faces become debris pieces when the unit
    /// ends; null for a unit without a readable model, which ends without debris.
    /// </summary>
    public Sod? Model { get; init; }

    /// <summary>The unit class's shield ellipsoid; null for a model that has none (it then shows no shield hit effect).</summary>
    public ShieldEllipsoid? Shield { get; init; }
}

/// <summary>
/// The fight of a map in plain C#: every unit's combat state, the orders, and the shots in flight. Step advances
/// it and returns what happened. The world owns each mobile unit's motion (<see cref="CombatUnit.Motion"/>) and
/// steps it on its fixed ticks, recording the pose in the pose source; a unit without motion keeps the pose the
/// pose source gives it. Randomness comes from the injected source, a number in [0, 1) per call.
/// </summary>
public sealed class CombatWorld
{
    // stand-in: the world runs on fixed ticks of this many seconds, and a step carries the time left over to the
    // next, so one long step does what many short ones over the same time do; the game's own update rate is not
    // known.
    /// <summary>The length of one world tick in seconds.</summary>
    public const double Tick = 0.02;

    const double Epsilon = 1e-9;

    /// <summary>The game's compiled default of <c>MAX_PREVENTION_ROTATION</c> (0x4453f0), in radians.</summary>
    public const float DefaultMaxPreventionRotation = 0.4f;

    // A ship's separation pass looks at the ships within this times its model radius on X and on Z (0x464910).
    const float NeighbourBox = 3f;

    // A beam shows its effect once this share of its lifetime has passed: 1 - 0.9 (0x6173e8).
    const double BeamEffectStart = 0.1;

    // The hull hit sprite of a beam is drawn at this times the ordnance's size (0x6173dc, 0x6173e0).
    const float HullSpriteScale = 2f;
    const float ArrivedWithin = 1e-3f;

    // An idle unit at red closes on its pick only while its center is within 300 units of its tether point, in
    // 3D (0x42a3a9, the constant at 0x5ca7ac).
    const float TetherSquared = 90000f;

    // A hit counts as recent for less than this many seconds (0x46e640, the 1.0 at 0x42a14a).
    const double HitWindow = 1.0;

    // A hit on a unit of the same team within this many units on X and on Z counts too (0x42a000); stand-in: the
    // edge counts.
    const float TeammateBox = 400f;

    // A dying unit has a small explosion every 0.05 s (0x600784) of one of systemexpl1 to systemexpl7.
    const double SmallExplosionEvery = 0.05;
    const int SmallExplosionNames = 7;

    // A repair spark's size is this times the unit's model radius (0x600794).
    const float SparkScale = 0.14f;

    // A hit explosion's size is this times the unit's model radius (0x60076c), and at least the least (0x600770).
    const float HitExplosionScale = 0.14f;
    const float HitExplosionLeast = 4f;

    readonly UnitPoses _poses;
    readonly double _protection;
    readonly Func<double> _random;
    readonly Func<PlacedObject, string, ShipPose, Vector3> _hardpointPosition;
    readonly Dictionary<PlacedObject, UnitCombatState> _units = new(ReferenceEqualityComparer.Instance);
    readonly List<Shot> _shots = new();
    readonly DebrisField _debris;
    readonly Dictionary<PlacedObject, Sod?> _models = new(ReferenceEqualityComparer.Instance);
    readonly HashSet<string> _faults = new();

    // The units whose non-finite push is reported: by reference, since labels can repeat.
    readonly HashSet<PlacedObject> _badPushes = new(ReferenceEqualityComparer.Instance);
    double _pending;
    long _ticks;

    /// <summary>
    /// <c>MAX_PREVENTION_ROTATION</c>, the largest angle of a collision turn in radians; the game's compiled default
    /// unless the combat plan read the install's.
    /// </summary>
    public float MaxPreventionRotation { get; init; } = DefaultMaxPreventionRotation;

    /// <summary>
    /// The asteroid fields and planets that push a ship out of them in the post-simulate pass (0x4644b0), every ship
    /// that is not derelict and not dying, coasting ones included; none unless the plan gives some.
    /// </summary>
    public IReadOnlyList<Obstacle> Obstacles { get; init; } = Array.Empty<Obstacle>();

    /// <summary>
    /// The black holes of the map: each swallows the units inside its event horizon before the motions of a tick,
    /// pulls the ships without thrust after them, and pushes the ships with thrust out of its ring in the
    /// post-simulate pass; none unless the plan gives some.
    /// </summary>
    public IReadOnlyList<BlackHole> BlackHoles { get; init; } = Array.Empty<BlackHole>();

    /// <summary>Seconds of fight the ticks that ran add up to.</summary>
    public double Time => _ticks * Tick;

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
        double systemHitpointsBonus = 0,
        DebrisLimits? debris = null
    )
    {
        _debris = new DebrisField(debris ?? DebrisLimits.Default);
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
            )
            {
                Shield = u.Shield,
                ModelRadius = u.ModelRadius,
                Motion = u.Mobile ? u.Motion : null,
            };
            if (u.Unit.Start is { } start)
            {
                state.Begin(start);
            }

            if (state.Motion is { } motion && state.Coasting)
            {
                // a unit that starts without thrust coasts from the first tick, along its starting velocity
                motion.Coast();
                if (u.Unit.Start?.Velocity is { } velocity)
                {
                    motion.SetVelocity(velocity);
                }

                state.Velocity = motion.Velocity;
                state.Stopped = state.Velocity == Vector3.Zero;
            }

            _units[u.Unit] = state;
            _models[u.Unit] = u.Model;
        }
    }

    /// <summary>The live debris pieces, the oldest first.</summary>
    public IReadOnlyList<DebrisPiece> Debris => _debris.Pieces;

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
        if (State(attacker) is not { } a || State(target) is not { } t || a.Dying || t.Dying)
            return false;
        if (!CombatRules.Hostile(a.Team, t.Team) || a.Weapons.Count == 0)
            return false;
        a.Order = t;
        a.AutoMove = false;
        a.Returning = false;
        a.PlayerMove = null;
        a.Intent = default;

        return true;
    }

    /// <summary>
    /// The one route of every forced push of a ship (collision separation, push-out): the ship's motion moves it by
    /// <c>displacement</c> (<see cref="ShipMotion.Push"/>), its pose, position, and velocity follow at once, and the
    /// arrival test runs. A move order, or the hold's return to the tether point, whose point lies strictly under the
    /// ship's tooCloseToTurn away after the push ends where the ship stands: the order and the motion's target
    /// end, and that spot, its height included, becomes the tether point (0x428170 posts STOP inside the radius, and
    /// 0x46eac0 makes the stopping spot the path). A ship at or beyond the radius keeps its order. A ship without
    /// thrust, a ship with an attack order, and a unit without motion end no order. A displacement with a component
    /// that is no finite number skips the push and is reported as one ERROR entry, the first time for that unit.
    /// </summary>
    public void Push(PlacedObject unit, Vector3 displacement)
    {
        if (State(unit) is not { Destroyed: false, Motion: { } motion } u)
            return;
        if (!float.IsFinite(displacement.X) || !float.IsFinite(displacement.Y) || !float.IsFinite(displacement.Z))
        {
            // each unit's non-finite push is reported once, however often it recurs (INV-0002)
            if (_badPushes.Add(unit))
            {
                Log.Error(
                    Area.Combat,
                    $"push of {unit.Label} skipped: displacement {displacement} is no finite vector"
                );
            }

            return;
        }

        motion.Push(displacement);
        u.Position = _poses.Advance(unit, motion, 0).Position;
        u.Velocity = motion.Velocity;
        if (u.Coasting || u.Order != null)
            return;
        var at = u.Position.Value;
        var point =
            u.PlayerMove is { } to ? new Vector3(to.X, u.Tether.Y, to.Z)
            : u.Returning ? u.Tether
            : (Vector3?)null;
        // stand-in (ADR 0009): the game also asks the ship to be slower than half its impulseSpeed, which its brake
        // brings about; the kinematic model has no brake, so a push inside the radius stands for slow and near.
        if (point is { } p && Vector3.Distance(at, p) < motion.TooCloseToTurn)
        {
            u.PlayerMove = null;
            u.Returning = false;
            u.Intent = default;
            motion.Stop();
            _poses.Advance(unit, motion, 0); // the stop drops the bank
            u.Tether = at;
            u.Velocity = motion.Velocity;
            u.Stopped = u.Velocity == Vector3.Zero;
        }
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
        state.Returning = false;
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
            if (state.Motion is { } motion && !state.Immobile)
            {
                motion.Thrust();
                motion.SetTarget(x, z);
            }
        }
    }

    /// <summary>
    /// The game's group move (<see cref="Formation"/>): the ships of <c>ships</c> (in selection order) that take a
    /// move order are laid on the group formation around <c>click</c> (the world's X, Z, already inside the map), and
    /// each gets its own move order to its cell (<see cref="Move"/>). A station, a ship that cannot move, a derelict,
    /// dying or coasting ship takes none and leaves the grid as it was. One ship that takes it goes to the click.
    /// <c>spacing</c> is FORMATION_SPACING and <c>midY</c> the middle of the combat band, which the cells are matched
    /// at. A cell outside <c>bounds</c> goes to the nearest point of the map's edge, as a single click does.
    /// </summary>
    /// <remarks>
    /// stand-in: the leader's move order and the followers' FOLLOW orders (cmd 9, 0x480c30's pipeline, 0x4805d0) reduce
    /// to one move order per cell, which is what they come to with stock DEFAULT_TO_FORMATION_MOVE = 0 and the
    /// sandbox's one-point targets; followers start at once, not two game ticks after the leader; each ship keeps its
    /// own height (height steering belongs to the flight-model feature, ADR 0009); two clamped cells may coincide.
    /// </remarks>
    public void GroupMove(IReadOnlyList<PlacedObject> ships, Vector2 click, float spacing, float midY, MapBounds bounds)
    {
        var taking = ships.Where(s => State(s) is { Mobile: true, Immobile: false }).ToList();
        if (taking.Count == 1)
        {
            Move(taking[0], click.X, click.Y);

            return;
        }

        // the formation is the game's, in its frame: Z mirrored from the world's. Its heading is the mean over every
        // ship handed to the move, those that take no order included (0x47fde0).
        var gameClick = new Vector2(click.X, -click.Y);
        var heading = Formation.Heading(
            ships.Where(s => State(s) is not null).Select(s => InGameFrame(Position(State(s)!))).ToList(),
            gameClick
        );
        var cells = Formation.Destinations(
            taking.Select(s => InGameFrame(Position(State(s)!))).ToList(),
            gameClick,
            spacing,
            midY,
            heading
        );
        for (var i = 0; i < taking.Count; i++)
        {
            var cell = MoveGeometry.ClampToMap(new Vector2(cells[i].X, -cells[i].Y), bounds);
            Move(taking[i], cell.X, cell.Y);
        }
    }

    static Vector3 InGameFrame(Vector3 world) => world with { Z = -world.Z };

    /// <summary>
    /// The seconds of the latest Step call after its last tick: the part of a tick the motions have not run yet,
    /// which the view draws ahead (<see cref="ShipMotion.Drawn"/>).
    /// </summary>
    public double Pending => _pending;

    /// <summary>Advances the fight by <c>seconds</c> and returns the events of the ticks that ran.</summary>
    public IReadOnlyList<CombatEvent> Step(double seconds)
    {
        var events = new List<CombatEvent>();
        // A unit without motion has its pose driven from outside, which changes once per Step call (a frame): it is
        // stopped when it did not move since the previous call, and its velocity is that change over the call. Every
        // tick of the call shares that, so the to-hit does not depend on the frame rate.
        foreach (var u in _units.Values)
        {
            var at = _poses.Pose(u.Unit).Position;
            if (u.Motion == null)
            {
                u.Stopped = u.Position == null || u.Position == at;
                if (seconds > 0 && u.Position is { } before)
                {
                    u.Velocity = (at - before) / (float)seconds;
                }
            }

            u.Position = at;
            EndArrivedMove(u);
        }

        _pending += Math.Max(0, seconds);
        while (_pending + Epsilon >= Tick)
        {
            _pending -= Tick;
            RunTick(events);
        }

        return events;
    }

    // A ship that arrives stands exactly on its target (ShipMotion), which ends the move order.
    static void EndArrivedMove(UnitCombatState u)
    {
        if (
            u.PlayerMove is { } to
            && u.Position is { } at
            && Math.Abs(at.X - to.X) < ArrivedWithin
            && Math.Abs(at.Z - to.Z) < ArrivedWithin
        )
        {
            u.PlayerMove = null;
        }
    }

    // One tick of every unit's motion: it takes the thrust away or gives the unit's orders to it, runs for the tick,
    // and the unit's pose, velocity, and move order follow. Done for all units before any of them thinks or fires,
    // so each reads the same positions and velocities. A move order ends here, in the step that reaches its target.
    void RunMotions()
    {
        foreach (var u in _units.Values.Where(u => u.Motion != null))
        {
            var motion = u.Motion!;
            if (u.Coasting)
            {
                motion.Coast();
            }
            else
            {
                motion.Thrust();
                switch (u.Intent.Kind)
                {
                    case MoveKind.MoveTo:
                        motion.SetTarget(u.Intent.X, u.Intent.Z);

                        break;
                    case MoveKind.Face:
                        motion.Face(u.Intent.X, u.Intent.Z);

                        break;
                    case MoveKind.Stop:
                        motion.Stop();

                        break;
                }
            }

            u.Position = _poses.Advance(u.Unit, motion, Tick).Position;
            u.Velocity = motion.Velocity;
            u.Stopped = u.Velocity == Vector3.Zero; // stand-in: stopped is no velocity at all; the game's test was not traced
            EndArrivedMove(u);
        }
    }

    // The event horizon of the black holes (the game's area step, 0x44da40, before the simulate step): every unit that is
    // not dying, ship or station, whose center lies within EventRadius of the hole's center in the plane (the edge
    // included) is swallowed (0x450a40). It becomes a dying unit with a timer of 0 (0x466090), so it leaves in the next
    // tick through RunDying, with no fireball and no small explosions. A hole whose EventRadius is 0 swallows nothing
    // (0x44da40). A fault in one unit's test is reported once per cause and the run goes on.
    void SwallowUnits()
    {
        foreach (var hole in BlackHoles)
        {
            if (hole.EventRadius == 0) // the game skips the horizon of such a hole (0x44da40)
                continue;
            var reach = hole.EventRadius * hole.EventRadius;
            foreach (var u in _units.Values)
            {
                if (u.Dying || u.Destroyed)
                    continue;
                try
                {
                    var at = Position(u);
                    var (dx, dz) = (at.X - hole.Center.X, at.Z - hole.Center.Z);
                    if (dx * dx + dz * dz > reach)
                        continue;
                    u.Dying = true;
                    u.DyingRemaining = 0;
                    u.Swallowed = true;
                }
                catch (Exception e)
                {
                    if (_faults.Add(e.Message))
                    {
                        Log.Error(Area.Combat, $"black-hole event horizon on {u.Unit.Label} failed: {e.Message}");
                    }
                }
            }
        }
    }

    // The pull of the black holes (0x450a80), after the motions of the tick (the game's simulate step; the order of holes
    // and ships inside it is not recovered, so holes run in the plan's order and ships in the unit order). A hole pulls
    // every ship that moves and has no thrust (engines destroyed or derelict, dying or not; 0x450bcc) whose 3D distance d from its center is within
    // the gravity radius R: the velocity changes by the unit direction to the center times F times the tick, with F
    // = GravityForce x R^2 / d^2 (R^2 when d^2 < 1). The result is held to the ship's speed cap (0x450f40, 0x450ea0).
    // stand-in: the cap is impulseSpeed alone (no warp multiplier). A ship at the exact center has no direction and
    // keeps its velocity. A fault in one ship's pull is reported once per cause and the run goes on.
    void PullShips()
    {
        foreach (var hole in BlackHoles)
        {
            var reach = hole.GravityRadius * hole.GravityRadius;
            foreach (var s in _units.Values)
            {
                if (!s.Mobile || !(s.Derelict || s.SystemDestroyed(SystemNames.Engines)) || s.Motion is not { } motion)
                    continue;
                try
                {
                    var to = hole.Center - Position(s);
                    var squared = to.LengthSquared();
                    if (!(squared <= reach) || squared == 0)
                        continue;
                    // simplicity: the force is computed in double and the change held at 1e18, so a huge but finite force or
                    // radius cannot overflow the float velocity to infinity; the cap below makes the result the same.
                    var force =
                        (double)hole.GravityForce * hole.GravityRadius * hole.GravityRadius / Math.Max(squared, 1);
                    var change = (float)Math.Min(force * Tick, 1e18);
                    motion.AddVelocity(to / MathF.Sqrt(squared) * change);
                    var v = motion.Velocity;
                    var cap = (float)motion.ImpulseSpeed;
                    if (v.Length() is var speed && speed >= cap && speed > 0)
                    {
                        motion.SetVelocity(v / speed * cap);
                    }

                    s.Velocity = motion.Velocity;
                    s.Stopped = s.Velocity == Vector3.Zero;
                }
                catch (Exception e)
                {
                    if (_faults.Add(e.Message))
                    {
                        Log.Error(Area.Combat, $"black-hole pull on {s.Unit.Label} failed: {e.Message}");
                    }
                }
            }
        }
    }

    // The collision separation of the post-simulate pass (0x464910): each ship that is not dying, not derelict, and has
    // a motion (a station has none, so it runs no pass) separates from the ships in the box around it, in the plan's
    // unit order. A neighbour is any other unit that is not dying; a station never moves. A fault in a pass is
    // reported once per cause and the pass of the next ship runs (INV-0002).
    void SeparateShips()
    {
        var all = _units.Values.ToList();
        foreach (var a in all)
        {
            if (a.Destroyed || a.Dying || a.Derelict || a.Motion == null)
                continue;
            try
            {
                SeparateFrom(a, all);
            }
            catch (Exception e) // a fault of one ship's pass is reported, and the run goes on
            {
                if (_faults.Add(e.Message))
                {
                    Log.Error(Area.Combat, $"collision separation of {a.Unit.Label} failed: {e.Message}");
                }
            }
        }
    }

    void SeparateFrom(UnitCombatState a, List<UnitCombatState> all)
    {
        // the neighbors are gathered once before the pass, as the game does; the contacts then read the live positions
        var (start, reach) = (Position(a), NeighbourBox * a.ModelRadius);
        var neighbours = all.Where(b =>
                b != a
                && !b.Destroyed
                && !b.Dying
                && Math.Abs(start.X - Position(b).X) <= reach
                && Math.Abs(start.Z - Position(b).Z) <= reach
            )
            .ToList();
        foreach (var b in neighbours)
        {
            var (at, other) = (Position(a), Position(b));
            var contact = CollisionSeparation.Between(
                new CollisionSeparation.Body(at, a.ModelRadius, a.Shield),
                new CollisionSeparation.Body(other, b.ModelRadius, b.Shield)
            );
            if (contact is not { } c)
                continue;
            var (shareA, shareB) = CollisionSeparation.Shares(a.Velocity.Length(), b.Velocity.Length());
            var aBorg = a.Motion!.BorgPhysics; // SeparateShips runs no pass for a unit without motion
            var bBorg = b.Motion?.BorgPhysics ?? false;
            var v = CollisionSeparation.Push(c.Normal, at.Y, other.Y, aBorg, bBorg);
            Separate(a, b, shareA, v, c);
            Separate(b, a, shareB, -v, c);
        }

        // stand-in: every obstacle is tested, not only those in the neighbour box (how the game's grid holds a
        // large non-ship object is not recovered), and after the ship contacts, not in the neighbours' order. The
        // field and planet branch of 0x4644b0 has no engine test: a coasting ship (engines destroyed) is pushed out too.
        foreach (var obstacle in Obstacles)
        {
            PushOut(a, obstacle.Center, obstacle.Radius);
        }

        // The black hole's ring of 1.5 x EventRadius takes a ship with thrust only: engines working and not derelict
        // (0x4644b0's black-hole branch); a ship the pass runs for is never dying, so no thrust means engines destroyed.
        if (!a.Coasting)
        {
            foreach (var hole in BlackHoles)
            {
                PushOut(a, hole.Center, hole.Ring);
            }
        }
    }

    // The push-out of a ship from the sphere of `radius` around `center` in the plane (0x4644b0), through the push
    // route. The caller gates on engines: the black hole's ring of 1.5 x EventRadius takes ships with thrust only.
    void PushOut(UnitCombatState s, Vector3 center, float radius)
    {
        if (s.Destroyed || CollisionSeparation.PushOut(Position(s), s.ModelRadius, center, radius) is not { } by)
            return;
        Push(s.Unit, by);
    }

    // One ship's part of a separation: it moves by its share of v through the push route, then takes the collision
    // turn. A unit without motion (a station) moves in no way, and its share is lost.
    void Separate(UnitCombatState s, UnitCombatState o, float share, Vector3 v, CollisionSeparation.Contact contact)
    {
        if (s.Motion is not { } motion)
            return;
        Push(s.Unit, v * share);
        if (contact.Coincide || s.Destroyed)
            return;
        var turn = CollisionSeparation.Turn(
            motion.Pose.Heading,
            Position(s),
            Position(o),
            share,
            motion.BorgPhysics,
            v,
            contact.Penetration,
            MaxPreventionRotation
        );
        if (turn is { } delta)
        {
            motion.Yaw(delta);
            _poses.Advance(s.Unit, motion, 0);
            s.Velocity = motion.Velocity;
        }
    }

    void RunTick(List<CombatEvent> events)
    {
        _ticks++;
        _debris.Advance(Tick); // a piece made in this tick first moves in the next
        // the dying phase runs before the shots land, so a unit a hit dooms in this tick starts its timer in the next
        foreach (var u in _units.Values.Where(u => u.Dying).ToList())
        {
            RunDying(u, events);
        }

        AdvanceShots(events);
        SwallowUnits();
        RunMotions();
        PullShips();
        SeparateShips();
        foreach (var u in _units.Values.ToList())
        {
            if (u.Destroyed)
                continue;
            Think(u);
            if (u.Coasting)
            {
                u.Intent = new MoveIntent { Kind = MoveKind.Stop }; // no order moves a ship without thrust
                u.PlayerMove = null;
                u.Returning = false;
            }

            Fire(u, events);
            CombatRules.RegainShields(u, Tick);
            CombatRules.Sustain(u, Tick);
            Spark(u, events); // before the repair of the tick, as 0x4683d0 sparks before it adds hit points
            CombatRules.Repair(u, Tick);
        }
    }

    // The repair sparks of the unit's systems for one tick (0x4683d0). Each system below its maximum hit points runs its
    // spark clock. Once the clock passes the spark time, it drops by the spark time until it no longer passes, and one
    // spark shows at a target hardpoint picked at random (0x5018c0). A system without target hardpoints runs its
    // clock and shows none. A derelict or dying unit repairs nothing, so it sparks nothing either (CombatRules.Repair).
    // stand-in: the game's repair also waits out a disable timer and a held flag (system +0x2c, +0x30), which the
    // sandbox does not have; a spark time of 0 or less sparks once per tick, where the game's drop loop never ends.
    void Spark(UnitCombatState u, List<CombatEvent> events)
    {
        if (u.Derelict || u.Dying)
            return;
        foreach (var s in u.Systems.Values.Where(s => s.HitPoints < s.Max))
        {
            var every = s.Profile.SparkTime;
            s.SparkClock += Tick;
            if (s.SparkClock - every <= Epsilon)
                continue;
            s.SparkClock = every > 0 ? s.SparkClock - every * Math.Floor((s.SparkClock - Epsilon) / every) : 0;
            var hardpoints = s.Profile.TargetHardpoints;
            if (hardpoints.Count == 0)
                continue;
            var index = Math.Min(hardpoints.Count - 1, (int)(_random() * hardpoints.Count));
            events.Add(
                new RepairSpark
                {
                    Unit = u.Unit,
                    System = s.Profile.Name,
                    Hardpoint = hardpoints[index],
                    Size = u.ModelRadius * SparkScale,
                }
            );
        }
    }

    // The explosion of a hit that passed damage to the systems (0x465100): one of systemexpl1 to systemexpl7, picked at
    // random (0x5018c0), at the hit's node. The node is the target hardpoint of the hit location nearest to the
    // firing hardpoint, the first listed winning a tie (0x468a60); a hull or critical hit, or a location without
    // target hardpoints, takes the model's root node (the class load never fills the hull and critical lists).
    // stand-in: the game rolls the location and picks the node when the shot is made (0x50ae40, 0x4ee710); the
    // sandbox does both when it lands, from where the firing hardpoint is then.
    HitExplosion ExplosionOf(Shot shot, HitLocation location)
    {
        var target = shot.Target;
        string? node = null;
        if (
            location is not (HitLocation.Hull or HitLocation.Critical)
            && target.Systems.TryGetValue(SystemNames.All[(int)location], out var system)
        )
        {
            var from = HardpointAt(shot.Shooter, shot.Hardpoint);
            var nearest = float.MaxValue;
            foreach (var name in system.Profile.TargetHardpoints)
            {
                var distance = Vector3.DistanceSquared(HardpointAt(target, name), from);
                if (distance < nearest)
                {
                    nearest = distance;
                    node = name;
                }
            }
        }

        var index = Math.Min(SmallExplosionNames - 1, (int)(_random() * SmallExplosionNames));

        return new HitExplosion
        {
            Unit = target.Unit,
            Name = "systemexpl" + (index + 1),
            Hardpoint = node,
            Size = Math.Max(target.ModelRadius * HitExplosionScale, HitExplosionLeast),
        };
    }

    // One of the seven small explosions at a random point of the unit (table 0x600750, picked by 0x466320).
    // stand-in: the point is uniform in the box of the unit's radius around its center; 0x466180 is not recovered.
    SmallExplosion SmallExplosionOn(UnitCombatState u)
    {
        var index = Math.Min(SmallExplosionNames - 1, (int)(_random() * SmallExplosionNames));
        float Axis() => (float)((_random() - 0.5) * 2 * u.Radius);

        return new SmallExplosion
        {
            Unit = u.Unit,
            Name = "systemexpl" + (index + 1),
            Offset = new Vector3(Axis(), Axis(), Axis()),
        };
    }

    // One tick of a dying unit: the timer runs, and when it is out the unit leaves the world (0x4663d0).
    void RunDying(UnitCombatState u, List<CombatEvent> events)
    {
        u.ExplosionClock += Tick;
        while (u.ExplosionClock + Epsilon >= SmallExplosionEvery)
        {
            u.ExplosionClock -= SmallExplosionEvery;
            if (!u.Swallowed) // a swallowed unit shows no small explosions
            {
                events.Add(SmallExplosionOn(u));
            }
        }

        u.DyingRemaining -= Tick;
        if (u.DyingRemaining > Epsilon)
            return;
        u.Dying = false; // the phase is over: the unit is destroyed
        u.Destroyed = true;
        _units.Remove(u.Unit);
        Shatter(u);
        events.Add(
            new UnitDestroyed { Unit = u.Unit, Cause = u.Swallowed ? DestroyCause.BlackHole : DestroyCause.Fireball }
        );
    }

    // The unit ends in pieces: its model's showing faces become debris pieces at the unit's pose (0x58cef0). Whatever ends
    // a unit calls this, with the unit already removed from the world. A unit without a model leaves none.
    void Shatter(UnitCombatState u)
    {
        if (_models.GetValueOrDefault(u.Unit) is { } model)
        {
            _debris.Shatter(u.Unit, model, u, _poses.Pose(u.Unit), _random);
        }
    }

    // The unit's position as of its latest motion tick; for a unit without motion, as of the current Step call.
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
        if (u.Weapons.Count == 0 || u.Dying)
        {
            Hold(u); // a dying unit picks and fires nothing (0x506d70), and holds nothing (it coasts)

            return;
        }

        if (u.Order is { Destroyed: true } or { Dying: true })
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
            {
                Hold(u);

                return; // green picks nothing by itself
            }

            u.Target = Select(u, held, radius);
            // stand-in: a unit is idle when it has no order of the player (no attack order, no move order until it
            // arrives); the game's own idle test was not traced.
            var idle = u.PlayerMove == null;
            if (u.Alert != AlertStance.Red || !u.Profile.CanAttack || u.Derelict || !idle)
            {
                Hold(u); // yellow never moves on its own to a target, but holds its tether point

                return;
            }
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
                u.Returning = false; // closing on a pick keeps precedence over the hold
            }
            else
            {
                Hold(u);

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
    // stand-in: a dying teammate is still in _units, so its hit counts like any other until it leaves (the window ends
    // long before a gone unit would matter); whether the game counts it is not known.
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

    // The hold of an idle mobile ship (no attack order, no move order, with thrust) at every alert stance, with or
    // without weapons: the idle handler runs every tick (0x42a170) and keeps the ship at its tether point by the
    // near-target rule (0x42e4a0). A ship the tether point lies tooCloseToTurn or more away from drives back to it
    // and stops on reaching it; nearer, it stays where it was moved. The return is its own state, so a change of
    // stance does not end it. A push inside the radius ends it as well (<see cref="Push"/>).
    // stand-in: the game's brake inside the radius is not modelled (ADR 0009), so a ship nearer than the radius
    // does not move at all.
    void Hold(UnitCombatState u)
    {
        if (!u.Mobile || u.Coasting || u.PlayerMove != null || u.Order != null)
            return;
        var at = Position(u);
        var reached = Math.Abs(at.X - u.Tether.X) < ArrivedWithin && Math.Abs(at.Z - u.Tether.Z) < ArrivedWithin;
        var tooClose = u.Motion?.TooCloseToTurn ?? PhysicsValues.DefaultTooCloseToTurn;
        if (u.Returning ? !reached : Vector3.Distance(at, u.Tether) >= tooClose)
        {
            u.Intent = new MoveIntent
            {
                Kind = MoveKind.MoveTo,
                X = u.Tether.X,
                Z = u.Tether.Z,
            };
            u.Returning = true;
            u.AutoMove = false;

            return;
        }

        if (u.Returning)
        {
            u.Tether = at; // the return ended here, at the point's height too
        }

        if (u.Returning || u.AutoMove)
        {
            u.Intent = new MoveIntent { Kind = MoveKind.Stop };
        }

        u.Returning = false;
        u.AutoMove = false;
    }

    // A valid target is hostile, not being destroyed (dying or gone), and not derelict (0x42faa0).
    // stand-in: every unit is visible to every team until fog of war exists, and the extra check the game makes on
    // yellow candidates (meaning unknown) is skipped.
    static bool Valid(UnitCombatState u, UnitCombatState t) =>
        !t.Destroyed && !t.Dying && !t.Derelict && CombatRules.Hostile(u.Team, t.Team);

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
                // The beam ends without damage as soon as its shooter is gone or dying (0x4ee5f0, 0x4eb170) or its target is beyond twice the range.
                if (
                    shot.Shooter.Destroyed
                    || shot.Shooter.Dying
                    || Surface(shot.Shooter, shot.Target) > 2 * shot.Weapon.Range
                )
                {
                    _shots.Remove(shot);
                }
                else
                {
                    // a tenth of the lifetime in: the beam shows its effect (0x4eb170, 0.9 at 0x6173e8 read as the share
                    // of the lifetime left)
                    var target = shot.Target;
                    if (shot.Age + Epsilon >= BeamEffectStart * ordnance.LifeSpan)
                    {
                        if (target.Shields > 0 && !ordnance.IgnoreShield)
                        {
                            // each tick the line from the hardpoint the beam leaves to the target is tested against
                            // the shield (0x4eb384); a hit raises the effect once and turns it after (0x4eb3d8, 0x4eb3f0)
                            if (
                                ShieldHit(target, HardpointAt(shot.Shooter, shot.Hardpoint), Position(target)) is
                                { } contact
                            )
                            {
                                shot.ShieldDirection = contact.Direction;
                                if (!shot.EffectRaised)
                                {
                                    shot.EffectRaised = true;
                                    events.Add(ShieldEffect(shot, contact));
                                }
                            }
                        }
                        // stand-in: the hull sprite shows once the head is in reach; the game's reach is not recovered
                        else if (!shot.EffectRaised && Surface(shot.Shooter, target) <= shot.Weapon.Range)
                        {
                            shot.EffectRaised = true;
                            events.Add(
                                new HullHitSprite
                                {
                                    Shot = shot,
                                    Unit = target.Unit,
                                    Scale = HullSpriteScale * ordnance.Size,
                                }
                            );
                        }
                    }

                    if (shot.Age + Epsilon >= ordnance.LifeSpan)
                    {
                        _shots.Remove(shot);
                        Land(shot, events, null);
                    }
                }

                continue;
            }

            if (shot.Kind == ShotKind.Torpedo && shot.Age <= ordnance.SeekTime)
            {
                shot.Direction = Steer(shot, Tick);
            }

            // The shot hits where its step enters the target's shield ellipsoid while the shields are up and the
            // ordnance does not ignore them (0x4ec27a, 0x4ed427); else, or when the step misses the shield, when the
            // target lies within one step of it (0x4ec2b0, 0x4ed46b), a hit behind the shields with no effect.
            // stand-in: the target point is the unit's center; the game aims at the target's hardpoint (+0x110).
            var step = shot.Direction * ordnance.ShotSpeed * (float)Tick;
            var shielded = shot.Target.Shields > 0 && !ordnance.IgnoreShield;
            if (shielded && ShieldHit(shot.Target, shot.Position, shot.Position + step) is { } hit)
            {
                _shots.Remove(shot);
                shot.Position = hit.Contact;
                Land(shot, events, hit);
            }
            else if (Vector3.DistanceSquared(Position(shot.Target), shot.Position) < step.LengthSquared())
            {
                _shots.Remove(shot);
                shot.Position = Position(shot.Target);
                Land(shot, events, null);
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

    // The shield hit effect of the shot on its target at the shield contact `hit`, with the shield fraction as it is
    // now (0x5170e0). A projectile's effect is raised after its damage, a beam's from a tenth of its lifetime, before
    // the beam's own damage lands.
    ShieldHitEffect ShieldEffect(Shot shot, (Vector3 Direction, Vector3 Contact) hit)
    {
        var target = shot.Target;
        var ordnance = shot.Weapon.Ordnance;

        return new ShieldHitEffect
        {
            Shot = shot,
            Unit = target.Unit,
            Geometry = ordnance.ShieldGeometryName,
            Duration = ordnance.ShieldDuration,
            Raised = Time,
            Fraction = target.Profile.MaxHealth > 0 ? Math.Clamp(target.Shields / target.Profile.MaxHealth, 0, 1) : 0,
            Contact = hit.Contact,
            Direction = hit.Direction,
        };
    }

    // The game's shield test (0x4ee860) of the segment from `from` to `to`, world points, against the target's shield
    // ellipsoid at its current pose: the contact direction (ShieldEllipsoid.Hit) and the contact point in the world.
    // Null when the segment misses the shield, and for a target without an ellipsoid.
    (Vector3 Direction, Vector3 Contact)? ShieldHit(UnitCombatState target, Vector3 from, Vector3 to)
    {
        if (target.Shield is not { } shield)
            return null;
        var pose = _poses.Pose(target.Unit) with { Position = Position(target) };
        // the model's SOD coordinates are the Godot offsets with Z mirrored (HardpointGeometry.Offsets)
        Vector3 Model(Vector3 world) => Mirror(HardpointGeometry.ModelOffset(target.Unit, pose, world));
        if (shield.Hit(Model(from), Model(to)) is not { } direction)
            return null;

        return (direction, HardpointGeometry.WorldPosition(target.Unit, pose, Mirror(shield.Surface(direction))));
    }

    static Vector3 Mirror(Vector3 v) => new(v.X, v.Y, -v.Z);

    // The shot lands: its damage and hit location are rolled and applied to the target.
    // A projectile that entered the shield brings its contact, which raises the shield hit effect after the damage
    // (0x4ec288, 0x4ed45e); the gate read the shields before the damage.
    void Land(Shot shot, List<CombatEvent> events, (Vector3 Direction, Vector3 Contact)? shieldHit)
    {
        var target = shot.Target;
        var damage = CombatRules.Damage(shot.Weapon.Ordnance, target.DefinitionName, _random());
        var location = CombatRules.Location(target, Math.Min(99, (int)(_random() * 100)));
        var wasDying = target.Dying;
        var hit = CombatRules.Apply(target, shot.Weapon.Ordnance, damage, location, _protection);
        target.LastHit = _ticks;
        events.Add(new ShotLanded { Shot = shot, Hit = hit });
        if (!wasDying && hit.Damage > 0 && hit.SystemsPart != 0 && !hit.Destroyed)
        {
            events.Add(ExplosionOf(shot, location));
        }

        if (shieldHit is { } contact)
        {
            events.Add(ShieldEffect(shot, contact));
        }

        if (!hit.Destroyed)
            return;
        // the unit is dying now (CombatRules.Apply); shots in flight at it still land (0x4ee620) and cut its time
        target.Order = null;
        target.Target = null;
    }
}
