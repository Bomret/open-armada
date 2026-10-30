using System;
using System.Collections.Generic;
using System.Numerics;

namespace OpenArmada.Sandbox;

/// <summary>The movement values of a ship's physics definition: its flight coefficients and the distances its orders use.</summary>
public sealed record PhysicsValues
{
    /// <summary>
    /// stand-in: the compiled default of tooCloseToTurn, 25 (0x45fbe0), for a definition without the key or with a
    /// value that is not a positive number.
    /// </summary>
    public const double DefaultTooCloseToTurn = 25;

    /// <summary>
    /// The compiled default of pathLeadDistance (0x45fbe0, 0x455440): the class stores the squared distance 30 and reads
    /// the key as a distance, so a definition without it leads by the root of 30.
    /// </summary>
    public static readonly double DefaultPathLeadDistance = Math.Sqrt(30);

    /// <summary>
    /// The ship's speed cap in world units per second (+0x4a8): the definition's <c>impulseSpeed</c>, or its
    /// <c>maxForwardSpeed</c> when the definition has no impulse speed above 0. The carrot's speed and the arrival test
    /// read it. It stays the ship's own after the swap to <c>fastphys.odf</c> (0x458750 does not copy it).
    /// </summary>
    public required double ImpulseSpeed { get; init; }

    /// <summary>The coefficients of the ship's own physics definition.</summary>
    public required FlightCoefficients Coefficients { get; init; }

    /// <summary>
    /// The coefficients of <c>fastphys.odf</c>, which a ship takes at its first move or attack and keeps (0x42e4a0 calls
    /// 0x458750 with the shared class 0x632ecc, loaded by 0x425a20). Null when the file cannot be read: the ship keeps
    /// its own.
    /// </summary>
    public FlightCoefficients? Fast { get; init; }

    /// <summary>
    /// The definition's tooCloseToTurn in world units (0x455440): a ship this close to a move order's point ends the
    /// order once slower than half its speed, and holds the point with the brake spring instead of steering.
    /// </summary>
    public double TooCloseToTurn { get; init; } = DefaultTooCloseToTurn;

    /// <summary>
    /// The definition's pathLeadDistance (0x455440, stored squared): the distance the carrot keeps ahead of the ship and
    /// the radius inside which the ship counts a path point as passed.
    /// </summary>
    public double PathLeadDistance { get; init; } = DefaultPathLeadDistance;

    /// <summary>
    /// The definition's borgPhysics flag (class +0x1ec, 0x455440): a ship with it flies the Borg branch of the physics
    /// step (0x459a99) and the Borg path of the steering (0x42f740) and the move maneuver (0x42e4a0), adds 0.42 to the
    /// vertical push of every collision separation it is part of and never takes the collision turn (0x464910,
    /// 0x464620).
    /// </summary>
    public bool BorgPhysics { get; init; }

    /// <summary>
    /// The game's compiled default of tooSlowToTurn, 1 (0x45fbe0): the speed under which a Borg ship inside
    /// tooCloseToTurn of its point takes its yaw as the heading target.
    /// </summary>
    public const double DefaultTooSlowToTurn = 1;

    /// <summary>The hover class's compiled default of rotationAcceleration, 2 (0x455230).</summary>
    public const double DefaultRotationAcceleration = 2;

    /// <summary>The hover class's compiled default of maxRotationSpeed, 0.25 (0x455230).</summary>
    public const double DefaultMaxRotationSpeed = 0.25;

    /// <summary>The hover class's compiled default of rotationStiffness, 1 (0x455230).</summary>
    public const double DefaultRotationStiffness = 1;

    /// <summary>The definition's tooSlowToTurn (class +0x210, 0x455440); read by the Borg arrival spring only.</summary>
    public double TooSlowToTurn { get; init; } = DefaultTooSlowToTurn;

    /// <summary>
    /// The definition's rotationAcceleration (class +0x494): the rate at which a Borg ship's yaw rate relaxes to its
    /// limit, and the factor of the yaw acceleration's limit (class +0x498 = this x <see cref="MaxRotationSpeed"/>).
    /// The class keeps it whatever physics file the ship swaps to.
    /// </summary>
    public double RotationAcceleration { get; init; } = DefaultRotationAcceleration;

    /// <summary>The definition's maxRotationSpeed (class +0x490): the yaw rate a Borg ship's saturated spring relaxes to.</summary>
    public double MaxRotationSpeed { get; init; } = DefaultMaxRotationSpeed;

    /// <summary>
    /// The definition's rotationStiffness (class +0x4cc): the stiffness K of a Borg ship's yaw spring, whose damping is
    /// 2 x sqrt(K) (class +0x4d0).
    /// </summary>
    public double RotationStiffness { get; init; } = DefaultRotationStiffness;

