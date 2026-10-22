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
/// The spray of a damage emitter on simulated time (0x575400 spray, 0x5746d0 particle; no Godot, INV-0011): each
/// tick the timer counts down by the tick's seconds, and when it is below 0 one particle spawns and the timer is set
/// to the period (0x57549f; the overshoot is not carried). A tick spawns at most one particle, and a period of 0
/// means one per tick. A particle starts at the node's frame, its direction the node's +Z turned by three angles
/// (roll about Z, then pitch about X, then yaw about Y, as <see cref="Quaternion.CreateFromYawPitchRoll"/> composes
/// them), each uniform in plus or minus the spread angle (0x5746d0). It moves along that direction at the velocity
/// and dies at the lifespan.
/// stand-in: the tick is the fixed <see cref="CombatWorld.Tick"/> of simulated time (the game steps the timer by the
/// frame's seconds; the fixed tick keeps the rate the same at any frame rate and step size, as the combat world's
/// ticks do); the timer starts at 0, so the first tick spawns (the start value is set outside 0x575400); the order
/// of the three turns; the game's source of randomness (the play run fixes this one, INV-0008).
/// </summary>
/// <param name="definition">The <c>@emitter</c> block (a spray).</param>
/// <param name="random">Gives numbers in 0 up to 1.</param>
public sealed class SprayEmitter(SpriteEmitter definition, Func<double> random)
{
    // Floating-point slack for the sums of frame steps that make whole ticks, and for a lifespan met at a tick.
    const double Slack = 1e-9;

    readonly SpriteEmitter _definition = definition;
    readonly Func<double> _random = random;
    readonly List<SprayParticle> _particles = new();

    // The game's timer is a 32-bit float (0x575425), so a period that is a whole number of ticks lands on 0 or just below
    // by float arithmetic.
    float _timer;
    double _pending;

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
        _pending += Math.Max(0, seconds);
        while (_pending + Slack >= CombatWorld.Tick)
        {
            _pending -= CombatWorld.Tick;
            foreach (var particle in _particles)
            {
                particle.Age += CombatWorld.Tick;
                particle.Position += particle.Velocity * (float)CombatWorld.Tick;
            }

            _particles.RemoveAll(p => p.Age >= _definition.Lifespan - Slack);
            _timer -= (float)CombatWorld.Tick;
            if (_timer < 0)
            {
                _particles.Add(Spawn(frame));
                _timer = (float)_definition.FiringPeriod;
            }
        }
    }

    /// <summary>The particle's width or height: the sprite node's <paramref name="spriteSize"/> plus the growth so far.</summary>
    public double Size(double spriteSize, SprayParticle particle) =>
        spriteSize + _definition.ExpansionRate * particle.Age;

    /// <summary>Whether a particle of the sizes <paramref name="width"/> and <paramref name="height"/> (from <see cref="Size"/>) is drawn: a size at or below 0 is not (0x584040).</summary>
    public static bool Drawn(double width, double height) => width > 0 && height > 0;

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
