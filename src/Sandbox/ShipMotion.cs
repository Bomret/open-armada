using System;
using System.Numerics;

namespace OpenArmada.Sandbox;

/// <summary>The movement values of a ship's physics definition. MaxRoll is in degrees; null when the definition has none.</summary>
public sealed record PhysicsValues
{
    /// <summary>The definition's impulseSpeed: the travel speed in world units per second.</summary>
    public required double ImpulseSpeed { get; init; }

    /// <summary>The definition's yawRotationRate: the turn rate, read as radians per second.</summary>
    public required double YawRotationRate { get; init; }

    /// <summary>The definition's maxRoll in degrees; null when the definition has none (no bank).</summary>
    public required double? MaxRoll { get; init; }
}

/// <summary>
/// A ship's pose: Heading is the yaw about +Y (0 faces +Z, a positive heading turns toward +X), Bank the roll
/// about the ship's forward axis in radians (negative leans into a turn toward +X).
/// </summary>
public readonly record struct ShipPose
{
    /// <summary>The ship's position in the map's world units.</summary>
    public required Vector3 Position { get; init; }

    /// <summary>The yaw about +Y in radians: 0 faces +Z, a positive heading turns toward +X.</summary>
    public required float Heading { get; init; }

    /// <summary>The roll about the ship's forward axis in radians; negative leans into a turn toward +X.</summary>
    public required float Bank { get; init; }
}

/// <summary>
/// The kinematic model of ADR 0003: a ship turns toward its target at yawRotationRate, travels at impulseSpeed
/// along its heading, banks into the turn up to maxRoll, and stops at the target. The scene only asks it for a pose.
/// stand-in: units are the map's world units per second; yawRotationRate is read as radians per second (the game
/// stores angles in radians); the install defines no other movement rule.
/// </summary>
public sealed class ShipMotion
{
    // stand-in: a target nearer than this counts as reached.
    const float Reached = 1e-4f;

    // stand-in: a target nearer than this many turning circles (speed / yawRate) is reached by turning in place;
    // the install defines no such rule.
    const float Pivot = 2;

    // stand-in: a target more than this angle off the heading is reached by turning in place first; the install
    // defines no such rule.
    const float PivotAngle = MathF.PI / 2;

    // The no-thrust branch of the game's physics step (0x459440, checks 0x45969b-0x4596bf): the velocity vector
    // loses 2.0 per second per second against its own direction (0x459ca6-0x459cfe, constant 0x5cc140) until its
    // squared length is below 0.01 (0x459c46), then stays. The turn rate is not damped (0x459c0a) and only
    // changes the heading, so the path is straight.
    const float CoastDeceleration = 2f;
    const float CoastRest = 0.01f;

    // The state a thrust step started from, so a coast that began inside that step can take it back.
    readonly record struct Start(
        Vector3 Position,
        float Heading,
        float Bank,
        float Speed,
        float TurnRate,
        Vector2? Target,
        Vector2? Facing,
        double Seconds
    );

    readonly PhysicsValues _physics;
    Vector3 _position;
    float _heading;
    float _bank;
    float _speed; // units per second along the heading, as of the last thrust step
    float _turnRate; // radians per second, as of the last thrust step
    Vector3 _velocity; // units per second while coasting, fixed in direction by the loss of thrust
    Start? _last; // the last thrust step, null before one
    double _pending; // coast time not yet run as a whole world tick
    Vector2? _target; // X, Z
    Vector2? _facing; // X, Z of the point a ship turns to face in place; never set together with _target

    /// <summary>A ship at <c>position</c> facing <c>heading</c> (radians, as <see cref="ShipPose.Heading"/>), with no target and no bank.</summary>
    public ShipMotion(PhysicsValues physics, Vector3 position, float heading)
    {
        _physics = physics;
        _position = position;
        _heading = heading;
    }

    /// <summary>Whether the ship coasts: it has no thrust, and no order moves it (ADR 0008).</summary>
    public bool Coasting { get; private set; }

