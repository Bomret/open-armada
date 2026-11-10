using System.Numerics;

namespace OpenArmada.Sandbox;

/// <summary>
/// Where a drawn beam sits: its Center and three axes. X runs along the beam and is as long as the beam; Y and Z
/// are unit vectors perpendicular to it and to each other. The view takes them as the columns of a basis, so the
/// stretch lies along the beam, whichever way it points.
/// </summary>
public readonly record struct BeamPose
{
    /// <summary>The midpoint between the beam's start and end.</summary>
    public required Vector3 Center { get; init; }

    /// <summary>The axis along the beam, from start to end, as long as the beam.</summary>
    public required Vector3 X { get; init; }

    /// <summary>A unit vector perpendicular to the beam and to <see cref="Z"/>.</summary>
    public required Vector3 Y { get; init; }

    /// <summary>A unit vector perpendicular to the beam and to <see cref="Y"/>.</summary>
    public required Vector3 Z { get; init; }
}

/// <summary>The vector math of drawing a shot, in plain numbers so it is tested without Godot.</summary>
public static class ShotGeometry
{
    // stand-in: a missed shot passes this many target radii beside the target's center; the game's miss aim
    // (0x50aab0) is not traced.
    const float MissRadii = 2f;

    /// <summary>The pose of a beam from <c>start</c> to <c>end</c>; null when they are the same point.</summary>
    public static BeamPose? Beam(Vector3 start, Vector3 end)
    {
        var along = end - start;
        if (along.LengthSquared() < 1e-6f)
            return null;
        var x = Vector3.Normalize(along);
        var up = System.MathF.Abs(x.Y) > 0.99f ? -Vector3.UnitZ : Vector3.UnitY;
        var z = Vector3.Normalize(Vector3.Cross(x, up));

        return new BeamPose
        {
            Center = (start + end) / 2,
            X = along,
            Y = Vector3.Cross(z, x),
            Z = z,
        };
    }

    /// <summary>
    /// The scale of a special weapon's beam model over a beam of <c>distance</c>: the radius scale across and the
    /// distance over the length scale along the model's Z axis (0x4ddf80, 0x4da7a0). A length scale of 0 or less is a
    /// stand-in of 1, so the beam still spans its distance.
    /// </summary>
    public static Vector3 BeamModelScale(float distance, float radiusScale, float lengthScale) =>
        new(radiusScale, radiusScale, distance / (lengthScale > 0 ? lengthScale : 1));

    /// <summary>The point a shot that missed is aimed at: beside the target's center, off the line from <c>from</c>.</summary>
    public static Vector3 MissAim(Vector3 from, Vector3 center, float radius)
    {
        var line = center - from;
        var up =
            line.LengthSquared() > 0 && System.MathF.Abs(Vector3.Normalize(line).Y) > 0.99f
                ? Vector3.UnitX
                : Vector3.UnitY;
        var side = Vector3.Cross(line, up);
        side = side.LengthSquared() < 1e-12f ? Vector3.UnitX : Vector3.Normalize(side);

        return center + side * (radius * MissRadii);
    }
}
