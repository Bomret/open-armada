using System;
using System.Numerics;

namespace OpenArmada.Sandbox;

/// <summary>
/// The rules of the game's collision separation (0x464910) that need no world: the contact of two ships
/// (0x464000), the shares of their speeds, the vertical push, and the collision turn's angle (0x464620). The world
/// applies the results through its push route.
/// </summary>
static class CollisionSeparation
{
    // No contact beyond this times the sum of the model radii (0x5ca8b8).
    const float ContactReach = 1.5f;

    // Centers closer than this coincide (0x5ca6c8).
    const float Coinciding = 0.01f;

    // The probe point of the hull surface point lies this many radii out from the center (0x5ca760).
    const float ProbeRadii = 4f;

    // The vertical push, once and once more per ship of borgPhysics (0x5ccddc).
    const float VerticalPush = 0.42f;

    // Speeds adding up to less than this give each ship half (0x464b0e).
    const float SlowSum = 0.01f;

    // A turn needs a share above this (0x5caa18) and a nose less than this angle off the other ship (0x5ccf18).
    const float TurnShare = 0.1f;
    const float TurnAngle = 1.57f;

    /// <summary>A ship's part of the contact: where its center is, its model radius, and its shield ellipsoid.</summary>
    public readonly record struct Body(Vector3 Center, float ModelRadius, ShieldEllipsoid? Shield);

    /// <summary>
    /// The contact of ship A with ship B: the normal from B toward A, the penetration, and whether the centers
    /// coincide (the penetration is then not written and no turn follows).
    /// </summary>
    public readonly record struct Contact(Vector3 Normal, float Penetration, bool Coincide);

    /// <summary>
    /// The contact of A with B (0x464000): none beyond 1.5 times the sum of the model radii; centers closer than 0.01
    /// give the normal (0, A's model radius, 0); else the penetration is the two hull surface points' distances from
    /// their centers, added, less the center distance, and the ships touch when it is above 0.
    /// </summary>
    public static Contact? Between(Body a, Body b)
    {
        var d = a.Center - b.Center;
        var reach = ContactReach * (a.ModelRadius + b.ModelRadius);
        var squared = d.LengthSquared();
        if (squared > reach * reach)
            return null;
        var distance = MathF.Sqrt(squared);
        if (distance < Coinciding)
            return new Contact(new Vector3(0, a.ModelRadius, 0), 0, true);
        var u = d / distance;
        var penetration =
            Extent(a, a.Center - u * (ProbeRadii * a.ModelRadius))
            + Extent(b, b.Center + u * (ProbeRadii * b.ModelRadius))
            - distance;

        return penetration > 0 ? new Contact(u * penetration, penetration, false) : null;
    }

    // The distance of a ship's hull surface point from its center (0x463f60). The ellipsoid's radius is sampled
    // along the direction of the probe point from the map origin, not toward the other ship (the game's quirk, as
    // docs/rules/forced-motion.md states). A ship without a shield ellipsoid is a sphere of its model radius.
    static float Extent(Body body, Vector3 probe)
    {
        if (body.Shield is not { } shield)
            return body.ModelRadius;
        var length = probe.Length();

        return length > 0 ? (shield.Radii * probe / length).Length() : 0;
    }

    /// <summary>
    /// The shares of A and B from their speeds: A's speed over the sum, B's over the sum, half each when the sum is
    /// below 0.01 (0x464b0e).
    /// </summary>
    public static (float A, float B) Shares(float speedA, float speedB)
    {
        var sum = speedA + speedB;

        return sum < SlowSum ? (0.5f, 0.5f) : (speedA / sum, speedB / sum);
    }

    /// <summary>
    /// The push vector of A's pass (0x464bb7): the contact normal plus a vertical push of 0.42, and 0.42 more per
    /// borgPhysics ship of the two, toward the lower ship; A's ship goes up at equal heights.
    /// </summary>
    public static Vector3 Push(Vector3 normal, float heightA, float heightB, bool borgA, bool borgB)
    {
        var up = VerticalPush * (1 + (borgA ? 1 : 0) + (borgB ? 1 : 0));

        return normal + new Vector3(0, heightA < heightB ? -up : up, 0);
    }

    /// <summary>
    /// The displacement of a ship's position out of an obstacle's sphere (0x4644b0), or null for none: when
    /// (<c>radius</c> + <c>shipRadius</c>)² exceeds the squared plane distance between the positions, the ship moves
    /// out along the plane direction from the obstacle's center by the difference of the sum and the plane
    /// distance. Height and velocity stay. Centers on one plane point have no direction: stand-in, no push (the game
    /// divides by the zero distance).
    /// </summary>
    public static Vector3? PushOut(Vector3 ship, float shipRadius, Vector3 center, float radius)
    {
        var (dx, dz) = (ship.X - center.X, ship.Z - center.Z);
        var reach = radius + shipRadius;
        var squared = dx * dx + dz * dz;
        if (!(reach * reach > squared) || !(squared > 0))
            return null;
        var distance = MathF.Sqrt(squared);
        var by = (reach - distance) / distance;

        return new Vector3(dx * by, 0, dz * by);
    }

    /// <summary>
    /// The heading change of the collision turn of ship S away from ship O (0x464620), in radians, or null for no
    /// turn: the share must be above 0.1, S must not be borgPhysics, and S's nose must point less than 1.57 off O.
    /// The game rotates the nose toward the push vector V by (penetration / center distance) times
    /// <c>maxPrevention</c> over that angle, about the axis of nose and V; only the heading part of that rotation
    /// applies (ADR 0009), which is how far the rotated nose's heading lies from S's.
    /// </summary>
    public static float? Turn(
        float heading,
        Vector3 positionS,
        Vector3 positionO,
        float share,
        bool borg,
        Vector3 v,
        float penetration,
        float maxPrevention
    )
    {
        if (share <= TurnShare || borg)
            return null;
        var f = new Vector3(MathF.Sin(heading), 0, MathF.Cos(heading));
        var e = positionS - positionO;
        var distance = e.Length();
        if (distance <= 0)
            return null;
        var alpha = MathF.Acos(Math.Clamp(Vector3.Dot(f, -e) / distance, -1, 1));
        if (!(alpha < TurnAngle) || alpha <= 0)
            return null;
        var cross = Vector3.Cross(f, v);
        var length = cross.Length();
        if (length <= 0)
            return null;
        var phi = penetration / distance * maxPrevention / alpha;
        var rotated = f * MathF.Cos(phi) + Vector3.Cross(cross / length, f) * MathF.Sin(phi);
        var delta = MathF.Atan2(rotated.X, rotated.Z) - heading;
        delta -= MathF.Round(delta / (2 * MathF.PI)) * 2 * MathF.PI;

        return float.IsFinite(delta) ? delta : null;
    }
}
