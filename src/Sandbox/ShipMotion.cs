using System;
using System.Numerics;

namespace OpenArmada.Sandbox;

// The movement values of a ship's physics definition. MaxRoll is in degrees; null when the definition has none.
public sealed record PhysicsValues(double ImpulseSpeed, double YawRotationRate, double? MaxRoll);

// A ship's pose: Heading is the yaw about +Y (0 faces +Z, a positive heading turns toward +X), Bank the roll
// about the ship's forward axis in radians (negative leans into a turn toward +X).
public readonly record struct ShipPose(Vector3 Position, float Heading, float Bank);

// The kinematic model of ADR 0003: a ship turns toward its target at yawRotationRate, travels at impulseSpeed
// along its heading, banks into the turn up to maxRoll, and stops at the target. The scene only asks it for a pose.
// stand-in: units are the map's world units per second; yawRotationRate is read as radians per second (the game
// stores angles in radians); the install defines no other movement rule.
public sealed class ShipMotion
{
    // stand-in: a target nearer than this counts as reached.
    const float Reached = 1e-4f;

    readonly PhysicsValues _physics;
    Vector3 _position;
    float _heading;
    float _bank;
    Vector2? _target; // X, Z

    public ShipMotion(PhysicsValues physics, Vector3 position, float heading)
    {
        _physics = physics;
        _position = position;
        _heading = heading;
    }

    // Replaces the previous order; the ship keeps its own height.
    public void SetTarget(float x, float z) => _target = new Vector2(x, z);

    public ShipPose Step(double seconds)
    {
        if (_target is { } target && seconds > 0) Advance(target, (float)seconds);
        return new ShipPose(_position, _heading, _bank);
    }

    void Advance(Vector2 target, float seconds)
    {
        var offset = target - new Vector2(_position.X, _position.Z);
        var distance = offset.Length();
        if (distance <= Reached) { Stop(target); return; }

        // turn the shorter way, never more than yawRotationRate allows
        var error = MathF.IEEERemainder(MathF.Atan2(offset.X, offset.Y) - _heading, 2 * MathF.PI);
        var most = (float)_physics.YawRotationRate * seconds;
        var turn = MathF.Abs(error) <= most ? error : MathF.CopySign(most, error);
        _heading += turn;
        error -= turn;
        _bank = _physics.MaxRoll is { } roll ? -(float)(roll * Math.PI / 180) * Math.Clamp(turn / most, -1, 1) : 0; // stand-in: no bank without maxRoll (the Borg physics definitions have none)

        // ponytail: a target inside two turning circles (2 * speed / yawRate) is reached by turning in place and
        // then driving straight; a ship never orbits it. Farther away it drives while turning. Smooth arcs or
        // slowing down if the pivot looks wrong.
        var circle = 2 * (float)(_physics.ImpulseSpeed / _physics.YawRotationRate);
        if (error != 0 && (distance < circle || MathF.Abs(error) >= MathF.PI / 2)) return;
        // never farther along the heading than the foot of the target's perpendicular, so the distance only shrinks
        var travel = MathF.Min((float)_physics.ImpulseSpeed * seconds, distance * MathF.Cos(error));
        if (travel >= distance) { Stop(target); return; }
        _position += new Vector3(MathF.Sin(_heading), 0, MathF.Cos(_heading)) * travel;
    }

    void Stop(Vector2 target)
    {
        _position = new Vector3(target.X, _position.Y, target.Y);
        _target = null;
        _bank = 0;
    }
}
