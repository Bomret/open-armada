using System;
using System.Collections.Generic;
using System.Numerics;

namespace OpenArmada.Sandbox;

/// <summary>A unit's screen box in pixels: the edges of the smallest screen rectangle around the unit.</summary>
public readonly record struct ScreenBox(float Left, float Top, float Right, float Bottom)
{
    /// <summary>The box width in pixels, both edges counted.</summary>
    public float Width => Right - Left + 1;
}

/// <summary>A rectangle in screen pixels.</summary>
public readonly record struct ScreenRect(Vector2 Position, Vector2 Size);

/// <summary>The shield bar of docs/selection-display.md: its gray back, its fill, and the fill color (red, green, blue).</summary>
public readonly record struct ShieldBar(ScreenRect Back, ScreenRect Fill, Vector3 Color);

/// <summary>The crew dot of docs/selection-display.md: where it is drawn and the crew status that colors it.</summary>
public readonly record struct CrewDot(ScreenRect Rect, CrewStatus Status);

/// <summary>
/// The game's in-world selection display (docs/selection-display.md), placed around a unit's screen box. Sizes are
/// game pixels times <c>scale</c>, the display scale of the screen.
/// </summary>
public static class SelectionDisplay
{
    const float BarHeight = 4;

    const float DotSize = 7;

    const float DotRise = 8;

    /// <summary>
    /// The units that show the display, each with whether it shows the crew dot: every selected unit with its dot,
    /// then the hovered unit, when it is not selected, with the shield bar alone.
    /// </summary>
    public static IEnumerable<(T Unit, bool Dot)> Shown<T>(IEnumerable<T> selected, T? hovered)
        where T : class
    {
        var any = false;
        foreach (var unit in selected)
        {
            any |= unit == hovered;
            yield return (unit, true);
        }

        if (hovered != null && !any)
            yield return (hovered, false);
    }

    /// <summary>
    /// The point the cursor hovers: the mouse position while the cursor is inside the window of a run that has a
    /// mouse; null otherwise, such as in a screenshot or headless run. Inside follows the edge scroll's rule,
    /// CameraRig.CursorInside.
    /// </summary>
    public static Vector2? HoverPoint(Vector2 mouse, bool mouseInWindow, bool confined, bool hasMouse) =>
        hasMouse && CameraRig.CursorInside(mouseInWindow, confined) ? mouse : null;

    /// <summary>
    /// The screen box around a unit's box corners as projected to a screen of <c>screen</c> pixels (null: the corner
    /// lies behind the camera). Null when a corner lies behind the camera or the box lies wholly off screen.
    /// </summary>
    public static ScreenBox? BoxOf(IEnumerable<Vector2?> corners, Vector2 screen)
    {
        var (min, max) = (new Vector2(float.MaxValue), new Vector2(float.MinValue));
        foreach (var corner in corners)
        {
            if (corner is not { } c)
                return null;
            min = Vector2.Min(min, c);
            max = Vector2.Max(max, c);
        }

        if (max.X < 0 || max.Y < 0 || min.X >= screen.X || min.Y >= screen.Y)
            return null;

        return new ScreenBox(min.X, min.Y, max.X, max.Y);
    }

    /// <summary>
    /// The shield bar above the box: as wide as the box, BarHeight high, one pixel clear of the box's top edge, its
    /// fill as wide as the shield ratio (shields over maxHealth, kept within 0 and 1) and shading from red when the
    /// shields are gone to green when they are full. Null for a unit whose maxHealth is not above 0.
    /// </summary>
    public static ShieldBar? ShieldBarOf(ScreenBox box, UnitCombatState state, float scale)
    {
        // stand-in: the game shows the bar by a flag and a value whose meaning is unknown; a unit without a shield
        // maximum shows none
        if (state.Profile.MaxHealth <= 0)
            return null;
        var ratio = (float)Math.Clamp(state.Shields / state.Profile.MaxHealth, 0, 1);
        var height = BarHeight * scale;
        var at = new Vector2(box.Left, box.Top - height - scale);

        return new ShieldBar(
            new ScreenRect(at, new Vector2(box.Width, height)),
            new ScreenRect(at, new Vector2(box.Width * ratio, height)),
            new Vector3(1 - ratio, ratio, 0)
        );
    }

    /// <summary>
    /// The crew dot: a DotSize square whose top-left corner sits on the box's right edge, DotRise above its bottom
    /// edge, in the unit's crew status.
    /// </summary>
    public static CrewDot CrewDotOf(ScreenBox box, UnitCombatState state, float scale) =>
        new(
            new ScreenRect(new Vector2(box.Right, box.Bottom - DotRise * scale), new Vector2(DotSize * scale)),
            CombatRules.CrewStatusOf(state)
        );
}
