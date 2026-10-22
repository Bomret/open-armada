using System;
using System.Collections.Generic;
using System.Numerics;
using OpenArmada.Formats;

namespace OpenArmada.Sandbox;

/// <summary>
/// An emitter node's world frame: where it stands and its local X, Y, and Z axes in the world, each of length 1.
/// The axes are the node's own, so a mirrored model passes the axes its own +Z maps to.
/// </summary>
public readonly record struct EmitterFrame(Vector3 Position, Vector3 X, Vector3 Y, Vector3 Z);

/// <summary>One live particle of a <see cref="SprayEmitter"/>: where it is, its velocity, and the seconds since it spawned.</summary>
public sealed class SprayParticle
{
    /// <summary>The particle's world position.</summary>
    public Vector3 Position { get; internal set; }

    /// <summary>The particle's velocity in world units per second.</summary>
    public Vector3 Velocity { get; internal init; }

    /// <summary>The simulated seconds since the particle spawned; its sprite's animation plays from 0.</summary>
    public double Age { get; internal set; }
}

/// <summary>
/// The spray of a damage emitter on simulated time (0x575400 spray, 0x5746d0 particle; no Godot, INV-0011): a timer
/// counts down by each step's seconds, and when it drops below 0 one particle spawns and the timer takes the period
/// on (the overshoot stays, so the rate holds at any frame rate whose step is no longer than the period); a step
/// spawns at most one particle, and a period of 0 means one per step. A particle starts at the node's frame, its
/// direction the node's +Z turned by three angles (roll about Z, then pitch about X, then yaw about Y, as
/// <see cref="Quaternion.CreateFromYawPitchRoll"/> composes them), each uniform in plus or minus the spread angle.
/// It moves along that direction at the velocity and dies at the lifespan.
/// stand-in: the timer starts at 0, so the first step spawns; the order of the three turns; the game's source of
/// randomness (the play run fixes this one, INV-0008).
/// </summary>
public sealed class SprayEmitter
{
    // Floating-point slack for sums of equal steps (ten steps of 0.1 s make 1 s).
    const double Slack = 1e-9;

    readonly SpriteEmitter _definition;
    readonly Func<double> _random;
    readonly List<SprayParticle> _particles = new();
    double _timer;

    /// <summary>
    /// <paramref name="definition"/> is the <c>@emitter</c> block (a spray); <paramref name="random"/> gives numbers in
    /// 0 up to 1.
    /// </summary>
    public SprayEmitter(SpriteEmitter definition, Func<double> random)
    {
        _definition = definition;
        _random = random;
    }

    /// <summary>The live particles, oldest first.</summary>
    public IReadOnlyList<SprayParticle> Particles => _particles;

    /// <summary>
    /// Advances the spray by <paramref name="seconds"/> of simulated time. While <paramref name="showing"/> is false
    /// (the emitter's damage node is hidden) nothing moves, ages, or spawns: live particles stay as they are.
    /// <paramref name="frame"/> is the node's world frame now.
    /// </summary>
    public void Step(double seconds, bool showing, EmitterFrame frame)
    {
        if (!showing)
            return;
        foreach (var particle in _particles)
        {
            particle.Age += seconds;
            particle.Position += particle.Velocity * (float)seconds;
        }

        _particles.RemoveAll(p => p.Age >= _definition.Lifespan - Slack);
        _timer -= seconds;
        if (_timer < -Slack)
        {
            _particles.Add(Spawn(frame));
            _timer = Math.Max(_timer + _definition.FiringPeriod, 0);
        }
    }

    /// <summary>The particle's width or height: the sprite node's <paramref name="spriteSize"/> plus the growth so far.</summary>
    public double Size(double spriteSize, SprayParticle particle) =>
        spriteSize + _definition.ExpansionRate * particle.Age;

    /// <summary>Whether the particle is drawn: a size at or below 0 is not (0x584040).</summary>
    public bool Drawn(SprayParticle particle, double width, double height) =>
        Size(width, particle) > 0 && Size(height, particle) > 0;

    SprayParticle Spawn(EmitterFrame frame)
    {
        double Angle() => (_random() * 2 - 1) * _definition.SpreadAngle * Math.PI / 180;
        var yaw = (float)Angle();
        var pitch = (float)Angle();
        var roll = (float)Angle();
        var local = Vector3.Transform(Vector3.UnitZ, Quaternion.CreateFromYawPitchRoll(yaw, pitch, roll));
        var direction = frame.X * local.X + frame.Y * local.Y + frame.Z * local.Z;

        return new SprayParticle { Position = frame.Position, Velocity = direction * (float)_definition.Velocity };
    }
}
