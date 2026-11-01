using System;
using System.Numerics;
using OpenArmada.Formats;

namespace OpenArmada.Sandbox;

/// <summary>The geometry of a move order in Godot's frame, kept free of the scene so tests reach it.</summary>
public static class MoveGeometry
{
    /// <summary>
    /// The (X, Z) point where a ray meets the horizontal plane at <c>planeHeight</c>; null when the ray is parallel to
    /// the plane or points away from it.
    /// </summary>
    public static Vector2? PlaneTarget(Vector3 origin, Vector3 direction, float planeHeight)
    {
        var along = (planeHeight - origin.Y) / direction.Y;
        if (!float.IsFinite(along) || along <= 0)
            return null;
        var point = origin + direction * along;

        return new Vector2(point.X, point.Z);
    }

    /// <summary>
    /// The point of the map nearest to the (X, Z) <c>target</c>, each axis clamped on its own as the camera clamps its
    /// look-at point; an inverted pair is swapped, as the camera swaps it. <c>bounds</c> are in the map's frame; Godot's
    /// Z is mirrored, so it runs from -MaxZ to -MinZ.
    /// </summary>
    public static Vector2 ClampToMap(Vector2 target, MapBounds bounds) =>
        new(
            Math.Clamp(target.X, Math.Min(bounds.MinX, bounds.MaxX), Math.Max(bounds.MinX, bounds.MaxX)),
            Math.Clamp(target.Y, -Math.Max(bounds.MinZ, bounds.MaxZ), -Math.Min(bounds.MinZ, bounds.MaxZ))
        );

    // The frames. The game's frame has +x for a ship's side, +y up, and +z forward (Armada.exe stores a ship's
    // orientation as the rows right, up, front). The sandbox's frame is the game's with Z mirrored (S = diag(1, 1, -1)):
    // a world point (x, y, z) of the game is (x, y, -z) in the sandbox, and so is a model's vertex (SodBuilder). A
    // rotation of the game, seen in the sandbox, is conjugated by S: a turn about X or Y reverses its angle and a turn
    // about Z keeps it. So a model offset in the sandbox's frame turns by Ry(-yaw) * Rx(-pitch) * Rz(roll), with the
    // right-handed rotation matrices, where the game's 0x55d080 composes Ry(yaw) * Rx(pitch) * Rz(roll). The game's yaw
    // 0 faces +Z, so a bow faces -Z in the sandbox, and the sandbox's own bearing (0 faces +Z, positive toward +X) is
    // pi - yaw.

    /// <summary>
    /// The rotation of a ship at the game's <c>pitch</c>, <c>yaw</c>, and <c>roll</c> (0x55d080), in the sandbox's frame:
    /// it takes a model offset in Godot's frame (Z mirrored, as SodBuilder builds it) to the offset in the world. Roll
    /// turns about the ship's forward axis first, then pitch about its side axis, then yaw about the world's up axis.
    /// Yaw 0 puts the bow at -Z, a positive yaw turns it toward +X, a positive pitch lowers it, and a positive roll
    /// raises the right (+X) side.
    /// </summary>
    public static Quaternion Orientation(float pitch, float yaw, float roll) =>
        Quaternion.CreateFromAxisAngle(Vector3.UnitY, -yaw)
        * Quaternion.CreateFromAxisAngle(Vector3.UnitX, -pitch)
        * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, roll);

    /// <summary>
    /// The pitch, yaw, and roll of the orientation whose side, up, and forward axes are <c>right</c>, <c>up</c>, and
    /// <c>front</c> in the game's frame, as the game's decomposition 0x458ea0 reads them: pitch = -asin(front.y), yaw =
    /// atan2(front.x, front.z), roll = atan2(right.y, up.y). Where the forward axis is within 0.999 of straight up or
    /// down the pitch is -pi/2 or +pi/2, the roll 0, and the yaw atan2(-right.z, right.x).
    /// </summary>
    public static (float Pitch, float Yaw, float Roll) Decompose(Vector3 right, Vector3 up, Vector3 front)
    {
        if (Math.Abs((double)front.Y) > 0.999)
            return (front.Y > 0 ? -MathF.PI / 2 : MathF.PI / 2, MathF.Atan2(-right.Z, right.X), 0);

        return (-MathF.Asin(front.Y), MathF.Atan2(front.X, front.Z), MathF.Atan2(right.Y, up.Y));
    }

    /// <summary>
    /// The pose of a unit standing where the map placed it: its position and the angles the game's decomposition
    /// (<see cref="Decompose"/>) reads from its placed orientation. The orientation is the placed basis without its scale and
    /// mirror (<see cref="HardpointGeometry.Split"/> keeps those), turned from the sandbox's frame back into the
    /// game's: right = (x, y, -z) of the basis's X, up the same of its Y, and front = (-x, -y, z) of its Z, the
    /// inverse of SandboxPlan's mapping.
    /// </summary>
    /// <remarks>
    /// stand-in: the game decomposes a matrix as it finds it; what it does with a mirrored matrix is not recovered, so a
    /// mirrored basis is decomposed from its rotation part, which keeps the forward axis the model shows (the mirror
    /// sits on the side axis, <see cref="HardpointGeometry.Split"/>).
    /// </remarks>
    public static ShipPose PlacedPose(PlacedObject unit)
    {
        var (_, x, y, z) = HardpointGeometry.Split(unit);
        var (pitch, yaw, roll) = Decompose(
            new Vector3(x.X, x.Y, -x.Z),
            new Vector3(y.X, y.Y, -y.Z),
            new Vector3(-z.X, -z.Y, z.Z)
        );

        return new ShipPose
        {
            Position = unit.Position,
            Pitch = pitch,
            Yaw = yaw,
            Roll = roll,
        };
    }

    /// <summary>The sandbox's bearing (0 faces +Z, positive toward +X) of a bow at the game's <c>yaw</c>: pi - yaw, in (-pi, pi].</summary>
    public static float HeadingOfYaw(float yaw) => Wrap(MathF.PI - yaw);

    /// <summary>The game's yaw of a bow at the sandbox's <c>heading</c>: pi - heading, in (-pi, pi].</summary>
    public static float YawOfHeading(float heading) => Wrap(MathF.PI - heading);

    /// <summary>The angle in (-pi, pi].</summary>
    internal static float Wrap(float angle)
    {
        var wrapped = MathF.IEEERemainder(angle, 2 * MathF.PI);

        return wrapped <= -MathF.PI ? wrapped + 2 * MathF.PI : wrapped;
    }
}
