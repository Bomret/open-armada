using System;

namespace OpenArmada.Sandbox;

/// <summary>
/// How a combat sound fades with its distance from the listener, in the terms of Godot's inverse-distance model:
/// the gain is <see cref="UnitSize"/> / distance, capped at <see cref="MaxDb"/>. The player keeps Godot's default
/// max distance of 0, which cuts no sound off.
/// stand-in: the install holds no falloff. The unit size is a fifth of the camera's opening distance, so a sound
/// there plays at about -14 dB; a close sound is capped at 0 dB; nothing is cut off at any distance.
/// </summary>
public sealed record SoundFalloff
{
    /// <summary>The cap on every sound's level, in dB.</summary>
    public const float MaxDb = 0;

    /// <summary>
    /// The cutoff of the attenuation low-pass filter, in Hz.
    /// stand-in: the filter is off (Godot filters nothing at 20500 Hz), so a far sound is quieter but not muffled.
    /// </summary>
    public const float FilterCutoffHz = 20500;

    const double UnitShare = 0.2;

    /// <summary>The distance at which a sound plays at 0 dB before the cap, in world units.</summary>
    public required float UnitSize { get; init; }

    /// <summary>The falloff for a camera that opens <paramref name="openingDistance"/> from its target.</summary>
    public static SoundFalloff For(double openingDistance) => new() { UnitSize = (float)(openingDistance * UnitShare) };

    /// <summary>The level in dB at <paramref name="distance"/>, as Godot computes it (1e-5 is Godot's CMP_EPSILON).</summary>
    public double DbAt(double distance) => Math.Min(20 * Math.Log10(1 / (distance / UnitSize + 1e-5)), MaxDb);
}
