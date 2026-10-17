using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace OpenArmada.Sandbox;

/// <summary>The checks of a play step that sends input events at a unit or a point: where to aim and whether the click picks what the step names.</summary>
public static class PlayTarget
{
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
}
