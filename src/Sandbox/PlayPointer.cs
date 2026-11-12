using System;
using System.Numerics;

namespace OpenArmada.Sandbox;

/// <summary>
/// The play pointer of a play run: where the run's last mouse event landed, in viewport coordinates; null before the
/// first mouse event and after a <c>leave</c>. A hover moves it only once every check of the hover passes; a failed
/// hover leaves it where it was.
/// </summary>
public sealed class PlayPointer
{
    /// <summary>Where the pointer lies, or null outside the window.</summary>
    public Vector2? At { get; private set; }

    /// <summary>A mouse event lands at <c>at</c>.</summary>
    public void Land(Vector2 at) => At = at;

    /// <summary>A <c>leave</c>: the pointer moves out of the window.</summary>
    public void Leave() => At = null;

    /// <summary>
    /// A hover on <c>name</c> aimed at <c>at</c> in a viewport of <c>viewport</c> pixels, where <c>picked</c> names
    /// the unit picked at a point (null: none). Returns the problem (<see cref="PlayTarget.HoverProblem"/>, then
    /// <see cref="PlayTarget.PickProblem"/>), or null with the pointer at <c>at</c>.
    /// </summary>
    public string? Hover(string name, Vector2 at, Vector2 viewport, Func<Vector2, string?> picked)
    {
        var problem = PlayTarget.HoverProblem(name, at, viewport) ?? PlayTarget.PickProblem(name, picked(at));
        if (problem == null)
            Land(at);

        return problem;
    }
}
