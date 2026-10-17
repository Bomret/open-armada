using System.Numerics;

namespace OpenArmada.Sandbox;

// Where a drawn beam sits: its Center and three axes. X runs along the beam and is as long as the beam; Y and Z
// are unit vectors perpendicular to it and to each other. The view takes them as the columns of a basis, so the
// stretch lies along the beam, whichever way it points.
public readonly record struct BeamPose
{
    public required Vector3 Center { get; init; }
    public required Vector3 X { get; init; }
    public required Vector3 Y { get; init; }
    public required Vector3 Z { get; init; }
}

// The vector math of drawing a shot, in plain numbers so it is tested without Godot.
public static class ShotGeometry
{
    // stand-in: a missed shot passes this many target radii beside the target's center; the game's miss aim
    // (0x50a7c0) is not traced.
    const float MissRadii = 2f;

    // The pose of a beam from `start` to `end`; null when they are the same point.
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

    // The point a shot that missed is aimed at: beside the target's center, off the line from `from`.
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
