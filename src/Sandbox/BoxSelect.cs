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