    /// <summary>
    /// The values of <c>keys</c>: the speed cap and the coefficients, with <c>fast</c> the keys of
    /// <c>fastphys.odf</c> when the ship swaps to them. The distances keep their defaults.
    /// </summary>
    public static PhysicsValues From(PhysicsKeys keys, PhysicsKeys? fast = null) =>
        new()
        {
            ImpulseSpeed = keys["impulseSpeed"] > 0 ? keys["impulseSpeed"] : keys["maxForwardSpeed"],
            Coefficients = FlightCoefficients.Derive(keys),
            Fast = fast == null ? null : FlightCoefficients.Derive(fast),
        };
}

/// <summary>
/// A ship's flight: its position, velocity, orientation angles and their rates, and the control block the
/// steering writes, with the game's physics step (0x459440) that integrates them on the combat world's tick and the
/// move maneuver (0x42e4a0) that steers them. All state lives in the game's frame, where a ship's forward axis at yaw 0
/// is +Z; every public vector is in the sandbox's frame (Z mirrored, <see cref="MoveGeometry"/>).
/// </summary>
/// <remarks>
/// The tick order is the game's (0x43f2b0, 0x43f1a0): the process that steers (the move order, the hold, the face) runs
/// first and writes the controls; the physics step then reads them (inferred: the AI update 0x433b30 runs ahead of the
/// world tick 0x43f1a0). The terrain coefficient and cap (<see cref="CapSpeed"/>) are set before the step and
/// reset by it. A ship keeps its velocity and rates whatever its thrust.
/// </remarks>
public sealed class FlightModel
{
    // the pitch angle is held within 1.5 radians (0x5cc138, 0x5ca8b8); a velocity component above 120 resets the velocity (0x5cc130)
    const float PitchLimit = 1.5f;
    internal const float VelocityLimit = 120f;

    // the no-thrust branch brakes at 2.0 (0x5cc140) while the squared speed is at least 0.01 (0x459c46)
    const float CoastBrake = 2f;
    const float CoastRest = 0.01f;

    // the sub-step rule's constants (0x459494): 0.05 least, 0.1 over the rotation rate, 4 over the speed
    const float LeastSubStep = 0.05f;
    const float RotationStep = 0.1f;
    const float SpeedStep = 4f;

    // the steering direction divides by at least 20 (0x5ca7d4) and is zero below 0.001 (0x5ca750)
    const float LeastSteerDistance = 20f;
    const float ZeroDistance = 0.001f;

    // the target-location dogfight faces the point when its squared distance exceeds 100 (0x5ca86c)
    const float PivotSquared = 100f;

    // the spring replaces the steering throttle when the point lies within this cosine of the heading (cos 45 degrees, 0x5ca8c8)
    const float HeadingCone = 0.707f;

    // the arrival test reads half the impulse speed (0x5ca230)
    const float ArrivalSpeed = 0.5f;

    // 1 and no cap: the terrain coefficient and limit a physics step starts from and returns to (0x465cc0)
    const float NoCap = 1e10f;

    readonly PhysicsValues _values;
    FlightCoefficients _c;
    Vector3 _position,
        _velocity; // game frame
    float _pitch,
        _yaw,
        _roll,
        _pitchRate,
        _yawRate,
        _rollRate;
    float _rollControl,
        _pitchControl,
        _lift,
        _throttle;
    float _terrainCoefficient = 1,
        _terrainLimit = NoCap;

    // The Borg velocity command (+0x158, game frame): written by the steering or the spring, read by the next Borg step.
    Vector3 _command;

    /// <summary>A ship standing at <c>placed</c> at rest, on its own physics definition.</summary>
    public FlightModel(PhysicsValues values, ShipPose placed)
    {
        _values = values;
        _c = values.Coefficients;
        _position = Game(placed.Position);
        _pitch = placed.Pitch;
        _yaw = placed.Yaw;
        _roll = placed.Roll;
        HeadingTarget = placed.Yaw;
    }

    /// <summary>The ship's speed cap (<see cref="PhysicsValues.ImpulseSpeed"/>).</summary>
    public double ImpulseSpeed => _values.ImpulseSpeed;

    /// <summary>The definition's <see cref="PhysicsValues.TooCloseToTurn"/>.</summary>
    public double TooCloseToTurn => _values.TooCloseToTurn;

    /// <summary>The definition's <see cref="PhysicsValues.PathLeadDistance"/>.</summary>
    public double PathLeadDistance => _values.PathLeadDistance;

    /// <summary>The definition's <see cref="PhysicsValues.BorgPhysics"/>.</summary>
    public bool BorgPhysics => _values.BorgPhysics;

    /// <summary>The coefficients now in force: the ship's own, or <c>fastphys.odf</c>'s once the ship has moved or attacked.</summary>
    public FlightCoefficients Coefficients => _c;

