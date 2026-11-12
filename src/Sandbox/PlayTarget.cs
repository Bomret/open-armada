using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace OpenArmada.Sandbox;

/// <summary>A rectangle of whole pixels: its top-left pixel and its size, each at least one.</summary>
public readonly record struct CropRect(int X, int Y, int Width, int Height);

/// <summary>The checks of a play step that sends input events at a unit or a point: where to aim and whether the click picks what the step names.</summary>
public static class PlayTarget
{
    const long MaxImageSide = 16384;

    /// <summary>
    /// The screen point a step aimed at <c>name</c> sends its events to: <c>projected</c>, the point's viewport
    /// coordinates, when the point lies inside <c>viewport</c> and not behind the camera (a point behind the camera
    /// projects mirrored, so its coordinates mean nothing). Otherwise the problem.
    /// </summary>
    public static (Vector2? Point, string? Problem) Of(string name, Vector2 projected, bool behind, Vector2 viewport)
    {
        if (behind)
            return (null, $"'{name}' is behind the camera");
        if (projected.X < 0 || projected.Y < 0 || projected.X >= viewport.X || projected.Y >= viewport.Y)
            return (null, $"'{name}' is off screen");

        return (projected, null);
    }

    /// <summary>
    /// The problem with a hover on <c>name</c> aimed at <c>point</c>, a point <see cref="Of"/> accepted, or null. A
    /// point on a viewport edge pixel hovers nothing (<see cref="SelectionDisplay.PlayHoverPoint"/>), so a step that
    /// moved the play pointer there would show no selection display.
    /// </summary>
    public static string? HoverProblem(string name, Vector2 point, Vector2 viewport) =>
        SelectionDisplay.PlayHoverPoint(point, viewport) == null ? $"'{name}' lies on the viewport edge" : null;

    /// <summary>
    /// The problem with a click on <c>name</c> whose pick under the cursor is <c>picked</c> (null: no unit), or null
    /// when the pick is the named unit. A step that sends such a click would select or order something else.
    /// </summary>
    public static string? PickProblem(string name, string? picked) =>
        picked == name ? null : $"the click on '{name}' would pick {(picked == null ? "no unit" : $"'{picked}'")}";

    /// <summary>
    /// The problem with an attack step on <c>name</c> when each entry of <c>hostile</c> says whether <c>name</c> is
    /// hostile to one selected unit, or null when every selected unit takes the attack order. A selected unit that
    /// finds the unit not hostile takes a move order from the same click, and nothing selected takes none.
    /// </summary>
    public static string? AttackProblem(string name, IReadOnlyCollection<bool> hostile) =>
        hostile.Count == 0 ? $"no unit is selected to attack '{name}'"
        : hostile.All(h => h) ? null
        : $"'{name}' is not hostile to every selected unit";

    /// <summary>
    /// The problem with a transport step on <c>name</c> when each entry of <c>takes</c> says whether one selected unit
    /// takes a transport order on it, or null when at least one does. Nothing selected takes none.
    /// </summary>
    public static string? TransportProblem(string name, IReadOnlyCollection<bool> takes) =>
        takes.Count == 0 ? $"no unit is selected to transport to '{name}'"
        : takes.Any(t => t) ? null
        : $"no selected unit can take a transport order on '{name}'";

    /// <summary>
    /// The two lists <see cref="TransportProblem(string, IReadOnlyCollection{bool}, IReadOnlyCollection{bool})"/> reads,
    /// one entry per selected unit in order: whether it takes a transport order on <c>target</c> (it is not
    /// transporting, and <see cref="CombatRules.TransportOrderOk"/> accepts the pair; a transporting unit takes none)
    /// and whether it is transporting. A unit or target the world has lost (null) takes no order and is not transporting.
    /// </summary>
    public static (List<bool> Takes, List<bool> Transporting) TransportSelection(
        IEnumerable<UnitCombatState?> selected,
        UnitCombatState? target
    )
    {
        var units = selected.ToList();

        return (
            units
                .Select(s => s is { Transporting: false } && target != null && CombatRules.TransportOrderOk(s, target))
                .ToList(),
            units.Select(s => s?.Transporting == true).ToList()
        );
    }

    /// <summary>The problem with a transport step when the chord is unbound (<c>bound</c> false), or null.</summary>
    public static string? TransportChordProblem(bool bound) =>
        bound ? null : "the transport chord is unbound: Input.map or its transport block is missing";

    /// <summary>
    /// The problem with a transport step on <c>name</c> when each entry of <c>transporting</c> says whether one selected
    /// unit is transporting and each entry of <c>takes</c> whether one selected unit not transporting takes the order, or
    /// null. A selection of only transporting units fails first: the chord would cancel them, enter no mode, and the
    /// click would select. Otherwise <see cref="TransportProblem(string, IReadOnlyCollection{bool})"/> answers.
    /// </summary>
    public static string? TransportProblem(
        string name,
        IReadOnlyCollection<bool> takes,
        IReadOnlyCollection<bool> transporting
    ) =>
        transporting.Count > 0 && transporting.All(t => t)
            ? $"every selected unit is transporting: the transport chord would cancel them, not wait for '{name}'"
            : TransportProblem(name, takes);

    /// <summary>
    /// The pixels a <c>shot</c> step aimed at a unit keeps: the unit's screen <c>box</c> grown past each edge by half its
    /// longer side, so effects around the hull such as a fireball or a shield flash stay in it, then every pixel the
    /// grown box touches, clipped to <c>viewport</c>. The box lies at least partly inside the viewport.
    /// </summary>
    public static CropRect CropOf(ScreenBox box, Vector2 viewport)
    {
        var pad = Math.Max(box.Right - box.Left, box.Bottom - box.Top) / 2;
        // clipped before the cast: a corner just in front of the camera projects past what an int holds
        var left = (int)MathF.Max(0, MathF.Floor(box.Left - pad));
        var top = (int)MathF.Max(0, MathF.Floor(box.Top - pad));
        var right = (int)MathF.Min(MathF.Floor(viewport.X), MathF.Floor(box.Right + pad) + 1);
        var bottom = (int)MathF.Min(MathF.Floor(viewport.Y), MathF.Floor(box.Bottom + pad) + 1);

        return new CropRect(left, top, right - left, bottom - top);
    }

    /// <summary>
    /// Null when the <c>crop</c> enlarged by <c>scale</c> fits an image, at most 16384 pixels on a side (Godot's
    /// largest image, which also keeps it within the engine's pixel count); otherwise the problem naming both sizes.
    /// </summary>
    public static string? ScaleProblem(CropRect crop, int scale)
    {
        var (width, height) = ((long)crop.Width * scale, (long)crop.Height * scale);

        return width <= MaxImageSide && height <= MaxImageSide
            ? null
            : $"the crop of {crop.Width}x{crop.Height} pixels at scale {scale} is {width}x{height}, more than {MaxImageSide} on a side";
    }
}
