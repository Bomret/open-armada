using System;
using System.Collections.Generic;

namespace OpenArmada.Sandbox;

// The user arguments after `--`. A flag's value is the next argument, never another flag: a flag at the
// end or followed by a flag has no value and each such lookup adds a line to Problems (once per flag).
public sealed class LaunchArgs
{
    readonly string[] _args;
    readonly List<string> _problems = new();

    public LaunchArgs(string[] args) => _args = args;

    public IReadOnlyList<string> Problems => _problems;

    public bool Has(string flag) => Array.IndexOf(_args, flag) >= 0;

    // The value of the flag; null when the flag is absent or has no value (the latter is a problem).
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