    /// <summary>The heading target (+0x4b0): set to the yaw by the decomposition and to the steering direction's bearing by the move.</summary>
    public float HeadingTarget { get; set; }

    /// <summary>
    /// The facing offset of the ship's order (the process's +0x114): a multiple of a quarter turn the order handler
    /// draws once per accepted move order (<c>CombatWorld.Move</c>). A Borg ship's heading target is the steering
    /// direction's bearing plus it (0x42f9b0); a ship without Borg physics ignores it. It stays as the last order left it:
    /// the attack orders draw none (0x426ad0).
    /// </summary>
    public float FacingOffset { get; set; }

    /// <summary>
    /// The avoidance seam of the steering direction (0x42ebb0 calls 0x42ef70 between building the direction and the
    /// height clamp): given the ship, the order's goal, and the direction, in the sandbox's frame, it returns the
    /// direction to steer. The game hands the avoidance the goal that the move maneuver (0x42e4a0) passed in, not the
    /// carrot the direction points at. Null is no avoidance, as the stock game (OBSTACLE_AVOIDANCE 0).
    /// </summary>
    public Func<FlightModel, Vector3, Vector3, Vector3>? Avoidance { get; set; }

    /// <summary>The move order the ship follows; null when it has none.</summary>
    public MovePath? Order { get; private set; }

    /// <summary>The ship's pose: its position and the game's angles.</summary>
    public ShipPose Pose =>
        new()
        {
            Position = Game(_position),
            Pitch = _pitch,
            Yaw = _yaw,
            Roll = _roll,
        };

    /// <summary>The ship's world velocity in units per second, in the sandbox's frame; its length is the ship's speed.</summary>
    public Vector3 Velocity => Game(_velocity);

    /// <summary>The ship's forward axis (+0x80) in the sandbox's frame, a unit vector from the angles.</summary>
    public Vector3 Forward => Game(Axes(_pitch, _yaw, _roll).Forward);

    /// <summary>The ship's speed: the length of the physics velocity (+0x138).</summary>
    public float Speed => _velocity.Length();

    /// <summary>The pitch, yaw, and roll rates in radians per second (+0x14c).</summary>
    public (float Pitch, float Yaw, float Roll) Rates => (_pitchRate, _yawRate, _rollRate);

    /// <summary>The control block (+0x494): roll, pitch, lift, and throttle, each in -1 to 1.</summary>
    public (float Roll, float Pitch, float Lift, float Throttle) Controls =>
        (_rollControl, _pitchControl, _lift, _throttle);

    /// <summary>The game's vector in the sandbox's frame and back: Z mirrored.</summary>
    internal static Vector3 Game(Vector3 v) => new(v.X, v.Y, -v.Z);

    // The ship's side, up, and forward axes in the game's frame, from the angles (0x55d080: Ry(yaw) Rx(pitch) Rz(roll)).
    static (Vector3 Side, Vector3 Up, Vector3 Forward) Axes(float pitch, float yaw, float roll)
    {
        var (sp, cp) = MathF.SinCos(pitch);
        var (sy, cy) = MathF.SinCos(yaw);
        var (sr, cr) = MathF.SinCos(roll);

        return (
            new Vector3(cr * cy + sr * sp * sy, sr * cp, -cr * sy + sr * sp * cy),
            new Vector3(-sr * cy + cr * sp * sy, cr * cp, sr * sy + cr * sp * cy),
            new Vector3(cp * sy, -sp, cp * cy)
        );
    }

    static float Unit(float x) => float.IsNaN(x) ? 0 : Math.Clamp(x, -1f, 1f);

    // Constant time whatever the angle: a non-finite or huge angle (a degenerate definition value) becomes NaN or a
    // wrapped value, never a loop that does not end, and the step's non-finiteness guard reports the NaN.
    static float Wrap(float angle) => MoveGeometry.Wrap(angle);

    /// <summary>
    /// Takes the coefficients of <c>fastphys.odf</c> and keeps them (0x458750 through the shared class, called by the
    /// move maneuver 0x42e4a0 and the dogfight 0x42c9d0); speed and class distances stay the ship's own. No effect
    /// where the install gave no <c>fastphys.odf</c>, or on a ship that already has them.
    /// </summary>
    public void UseFastPhysics() => _c = _values.Fast ?? _c;

    /// <summary>
    /// Lowers the terrain coefficient to at most <c>coefficient</c> and, for a <c>limit</c> of 0 or more, the speed limit
    /// to at most <c>limit</c> (0x4690c0). The physics step resets both.
    /// </summary>
    public void CapSpeed(float coefficient, float limit)
    {
        _terrainCoefficient = MathF.Min(_terrainCoefficient, coefficient);
        if (limit >= 0)
        {
            _terrainLimit = MathF.Min(_terrainLimit, limit);
        }
    }

