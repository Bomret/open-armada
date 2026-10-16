using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace OpenArmada.Sandbox;

// The mouse rules of selection, over screen points.
public static class BoxSelect
{
    // stand-in: a drag shorter than 4 pixels in both directions is a click (the install defines none).
    const float ClickPixels = 4;

    public static bool IsClick(Vector2 from, Vector2 to) => MathF.Abs(to.X - from.X) < ClickPixels && MathF.Abs(to.Y - from.Y) < ClickPixels;

    // The item a click ray hits first: the one with the smallest entry distance (null: the ray misses it). A
    // box that holds the camera has no entry distance: a unit's box takes no click, but a non-unit object
    // (a planet) blocks at the entry distance of its bounding sphere (ShapeDistance, see SphereEntry), because
    // its box is bigger than its shape. A sphere that misses the ray or holds the camera blocks nothing.
    public static T? Nearest<T>(IEnumerable<(T Item, float? Distance, bool IsUnit, bool CameraInside, float? ShapeDistance)> entries)
    {
        T? hit = default;
        var nearest = float.MaxValue;
        foreach (var (item, distance, isUnit, cameraInside, shapeDistance) in entries)
        {
            var at = cameraInside ? (isUnit ? null : shapeDistance) : distance;
            if (at is { } d && d < nearest) { nearest = d; hit = item; }
        }
        return hit;
    }

    // The distance along the ray (`direction` a unit vector) at which it enters the sphere; null when it
    // misses the sphere, the sphere lies behind the origin, or the origin is inside the sphere.
    public static float? SphereEntry(Vector3 center, float radius, Vector3 origin, Vector3 direction)
    {
        var toCenter = center - origin;
        var along = Vector3.Dot(toCenter, direction);
        var offAxis = toCenter.LengthSquared() - along * along;
        var half = radius * radius - offAxis;
        if (half < 0 || toCenter.LengthSquared() <= radius * radius) return null;
        var entry = along - MathF.Sqrt(half);
        return entry >= 0 ? entry : null;
    }

    // The items whose point lies in the box between the two corners, whichever way the drag went. An item
    // behind the camera is never inside: its projected point is mirrored.
    public static IReadOnlyList<T> Inside<T>(IEnumerable<(T Item, Vector2 Point, bool InFront)> entries, Vector2 from, Vector2 to)
    {
        var min = Vector2.Min(from, to);
        var max = Vector2.Max(from, to);
        return entries.Where(e => e.InFront && e.Point.X >= min.X && e.Point.X <= max.X && e.Point.Y >= min.Y && e.Point.Y <= max.Y)
            .Select(e => e.Item).ToList();
    }
}
