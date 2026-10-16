using System;
using System.Numerics;

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

    // The ShipMotion heading of a ship whose node basis has `axisZ` as its Z column. SodBuilder mirrors each
    // vertex's Z, so the model's +Z bow sits at node-local -Z and points along -axisZ in the world.
    public static float BowHeading(Vector3 axisZ) => MathF.Atan2(-axisZ.X, -axisZ.Z);

    // The orientation of a ship that started at `initial` and has since changed its heading by `headingChange`
    // and banks by `bank` (ShipMotion's pose). The heading turns about the world's Y; the bank rolls about the
    // ship's own bow axis, node-local -Z, and is negative in a left turn, so the left (-X) wing goes down.
    public static Quaternion Orientation(Quaternion initial, float headingChange, float bank) =>
        Quaternion.CreateFromAxisAngle(Vector3.UnitY, headingChange)
        * initial
        * Quaternion.CreateFromAxisAngle(-Vector3.UnitZ, bank);
}