    /// <summary>A position push: the ship moves by <c>displacement</c> and keeps its velocity, angles, rates, and order.</summary>
    public void Push(Vector3 displacement) => _position += Game(displacement);

    /// <summary>A velocity change: <c>delta</c> is added to the velocity (0x450ea0 writes it).</summary>
    public void AddVelocity(Vector3 delta) => _velocity += Game(delta);

    /// <summary>A velocity change that replaces the velocity.</summary>
    public void SetVelocity(Vector3 velocity) => _velocity = Game(velocity);

    /// <summary>
    /// Writes the orientation angles as the matrix setter's decomposition does (0x4718e0, 0x458ea0): the rates stay as
    /// they were and the heading target becomes the yaw. The collision turn writes its turned orientation through it.
    /// </summary>
    public void SetAngles(float pitch, float yaw, float roll)
    {
        _pitch = pitch;
        _yaw = yaw;
        _roll = roll;
        HeadingTarget = yaw;
    }

    /// <summary>
    /// The pose to draw: the simulated <see cref="Pose"/> advanced by <c>ahead</c> seconds, the part of a world tick that
    /// has not run yet, at the ship's velocity and rates, so a frame shorter than a tick still shows movement. Only the
    /// view reads it; the simulated state stays on the world's ticks.
    /// </summary>
    public ShipPose Drawn(double ahead)
    {
        var t = (float)ahead;

        return new ShipPose
        {
            Position = Game(_position + _velocity * t),
            Pitch = Math.Clamp(_pitch + _pitchRate * t, -PitchLimit, PitchLimit),
            Yaw = Wrap(_yaw + _yawRate * t),
            Roll = Wrap(_roll + _rollRate * t),
        };
    }