    /// <summary>
    /// Takes the thrust away: the ship keeps its current velocity, its current speed along its heading, and its
    /// current turn rate. The velocity slows at 2.0 units/s² against its own direction until its speed is below
    /// 0.1, then stays; the turn rate only turns the heading (0x459440). Its orders end, and no order gives it
    /// thrust back until <see cref="Thrust"/>. A ship already coasting keeps its coast.
    /// <c>overshoot</c> is how many seconds of the ship's last <see cref="Step"/> lie after the combat world's tick
    /// that took the thrust away: the ship takes that part of the step back, follows its orders for the rest, and
    /// coasts on from that tick, in phase with the world's ticks. Call it right after that step; an overshoot
    /// longer than the step, or a ship that made no thrust step, counts as none.
    /// stand-in: the position moves by the mean speed of each world tick (the half-step detail is medium
    /// confidence); a part of a tick waits for the next step.
    /// </summary>
    public void Coast(double overshoot = 0)
    {
        if (Coasting)
            return;
        if (overshoot > 0 && _last is { } last && overshoot <= last.Seconds + 1e-9)
        {
            overshoot = Math.Min(overshoot, last.Seconds);
            (_position, _heading, _bank, _speed, _turnRate, _target, _facing) = (
                last.Position,
                last.Heading,
                last.Bank,
                last.Speed,
                last.TurnRate,
                last.Target,
                last.Facing
            );
            Step(last.Seconds - overshoot);
        }
        else
        {
            overshoot = 0;
        }

        Coasting = true;
        _target = null;
        _facing = null;
        _velocity = _speed * new Vector3(MathF.Sin(_heading), 0, MathF.Cos(_heading));
        _pending = 0;
        RunCoast(overshoot);
    }

    /// <summary>Gives the thrust back: the ship stands at rest and takes orders again. No effect on a ship that does not coast.</summary>
    public void Thrust()
    {
        if (!Coasting)
            return;
        Coasting = false;
        _speed = 0;
        _turnRate = 0;
        _last = null;
    }

    /// <summary>Replaces the previous order; the ship keeps its own height. A coasting ship ignores it.</summary>
    public void SetTarget(float x, float z)
    {
        if (Coasting)
            return;
        _facing = null;
        _target = new Vector2(x, z);
    }

    /// <summary>Replaces the previous order; the ship stays where it is, at its own height, and keeps its heading.</summary>
    public void Stop()
    {
        if (Coasting)
            return;
        _target = null;
        _facing = null;
        _bank = 0;
        _speed = 0;
        _turnRate = 0;
    }

    /// <summary>
    /// Replaces the previous order; the ship turns in place to face the point, the shorter way at yawRotationRate,
    /// and never travels. A point at the ship's own position gives no heading to turn to. The bank follows the
    /// turns made, so Pose keeps the last step's bank until the next positive Step turns the ship. A coasting
    /// ship ignores it.
    /// </summary>
    public void Face(float x, float z)
    {
        if (Coasting)
            return;
        _target = null;
        _facing = new Vector2(x, z);
    }

    /// <summary>The ship's current pose.</summary>
    public ShipPose Pose =>
        new()
        {
            Position = _position,
            Heading = _heading,
            Bank = _bank,
        };

    /// <summary>
    /// The pose to draw: the simulated <see cref="Pose"/>, and for a coasting ship also the part of a world tick
    /// that has not run yet, at the ship's current velocity and turn rate, so a frame shorter than a tick still
    /// shows movement. Only the view reads it; the simulated state stays on the world's ticks.
    /// </summary>
    public ShipPose Drawn =>
        Coasting
            ? Pose with
            {
                Position = _position + _velocity * (float)_pending,
                Heading = Wrap(_heading + _turnRate * (float)_pending),
            }
            : Pose;

    /// <summary>
    /// Moves the ship by its order for <c>seconds</c> and returns the pose it reaches. A ship that reaches its target
    /// stands exactly on it and its order ends. Zero or negative seconds change nothing.
    /// </summary>
    public ShipPose Step(double seconds)
    {
        if (seconds > 0 && Coasting)
        {
            RunCoast(seconds);
        }
        else if (seconds > 0)
        {
            _last = new Start(_position, _heading, _bank, _speed, _turnRate, _target, _facing, seconds);
            _speed = 0;
            _turnRate = 0;
            if (_target is { } target)
            {
                Advance(target, (float)seconds);
            }
            else if (_facing is { } facing)
            {
                TurnToFace(facing, (float)seconds);
            }
        }

        return Pose;
    }

