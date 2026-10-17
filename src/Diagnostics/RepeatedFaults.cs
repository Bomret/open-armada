using System.Collections.Generic;

namespace OpenArmada.Diagnostics;

/// <summary>
/// Logs a fault the engine catches again and again (a combat step failing each frame) as an
/// <see cref="Level.Error"/> at its first occurrence per message, and counts the rest; <see cref="Flush"/> logs
/// each count once as one entry. Not thread-safe: one owner calls it from one thread.
/// </summary>
public sealed class RepeatedFaults(Area area)
{
    readonly Dictionary<string, int> _further = new();

    /// <summary>Logs <c>message</c> when it is new; counts it otherwise.</summary>
    public void Report(string message)
    {
        if (_further.TryGetValue(message, out var n))
        {
            _further[message] = n + 1;

            return;
        }

        _further[message] = 0;
        Log.Error(area, message);
    }

    /// <summary>Logs one entry with the count for each message seen again, then forgets the counts.</summary>
    public void Flush()
    {
        foreach (var (message, n) in _further)
        {
            if (n > 0)
            {
                Log.Error(area, $"{message} ({n} more {(n == 1 ? "time" : "times")})");
                _further[message] = 0;
            }
        }
    }
}