    /// <summary>
    /// One physics step of <c>seconds</c> (0x459440), in sub-steps of at most
    /// <c>h = max(0.05, min(0.1 / |rates|, 4 / speed))</c>. A ship that is dying, or has no thrust (engines destroyed or
    /// derelict), takes the no-thrust branch: its velocity brakes at 2.0 along itself until its squared length is below
    /// 0.01, and its three rates keep spinning undamped. A ship with thrust steers by its controls. A ship with
    /// thrust turns by its bank, dying or not (0x459579 does not test the dying flag). Zero or negative seconds change nothing.
    /// </summary>
    /// <exception cref="InvalidOperationException">when the step leaves a non-finite state: it is restored, the ship
    /// stands at the state it had, with no velocity or rates.</exception>
    public void Step(double seconds, bool dying, bool hasThrust)
    {
        if (!(seconds > 0) || double.IsInfinity(seconds))
            return;
        var before = (_position, _velocity, _pitch, _yaw, _roll, _pitchRate, _yawRate, _rollRate);
        var dt = (float)seconds;
        var thrusting = !dying && hasThrust;
        var borg = thrusting && _values.BorgPhysics;
        var alive = thrusting && !_values.BorgPhysics;
        var bank = hasThrust && !_values.BorgPhysics;
        var h = MathF.Max(
            LeastSubStep,
            MathF.Min(RotationStep / new Vector3(_pitchRate, _yawRate, _rollRate).Length(), SpeedStep / Speed)
        );
        var n = 1;
        var sub = dt;
        if (dt > h)
        {
            n = (int)MathF.Ceiling(dt / h);
            sub = dt / n;
        }

        var inverse = sub >= 0.001f ? 1 / sub : 1000f;
        var half = sub / 2;
        // the Borg branch's stored accelerations (+0x158, +0x168): written on sub-step 0, read by the later ones
        var borgAcceleration = Vector3.Zero;
        float borgYawAcceleration = 0;
        for (var i = 0; i < n; i++)
        {
            var (side, up, forward) = Axes(_pitch, _yaw, _roll);
            float pitchAcceleration = 0,
                rollAcceleration = 0,
                yawAcceleration = 0;
            Vector3 acceleration;
            if (borg)
            {
                if (i == 0)
                {
                    (borgAcceleration, borgYawAcceleration) = BorgAcceleration();
                }

                acceleration = borgAcceleration;
                yawAcceleration = borgYawAcceleration;
            }
            else if (alive)
            {
                var gain =
                    1 - 1 / ((_velocity - Vector3.Dot(up, _velocity) * up).Length() * _c.GainSlope + _c.GainBase);
                var pitchDamping = sub * _c.PitchRate < 1 ? _c.PitchRate : inverse;
                pitchAcceleration =
                    -pitchDamping * _pitchRate
                    - _pitchControl * _c.PitchControl * gain
                    - _c.PitchStiffness * gain * _pitch;
                var rollDamping = sub * _c.RollRate < 1 ? _c.RollRate : inverse;
                rollAcceleration =
                    -rollDamping * _rollRate + _c.RollControl * _rollControl * gain - _c.RollStiffness * _roll;
                var along = Vector3.Dot(forward, _velocity);
                var thrust =
                    (
                        _throttle > 0 ? _c.ForwardThrust
                        : along > 0 ? _c.ReverseThrust
                        : 0
                    ) * _throttle;
                var coefficient = MathF.Max(_terrainCoefficient, 1e-6f);
                float drag;
                if (thrust * coefficient > _terrainLimit * _c.ForwardDrag)
                {
                    _terrainLimit = MathF.Max(_terrainLimit, 0.01f);
                    drag = thrust / _terrainLimit;
                }
                else
                {
                    drag = _c.ForwardDrag / coefficient;
                }

                thrust -= drag * along;
                if (along + thrust * sub < 0)
                {
                    thrust = -along * inverse; // the forward speed never crosses zero within a sub-step
                }

                acceleration =
                    thrust * forward
                    + (_lift * _c.Lift - Vector3.Dot(up, _velocity) * _c.UpDownDrag) * up
                    - _c.SideDrag * Vector3.Dot(side, _velocity) * side;
            }
            else
            {
                var squared = _velocity.LengthSquared();
                acceleration = squared >= CoastRest ? _velocity / MathF.Sqrt(squared) * -CoastBrake : Vector3.Zero;
            }

            _pitchRate += pitchAcceleration * half;
            _rollRate += rollAcceleration * half;
            _yawRate += yawAcceleration * half;
            if (bank)
            {
                _yawRate = -_c.BankTurn * _roll; // the yaw rate comes only from the bank, set after the first half kick
            }

            _pitch += sub * _pitchRate;
            _yaw += sub * _yawRate;
            _roll += sub * _rollRate;
            _pitchRate += pitchAcceleration * half;
            _rollRate += rollAcceleration * half;
            _yawRate += yawAcceleration * half;
            _velocity += acceleration * half;
            _pitch = Math.Clamp(_pitch, -PitchLimit, PitchLimit);
            _roll = Wrap(_roll);
            _position += sub * _velocity;
            _velocity += acceleration * half;
        }

        _yaw = Wrap(_yaw);
        // the matrix setter's decomposition (0x4718e0, 0x458ea0) leaves the heading target at the yaw after each sub-step
        HeadingTarget = _yaw;
        _command = Vector3.Zero; // the process clears a Borg ship's command (+0x158) before it steers (0x4267a0)
        if (
            MathF.Abs(_velocity.X) > VelocityLimit
            || MathF.Abs(_velocity.Y) > VelocityLimit
            || MathF.Abs(_velocity.Z) > VelocityLimit
        )
        {
            _velocity = Vector3.Zero;
        }

        _terrainCoefficient = 1;
        _terrainLimit = NoCap;
        if (
            !IsFinite(_position, _velocity)
            || !float.IsFinite(_pitch + _yaw + _roll + _pitchRate + _yawRate + _rollRate)
        )
        {
            (_position, _velocity, _pitch, _yaw, _roll, _pitchRate, _yawRate, _rollRate) = before;
            _velocity = Vector3.Zero;
            _pitchRate = _yawRate = _rollRate = 0;
            HeadingTarget = _yaw; // a non-finite target (the fault's cause) would fault every later Borg step

            throw new InvalidOperationException("the physics step left a non-finite state");
        }
    }

    static bool IsFinite(Vector3 a, Vector3 b) => float.IsFinite(a.X + a.Y + a.Z + b.X + b.Y + b.Z);

    /// <summary>
    /// Starts a move order along <c>points</c> (in the sandbox's frame, the first at the ship and the last the goal), the
    /// planner's path or the two-point path [ship, goal]. <c>final</c> is the order's final point where the path leads to an
    /// earlier point of the order (the stuck replan, 0x42ded0); it is the path's last point otherwise. The ship takes the
    /// coefficients of <c>fastphys.odf</c> (<see cref="UseFastPhysics"/>).
    /// </summary>
    public void StartMove(IReadOnlyList<Vector3> points, Vector3? final = null)
    {
        Order = new MovePath(points, final);
        UseFastPhysics();
    }

    /// <summary>Ends the move order; the controls stay as they were until the next steering writes them.</summary>
    public void EndMove() => Order = null;

    /// <summary>
    /// The order's arrival test (0x428170): the 3D distance to the path's last point is under tooCloseToTurn and the
    /// speed is under half the impulse speed. False without an order.
    /// </summary>
    public bool HasArrived =>
        Order is { } order
        && Vector3.Distance(order.FinalGame, _position) < _values.TooCloseToTurn
        && Speed < _values.ImpulseSpeed * ArrivalSpeed;

