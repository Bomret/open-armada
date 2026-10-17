using System;
using System.Collections.Generic;

namespace OpenArmada.Sandbox;

/// <summary>
/// The user arguments after <c>--</c>. A flag's value is the next argument, never another flag: a flag at the
/// end or followed by a flag has no value and each such lookup adds a line to Problems (once per flag).
/// </summary>
public sealed class LaunchArgs
{
    readonly string[] _args;
    readonly List<string> _problems = new();

    /// <summary><c>args</c> are the user arguments after <c>--</c>; the array is kept, not copied.</summary>
    public LaunchArgs(string[] args) => _args = args;

    /// <summary>One line per flag that a <see cref="Value"/> lookup found without a value.</summary>
    public IReadOnlyList<string> Problems => _problems;

    /// <summary>Whether any argument equals <c>flag</c> exactly (case-sensitive), whether or not it has a value.</summary>
    public bool Has(string flag) => Array.IndexOf(_args, flag) >= 0;

    /// <summary>The value of the flag; null when the flag is absent or has no value (the latter is a problem).</summary>
    public string? Value(string flag)
    {
        var i = Array.IndexOf(_args, flag);
        if (i < 0)
            return null;
        if (i + 1 < _args.Length && !_args[i + 1].StartsWith("--", StringComparison.Ordinal))
            return _args[i + 1];
        var problem = $"{flag} needs a value";
        if (!_problems.Contains(problem))
        {
            _problems.Add(problem);
        }

        return null;
    }
}
