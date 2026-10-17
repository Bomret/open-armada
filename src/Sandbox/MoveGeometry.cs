using System;
using System.Numerics;
using OpenArmada.Formats;

namespace OpenArmada.Sandbox;

// The geometry of a move order in Godot's frame, kept free of the scene so tests reach it.
public static class MoveGeometry
{
    // The (X, Z) point where a ray meets the horizontal plane at `planeHeight`; null when the ray is parallel to
    // the plane or points away from it.
    public static Vector2? PlaneTarget(Vector3 origin, Vector3 direction, float planeHeight)
    {
        var along = (planeHeight - origin.Y) / direction.Y;
        if (!float.IsFinite(along) || along <= 0)
            return null;
        var point = origin + direction * along;

        return new Vector2(point.X, point.Z);
    }

    // The point of the map nearest to the (X, Z) `target`, each axis clamped on its own as the camera clamps its
    // look-at point; an inverted pair is swapped, as the camera swaps it. `bounds` are in the map's frame; Godot's
    // Z is mirrored, so it runs from -MaxZ to -MinZ.
    public static Vector2 ClampToMap(Vector2 target, MapBounds bounds) =>
        new(
            Math.Clamp(target.X, Math.Min(bounds.MinX, bounds.MaxX), Math.Max(bounds.MinX, bounds.MaxX)),
            Math.Clamp(target.Y, -Math.Max(bounds.MinZ, bounds.MaxZ), -Math.Min(bounds.MinZ, bounds.MaxZ))
        );

    // The ShipMotion heading of a ship whose node basis has `axisZ` as its Z column. SodBuilder mirrors each
    // vertex's Z, so the model's +Z bow sits at node-local -Z and points along -axisZ in the world.
    public static float BowHeading(Vector3 axisZ) => MathF.Atan2(-axisZ.X, -axisZ.Z);

    // The pose of a unit standing where the map placed it: its position, its bow heading, no bank.
    public static ShipPose PlacedPose(Vector3 position, Vector3 axisZ) =>
        new()
        {
            Position = position,
            Heading = BowHeading(axisZ),
            Bank = 0,
        };

    // The orientation of a ship that started at `initial` and has since changed its heading by `headingChange`
    // and banks by `bank` (ShipMotion's pose). The heading turns about the world's Y; the bank rolls about the
    // ship's own bow axis, node-local -Z, and is negative in a left turn, so the left (-X) wing goes down.
    public static Quaternion Orientation(Quaternion initial, float headingChange, float bank) =>
        Quaternion.CreateFromAxisAngle(Vector3.UnitY, headingChange)
        * initial
        * Quaternion.CreateFromAxisAngle(-Vector3.UnitZ, bank);
}