    /// <summary>
    /// One tick of the move order's process after the arrival test (0x42ded0, 0x42e4a0): the carrot advances along the
    /// path by 10 units a pass until it lies the lead distance from the ship or at the path's end, and the move maneuver
    /// steers to it. No effect without an order.
    /// </summary>
    public void Follow(CombatBand band)
    {
        if (Order is not { } order)
            return;
        var lead = (float)_values.PathLeadDistance;
        var (carrot, carrotVelocity) = order.Lead(_position, lead * lead, (float)_values.ImpulseSpeed);
        Maneuver(carrot, carrotVelocity, order.FinalGame, band);
    }

    /// <summary>
    /// A hold where the ship stands: it brakes to rest, levelling out (0x428081 with its own position). stand-in: the
    /// game's maneuver swaps in <c>fastphys.odf</c> for the hold too (gated by +0xbc and +0x78c, meaning unknown); the
    /// hold keeps the ship's own coefficients, since the dynamic coefficients are fastphys's from the first move or
    /// attack on.
    /// </summary>
    public void Brake(CombatBand band) => Maneuver(_position, Vector3.Zero, _position, band);

    /// <summary>
    /// A move straight at <c>point</c>, without a path: the move maneuver with the point as carrot and goal and
    /// <c>pointVelocity</c> (sandbox frame) as the point's velocity: 0 for the idle handler's return to the tether point
    /// (0x42a170), the ordered unit's velocity for the attack (0x42fb90 reads it from +0x140 to +0x148 and 0x428e90
    /// hands it to 0x42e4a0). The velocity enters the spring only. Inside tooCloseToTurn of the point the ship brakes
    /// by the spring with its roll, pitch, and lift at 0; farther it steers there. The ship takes the coefficients of
    /// <c>fastphys.odf</c>. The attack closes on a target within the path's replan distance this way. A Borg ship
    /// (<see cref="BorgPhysics"/>) closes by the Borg move maneuver without a heading target: the velocity command
    /// toward the point beyond tooCloseToTurn, the 3D spring to it within; <c>pointVelocity</c> is not read.
    /// </summary>
    public void Approach(Vector3 point, CombatBand band, Vector3 pointVelocity = default)
    {
        UseFastPhysics();
        if (_values.BorgPhysics)
        {
            BorgManeuver(Game(point), Game(point), band, false);

            return;
        }

        Maneuver(Game(point), Game(pointVelocity), Game(point), band);
    }

    /// <summary>
    /// The dogfight against a target whose origin is at <c>point</c> and the longest non-special weapon range
    /// <c>range</c> (0x42c9d0): the aim is d times (|d| - range) while the target is beyond the range and 0 within it,
    /// d being the vector from the ship's origin to the point's. Its Y is zeroed while the ship is above the band's
    /// ceiling and the aim rises, or below the floor and the aim sinks. The ship steers along the aim with scale 1
    /// (full throttle beyond the range, 0.001 and level within it) and then the yaw servo turns it toward the point's
    /// bearing. A Borg ship (<see cref="BorgPhysics"/>) gets no servo and no <see cref="HeadingTarget"/>: it drives
    /// by the velocity command along the aim (the Borg path of 0x42f740, scale 1) and keeps its heading (no yaw target
    /// is written for it). The ship takes the coefficients of <c>fastphys.odf</c>. stand-in: the center is the position.
    /// </summary>
    public void Dogfight(Vector3 point, float range, CombatBand band)
    {
        UseFastPhysics();
        var d = Game(point) - _position;
        var length = d.Length();
        var aim = length > range ? d * (length - range) : Vector3.Zero;
        if ((_position.Y > band.Ceiling && aim.Y > 0) || (_position.Y < band.Floor && aim.Y < 0))
        {
            aim.Y = 0;
        }

        SteerGame(aim, 1);
        if (!BorgPhysics)
        {
            ServoYaw(MathF.Atan2(d.X, d.Z));
        }
    }

    /// <summary>
    /// Turns in place toward <c>point</c> at zero throttle, the dogfight of a ship with a target-location weapon
    /// (0x42c9d0): the steering with scale 0 along the vector to the point, or along the ship's forward axis when the
    /// point lies within 10 of the ship (the squared distance at most 100). A ship given its own position keeps its
    /// heading. A Borg ship (<see cref="BorgPhysics"/>) neither turns nor thrusts: the Borg steering at scale 0
    /// commands no velocity, so it brakes by its drag, and no heading target is written. The ship takes the
    /// coefficients of <c>fastphys.odf</c>.
    /// </summary>
    public void Pivot(Vector3 point)
    {
        UseFastPhysics();
        var to = Game(point) - _position;
        if (to.LengthSquared() > PivotSquared)
        {
            SteerGame(to, 0);
        }
        else
        {
            var (_, _, forward) = Axes(_pitch, _yaw, _roll);
            SteerGame(forward, 0);
        }
    }