    // Runs the whole world ticks the time makes, carrying the rest, so frame rate does not change the path.
    void RunCoast(double seconds)
    {
        _pending += seconds;
        const double Epsilon = 1e-9; // as the combat world's tick loop
        var dt = (float)CombatWorld.Tick;
        while (_pending + Epsilon >= CombatWorld.Tick)
        {
            _pending -= CombatWorld.Tick;
            var before = _velocity;
            var speed = before.Length();
            if (speed * speed >= CoastRest)
                _velocity = before / speed * MathF.Max(0, speed - CoastDeceleration * dt);
            _position += (before + _velocity) / 2 * dt;
            _heading = Wrap(_heading + _turnRate * dt);
        }
    }

    void TurnToFace(Vector2 facing, float seconds)
    {
        var offset = facing - new Vector2(_position.X, _position.Z);
        if (offset.Length() <= Reached)
        {
            Stop();

            return;
        }

        var (_, error) = Turn(offset, seconds);
        if (error == 0)
        {
            Stop();
        }
    }

    // Turns toward the offset's bearing, the shorter way, never more than yawRotationRate allows in `seconds`, and
    // banks as the turn asks. Returns the turn made and the error left.
    (float Turn, float Error) Turn(Vector2 offset, float seconds)
    {
        var error = BearingError(offset);
        var most = (float)_physics.YawRotationRate * seconds;
        var turn = MathF.Abs(error) <= most ? error : MathF.CopySign(most, error);
        _heading = Wrap(_heading + turn);
        _turnRate = turn / seconds;
        _bank = _physics.MaxRoll is { } roll ? -(float)(roll * Math.PI / 180) * Math.Clamp(turn / most, -1, 1) : 0; // stand-in: no bank without maxRoll (the Borg physics definitions have none)

        return (turn, error - turn);
    }

    // The angle from the heading to the offset's bearing, the shorter way.
    float BearingError(Vector2 offset) => MathF.IEEERemainder(MathF.Atan2(offset.X, offset.Y) - _heading, 2 * MathF.PI);

    void Advance(Vector2 target, float seconds)
    {
        var offset = target - new Vector2(_position.X, _position.Z);
        var distance = offset.Length();
        if (distance <= Reached)
        {
            Arrive(target);

            return;
        }

        // the pivot decision reads the error before this step's turn
        var before = BearingError(offset);
        var pivot =
            distance < Pivot * (float)(_physics.ImpulseSpeed / _physics.YawRotationRate)
            || MathF.Abs(before) >= PivotAngle;
        var (turn, error) = Turn(offset, seconds);

        // ponytail: a target inside two turning circles (2 * speed / yawRate) is reached by turning in place and
        // then driving straight; a ship never orbits it. Farther away it drives while turning. The ship drives
        // only for the time left after the turn completes within the step. Smooth arcs or slowing down if the
        // pivot looks wrong.
        var driving = seconds;
        if (pivot)
        {
            if (error != 0)
                return;
            driving = MathF.Max(0, driving - MathF.Abs(turn) / (float)_physics.YawRotationRate); // float rounding can leave it an ulp below 0
        }

        // never farther along the heading than the foot of the target's perpendicular, so the distance only shrinks
        var travel = MathF.Min((float)_physics.ImpulseSpeed * driving, distance * MathF.Cos(error));
        if (travel >= distance)
        {
            Arrive(target);

            return;
        }

        _position += new Vector3(MathF.Sin(_heading), 0, MathF.Cos(_heading)) * travel;
        _speed = travel / seconds;
    }

    // The angle in (-pi, pi].
    static float Wrap(float angle)
    {
        var wrapped = MathF.IEEERemainder(angle, 2 * MathF.PI);

        return wrapped <= -MathF.PI ? wrapped + 2 * MathF.PI : wrapped;
    }

    void Arrive(Vector2 target)
    {
        _position = new Vector3(target.X, _position.Y, target.Y);
        _target = null;
        _bank = 0;
    }
}
