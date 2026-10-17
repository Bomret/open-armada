using System;

namespace OpenArmada.Sandbox;

// How a combat sound fades with its distance from the listener, in the terms of Godot's inverse-distance model:
// the gain is UnitSize / distance, capped at MaxDb; MaxDistance 0 cuts no sound off.
// stand-in: the install holds no falloff. The unit size is a fifth of the camera's opening distance, so a sound
// there plays at about -14 dB; a close sound is capped at 0 dB; nothing is cut off at any distance.
public sealed record SoundFalloff(float UnitSize, float MaxDb, float MaxDistance)
{
    const double UnitShare = 0.2;
    const float Cap = 0;

    public static SoundFalloff For(double openingDistance) => new((float)(openingDistance * UnitShare), Cap, 0);

    // The level in dB at `distance`, as Godot computes it (1e-5 is Godot's CMP_EPSILON); -infinity once cut off.
    public double DbAt(double distance)
    {
        if (MaxDistance > 0 && distance > MaxDistance)
            return double.NegativeInfinity;
        return Math.Min(20 * Math.Log10(1 / (distance / UnitSize + 1e-5)), MaxDb);
    }
}