    /// <summary>
    /// The steering direction toward <c>point</c> in the sandbox's frame (0x42ebb0): (point - center) over at least 20,
    /// zero when the point is within 0.001; the avoidance seam may rewrite it; then its Y is zeroed above
    /// <c>band</c>'s ceiling when it rises and below the floor when it sinks. Not normalized. The seam gets
    /// <c>goal</c>, the point itself where none is given (<see cref="Avoidance"/>).
    /// </summary>
    public Vector3 SteeringDirection(Vector3 point, CombatBand band, Vector3? goal = null) =>
        Game(SteeringDirectionGame(Game(point), Game(goal ?? point), band));

    /// <summary>
    /// Steers along <c>direction</c> (sandbox frame) with throttle clamp(|direction|, 1) times <c>scale</c> (0x42f740): the
    /// pitch servo toward asin(y / |direction|), the yaw servo toward its bearing, the lift at 0.
    /// </summary>
    public void Steer(Vector3 direction, float scale) => SteerGame(Game(direction), scale);

    Vector3 SteeringDirectionGame(Vector3 point, Vector3 goal, CombatBand band)
    {
        var to = point - _position;
        var length = to.Length();
        var direction = length < ZeroDistance ? Vector3.Zero : to / MathF.Max(length, LeastSteerDistance);
        if (Avoidance is { } avoid)
        {
            direction = Game(avoid(this, Game(goal), Game(direction)));
        }

        if (_position.Y > band.Ceiling && direction.Y > 0)
        {
            direction.Y = 0;
        }

        if (_position.Y < band.Floor && direction.Y < 0)
        {
            direction.Y = 0;
        }

        return direction;
    }

    // The Borg branch's two accelerations (0x459a99): the yaw spring and the velocity command's drag law.
    (Vector3 Linear, float Yaw) BorgAcceleration()
    {
        var rotation = (float)_values.RotationAcceleration;
        var limit = rotation * (float)_values.MaxRotationSpeed;
        var stiffness = (float)_values.RotationStiffness;
        var damping = 2 * MathF.Sqrt(stiffness);
        var error = Wrap(_yaw - HeadingTarget);
        var q = Math.Clamp((rotation * _yawRate - stiffness * error - damping * _yawRate) / limit, -1f, 1f);

        return (_c.ForwardThrust * _command - _c.ForwardDrag * _velocity, q * limit - rotation * _yawRate);
    }

    // The Borg path of the steering (0x42f740): the command toward the direction's velocity, the unit direction times
    // min(|direction|, 1) x scale x the speed cap, whatever the ship faces. No servo and no heading target.
    void BorgSteer(Vector3 direction, float scale)
    {
        var length = MathF.Max(direction.Length(), ZeroDistance);
        var magnitude = MathF.Min(length, 1f);
        _throttle = magnitude * scale;
        var wanted = direction * (magnitude * scale * (float)_values.ImpulseSpeed / length);
        var command = (-_c.BrakeDamping * (_velocity - wanted) + _c.ForwardDrag * _velocity) / _c.ForwardThrust;
        if (command.LengthSquared() > scale * scale)
        {
            command *= scale / command.Length();
        }

        _command = command;
    }

    // The 3D spring of the move maneuver's arrival (0x42e4a0, Borg path): the acceleration -k (position - point) - 2sqrt(k) v
    // as a command. simplicity: the command's limit is 1 whichever way it points; the game's braking limit is the warp
    // multiplier (+0x4ac), which the engine does not model.
    void BorgSpring(Vector3 point)
    {
        var acceleration = -_c.BrakeStiffness * (_position - point) - _c.BrakeDamping * _velocity;
        var command = (acceleration + _c.ForwardDrag * _velocity) / _c.ForwardThrust;
        if (command.LengthSquared() > 1)
        {
            command /= command.Length();
        }

        _command = command;
        if (Speed < _values.TooSlowToTurn)
        {
            HeadingTarget = _yaw;
        }
    }

    // The Borg move maneuver (0x42e4a0): farther than tooCloseToTurn from the goal the ship steers by the steering
    // direction toward `point` (0x42f9b0, 0x42f740), with the heading target the direction's bearing plus the facing
    // offset, wrapped once above half a circle; inside it the 3D spring holds `point`. `faces` is false for the attack's
    // closing, which sets no heading target (0x42c400, 0x42f740).
    void BorgManeuver(Vector3 point, Vector3 goal, CombatBand band, bool faces)
    {
        if (Vector3.Distance(goal, _position) <= _values.TooCloseToTurn)
        {
            BorgSpring(point);

            return;
        }

        var direction = SteeringDirectionGame(point, goal, band);
        if (faces)
        {
            var target = MathF.Atan2(direction.X, direction.Z) + FacingOffset;
            HeadingTarget = target > MathF.PI ? target - 2 * MathF.PI : target;
        }

        BorgSteer(direction, 1);
    }

