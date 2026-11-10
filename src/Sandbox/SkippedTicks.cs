using System;
using System.Collections.Generic;

namespace OpenArmada.Sandbox;

/// <summary>
/// The combat events of the ticks a play run's <c>fast-forward</c> ran without drawing, kept until the next frame hands
/// them to the views ahead of that frame's own events.
/// </summary>
public sealed class SkippedTicks
{
    readonly List<CombatEvent> _events = [];

    /// <summary>
    /// Runs one skipped step and keeps its events. Returns null, or the problem when the step threw; nothing is logged
    /// here, since the play run reports the problem once (INV-0002).
    /// </summary>
    public string? Run(Func<IReadOnlyList<CombatEvent>> step)
    {
        try
        {
            _events.AddRange(step());

            return null;
        }
        catch (Exception e) // a failed step is the play run's problem, whatever the reason (INV-0007)
        {
            return $"combat step failed: {e.Message}";
        }
    }

    /// <summary>The events kept since the last call, in tick order; none are kept afterwards.</summary>
    public IReadOnlyList<CombatEvent> Take()
    {
        var events = _events.ToArray();
        _events.Clear();

        return events;
    }
}
