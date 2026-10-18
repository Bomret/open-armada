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

    /// <summary>One line per flag that a <see cref="Value"/> lookup found without a value, and per pair <see cref="Exclusive"/> found together.</summary>
    public IReadOnlyList<string> Problems => _problems;

    /// <summary>Whether any argument equals <c>flag</c> exactly (case-sensitive), whether or not it has a value.</summary>
    public bool Has(string flag) => Array.IndexOf(_args, flag) >= 0;

    /// <summary>
    /// The run's mode: <c>check</c>, <c>maps</c>, <c>play</c>, <c>map</c>, <c>layout</c>, or <c>browser</c>, the
    /// first whose flag is given. <c>--check</c> wins over <c>--maps</c>; <c>--maps</c> wins over <c>--map</c>,
    /// <c>--layout</c>, and <c>--play</c>. Looks up no value, so it adds no problem.
    /// </summary>
    public string Mode() =>
        Has("--check") ? "check"
        : Has("--maps") ? "maps"
        : Has("--play") ? "play"
        : Has("--map") ? "map"
        : Has("--layout") ? "layout"
        : "browser";

    /// <summary>Adds a problem naming both flags when both are given (once per pair).</summary>
    public void Exclusive(string flag, string other)
    {
        var problem = $"{flag} and {other} cannot be given together";
        if (Has(flag) && Has(other) && !_problems.Contains(problem))
        {
            _problems.Add(problem);
        }
    }

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