    void SteerGame(Vector3 direction, float scale)
    {
        if (_values.BorgPhysics)
        {
            BorgSteer(direction, scale);

            return;
        }

        var length = direction.Length();
        if (length < ZeroDistance)
        {
            length = ZeroDistance;
        }

        _throttle = Unit(length) * scale;
        ServoPitch(MathF.Asin(Math.Clamp(direction.Y / length, -1f, 1f)));
        ServoYaw(MathF.Atan2(direction.X, direction.Z));
        _lift = 0;
    }

    // The pitch servo (0x458ca0): the flight-path angle's error, the wanted pitch's feed-forward, the pitch rate, and the pitch error.
    void ServoPitch(float wanted)
    {
        var speed = _velocity.Length();
        var path = speed >= 1e-4f ? MathF.Asin(Math.Clamp(_velocity.Y / speed, -1f, 1f)) - wanted : 0;
        _pitchControl = Unit(
            _c.PitchServoPath * path
                + _c.PitchStiffness / _c.PitchControl * wanted
                + _c.PitchServoRate * _pitchRate
                + (-_pitch - wanted) * _c.PitchServoAngle
        );
    }

    // The yaw servo (0x458b80, Servo_Yaw): the yaw error and the roll and yaw state drive the roll control.
    void ServoYaw(float wanted)
    {
        var error = Wrap(_yaw - wanted);
        _rollControl = Unit(
            _c.YawServoError * error
                + _c.YawServoRollRate * _rollRate
                + _c.YawServoRollAngle * _roll
                + _c.YawServoYawRate * _yawRate
        );
    }

    // The throttle spring (0x4657b0, 0x458990): a critically damped brake toward `point` along the ship's forward axis,
    // turned into a throttle by the thrust terms. The ship's forward axis is the matrix's: the angles' (0x55d080).
    void Spring(Vector3 point, Vector3 pointVelocity)
    {
        var (_, _, forward) = Axes(_pitch, _yaw, _roll);
        var wanted =
            Vector3.Dot(forward, pointVelocity - _velocity) * _c.BrakeDamping
            + Vector3.Dot(forward, point - _position) * _c.BrakeStiffness;
        var force = Vector3.Dot(forward, _velocity) * _c.ForwardDrag + wanted;
        _throttle = Unit(force <= 0 ? force / _c.ReverseThrust : force / _c.ForwardThrust);
    }

    // The move maneuver (0x42e4a0, ordinary physics): steer to `point` (the carrot) with its velocity, `goal` the order's
    // last point. Inside tooCloseToTurn of the goal, or with the point inside the turning circle on its side, the ship
    // no longer steers: roll, pitch, and lift go to 0 and the spring brakes it toward the point's projection on its
    // heading. Otherwise it steers by the steering direction, and the spring replaces the throttle when the point lies
    // within 45 degrees of the heading. The point stands for the planned path's carrot.
    // stand-in: the warp state and multiplier (+0x688, +0x4ac) are not modelled, so the scale is 1; the center is the
    // position. A ship with Borg physics takes the Borg maneuver (BorgManeuver) instead.
    void Maneuver(Vector3 point, Vector3 pointVelocity, Vector3 goal, CombatBand band)
    {
        if (_values.BorgPhysics)
        {
            BorgManeuver(point, goal, band, true);

            return;
        }

        var (_, _, forward) = Axes(_pitch, _yaw, _roll);
        if (Vector3.Distance(goal, _position) <= _values.TooCloseToTurn)
        {
            Level();
            Spring(point, pointVelocity);

            return;
        }

        var toX = point.X - _position.X;
        var toZ = point.Z - _position.Z;
        var perpendicular = new Vector2(forward.Z, -forward.X);
        var squared = perpendicular.LengthSquared();
        if (squared > 0)
        {
            var radius = _c.TurnRadius;
            var right = perpendicular / MathF.Sqrt(squared);
            // the point's side: the circle that turns toward it, a turning radius to the left or the right
            var side = toX * right.X + toZ * right.Y <= 0 ? -1f : 1f;
            var (dx, dz) = (toX - side * right.X * radius, toZ - side * right.Y * radius);
            if (dx * dx + dz * dz < radius * radius)
            {
                Level();
                Spring(point, pointVelocity);

                return;
            }
        }

        var direction = SteeringDirectionGame(point, goal, band);
        HeadingTarget = MathF.Atan2(direction.X, direction.Z);
        SteerGame(direction, 1);
        var heading = new Vector2(forward.X, forward.Z);
        var toPoint = new Vector2(toX, toZ);
        if (Vector2.Dot(heading, toPoint) > toPoint.Length() * heading.Length() * HeadingCone)
        {
            Spring(point, pointVelocity);
        }
    }

    void Level() => _rollControl = _pitchControl = _lift = 0;
}
