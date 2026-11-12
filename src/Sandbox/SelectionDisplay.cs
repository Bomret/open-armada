using System;
using System.Collections.Generic;
using System.Linq;
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

/// <summary>The shield bar of docs/rules/selection-display.md: its gray back, its fill, and the fill color (red, green, blue).</summary>
public readonly record struct ShieldBar(ScreenRect Back, ScreenRect Fill, Vector3 Color);

/// <summary>The crew dot of docs/rules/selection-display.md: where it is drawn and the crew status that colors it.</summary>
public readonly record struct CrewDot(ScreenRect Rect, CrewStatus Status);

/// <summary>A unit that shows the display: its shield bar's opacity and whether it shows the crew dot.</summary>
public readonly record struct ShownUnit(PlacedObject Unit, double Opacity, bool Dot);

/// <summary>
/// The hover state of the selection display (docs/rules/selection-display.md, display update 0x499f70), stepped once per
/// frame: the hovered unit, its team as the group key, and the display fade.
/// </summary>
public sealed record HoverState
{
    // The hover hold of 0x60d7f0, in seconds.
    const double Hold = 0.4;

    /// <summary>The hovered unit; null when there is none.</summary>
    public PlacedObject? Hovered { get; init; }

    /// <summary>The group key: the hovered unit's current team, 0 without a hovered unit.</summary>
    public int Team { get; init; }

    /// <summary>The display fade, from 0 to 1.</summary>
    public double Fade { get; init; }

    /// <summary>The seconds since the pick last found the hovered unit.</summary>
    public double Lost { get; init; }

    /// <summary>
    /// The state after a frame of <c>delta</c> seconds whose pick found <c>pick</c> (null: no unit). A pick hovers its
    /// unit at once; without one the hovered unit stays until the hold has passed. A hovered unit that is not among
    /// <c>units</c> (destroyed or removed) ends the hover on this frame. The fade starts over at 0 when the group
    /// key changes and otherwise grows by <c>delta</c> up to 1. <c>teamOf</c> reads a unit's current team (INV-0012).
    /// </summary>
    public HoverState Step(
        PlacedObject? pick,
        IEnumerable<PlacedObject> units,
        double delta,
        Func<PlacedObject, int> teamOf
    )
    {
        var (hovered, lost) = pick != null ? (pick, 0.0) : (Hovered, Lost + delta);
        if (hovered != null && (lost >= Hold || !units.Contains(hovered)))
            hovered = null;
        var next = this with { Hovered = hovered, Lost = lost, Team = hovered == null ? 0 : teamOf(hovered) };

        return next with
        {
            Fade = next.Team != Team ? 0 : Math.Min(1, Fade + delta),
        };
    }
}

/// <summary>
/// The game's in-world selection display (docs/rules/selection-display.md), placed around a unit's screen box. Sizes are
/// game pixels times <c>scale</c>, the display scale of the screen.
/// </summary>
public static class SelectionDisplay
{
    const float BarHeight = 4;

    const float DotSize = 7;

    const float DotRise = 8;

    /// <summary>
    /// The units that show the display (0x49b6a0), each once in the order of <c>units</c>: the hovered unit at
    /// opacity 1, and every other unit of its team at the display fade; a hovered unit of team 0 is shown alone. A
    /// shown unit that is selected also shows its crew dot, at the display fade. Nothing is shown without a hovered
    /// unit, whatever is selected (INV-0006). <c>teamOf</c> reads a unit's current team (INV-0012).
    /// </summary>
    public static IEnumerable<ShownUnit> Shown(
        HoverState hover,
        IEnumerable<PlacedObject> units,
        IEnumerable<PlacedObject> selected,
        Func<PlacedObject, int> teamOf
    )
    {
        if (hover.Hovered is not { } hovered)
            yield break;
        var chosen = selected.ToHashSet();
        foreach (var unit in units)
        {
            if (unit == hovered || (hover.Team != 0 && teamOf(unit) == hover.Team))
                yield return new ShownUnit(unit, unit == hovered ? 1 : hover.Fade, chosen.Contains(unit));
        }
    }

    /// <summary>
    /// The alpha a part is drawn at for its opacity: (opacity − 0.5) × 2 kept within 0 and 1, so a part shows only
    /// while its opacity is above 0.5.
    /// </summary>
    public static float AlphaOf(double opacity) =>
        // stand-in: the game scales the part's color by (opacity − 0.5) and shows it only above 0.5; how bright its
        // renderer then draws it is unknown, so the excess is stretched to a full alpha range instead
        (float)Math.Clamp((opacity - 0.5) * 2, 0, 1);

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
    /// The crew dot (0x49afe0): a DotSize square whose top-left corner sits on the box's right edge, DotRise above
    /// its bottom edge, in the unit's crew status. Null for a unit whose game maximum crew (maximumCrew, crewCost where
    /// missing) is 0, whatever its shield maximum.
    /// </summary>
    public static CrewDot? CrewDotOf(ScreenBox box, UnitCombatState state, float scale) =>
        state.Profile.DefinedMaximumCrew == 0
            ? null
            : new CrewDot(
                new ScreenRect(new Vector2(box.Right, box.Bottom - DotRise * scale), new Vector2(DotSize * scale)),
                CombatRules.CrewStatusOf(state)
            );
}
