using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using OpenArmada.Formats;

namespace OpenArmada.Assets;

/// <summary>
/// Where a resolved value comes from: the definition itself, one of its includes, or a file of the
/// override layer (which wins over the archive's copy of the same definition, whichever level it is).
/// </summary>
public enum ValueOrigin
{
    /// <summary>The value comes from the definition itself.</summary>
    Own,

    /// <summary>The value comes from one of the definition's includes.</summary>
    Inherited,

    /// <summary>
    /// The value comes from a file of the override layer that replaces an archive or index copy; this wins over
    /// <see cref="Own"/> and <see cref="Inherited"/> at any level of the chain.
    /// </summary>
    Overridden,
}

/// <summary>One resolved key. Source is the definition (as named in the include) that holds the winning value.</summary>
public sealed record DefinitionValue
{
    /// <summary>The key as the definition that holds the winning value spells it.</summary>
    public required string Key { get; init; }

    /// <summary>The value's tokens (<see cref="OdfEntry.Tokens"/>); empty for an empty value.</summary>
    public required IReadOnlyList<string> Tokens { get; init; }

    /// <summary>Where the winning value comes from.</summary>
    public required ValueOrigin Origin { get; init; }

    /// <summary>The definition, as named in the include, that holds the winning value.</summary>
    public required string Source { get; init; }
}

/// <summary>A key that is missing or whose value has the wrong type. The message names the definition and the key.</summary>
public sealed class DefinitionException(string message) : Exception(message);

/// <summary>
/// An object definition with its include chain and the override layer applied: one value per key, the
/// definition's own values over its includes', a later include over an earlier one. Keys and include
/// names match regardless of case. A fault of the chain (missing or unreadable include, cycle) is listed in Problems,
/// a line the reader does not recognize in Notes, and the rest still resolves; a lookup of a missing
/// key or of the wrong type throws DefinitionException for that key alone.
/// </summary>
public sealed class ResolvedDefinition
{
    const int MaxDepth = 64;

    readonly Dictionary<string, int> _index = new(StringComparer.OrdinalIgnoreCase);
    readonly List<DefinitionValue> _values = new();
    readonly List<string> _chain = new();
    readonly List<string> _problems = new();
    readonly List<string> _notes = new();
    readonly HashSet<string> _missing = new();
    readonly List<(string Name, string IncludedBy)> _missingIncludes = new();
    readonly Dictionary<string, (int Depth, IReadOnlyList<string> Includes)> _cut = new(
        StringComparer.OrdinalIgnoreCase
    );

    ResolvedDefinition(string name) => Name = name;

    /// <summary>The definition's name, as passed to <see cref="Resolve"/>.</summary>
    public string Name { get; }

    /// <summary>The definitions that contributed, own first, then includes from the latest include back.</summary>
    public IReadOnlyList<string> Chain => _chain;

    /// <summary>Every key once: the definition's own keys in file order, then the inherited ones.</summary>
    public IReadOnlyList<DefinitionValue> Values => _values;

    /// <summary>
    /// One line per fault of the chain: an include that is missing or cannot be read, an include cycle,
    /// a chain too deep. A line appears once, however many walks reach the fault.
    /// </summary>
    public IReadOnlyList<string> Problems => _problems;

    /// <summary>
    /// The Problems that make the definition fail: all but a missing include, which is an unresolved
    /// reference (AssetKind.Unresolved).
    /// </summary>
    public IReadOnlyList<string> Failures => _problems.Where(p => !_missing.Contains(p)).ToList();

    /// <summary>Every include of the chain that is missing, as the file names it, with the definition that includes it.</summary>
    public IReadOnlyList<(string Name, string IncludedBy)> MissingIncludes => _missingIncludes;

    /// <summary>
    /// "&lt;definition&gt;: line N: reason" per line a definition of the chain holds that the reader does not
    /// recognize (the game's own files have a few). Such a line is left out of the values.
    /// </summary>
    public IReadOnlyList<string> Notes => _notes;

    /// <summary>Throws FileNotFoundException when the definition itself is not in the install.</summary>
    public static ResolvedDefinition Resolve(GameFileSystem files, string name)
    {
        var def = new ResolvedDefinition(name);
        def.Visit(files, name, new List<string>(), new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        return def;
    }

    // `visited` holds every definition read so far: one reached again (a diamond, a repeated include)
    // has already lost all its keys to the first visit, so it is skipped, not parsed again.
    // Returns true when the depth limit cut an include of this subtree. A definition so cut is walked
    // again, includes only, when a shallower path reaches it, so the keys below the cut are not lost.
    bool Visit(GameFileSystem files, string name, List<string> path, HashSet<string> visited)
    {
        var at = path.FindIndex(p => p.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (at >= 0)
        {
            // The line names the cycle alone, not the way in, and starts it at its smallest member
            // (ignoring case), so every path that reaches it, at whichever member, gives the same line.
            var members = path.Skip(at).ToList();
            var start = members.IndexOf(members.Min(StringComparer.OrdinalIgnoreCase)!);
            var cycle = members.Skip(start).Concat(members.Take(start)).ToList();
            AddProblem($"include cycle {string.Join(" -> ", cycle.Append(cycle[0]))}");

            return false;
        }

        // The engine's rule: a definition cut by the depth limit and walked again by a shallower path adds
        // the keys it recovers after every key added since the cut, so those rank below them (this arises
        // only in chains deeper than 64). The depth problem stays listed after the recovery, because the
        // chain does exceed the limit; DefinitionKind.Check fails such a definition.
        if (visited.Contains(name))
            return _cut.TryGetValue(name, out var c)
                && path.Count < c.Depth
                && WalkIncludes(files, name, c.Includes, path, visited);
        if (path.Count >= MaxDepth)
        {
            AddProblem($"include chain deeper than {MaxDepth} at '{name}'");

            return true;
        }

        byte[] bytes;
        try
        {
            bytes = files.ReadAllBytes(name);
        }
        catch (Exception e)
            when (path.Count > 0
                && e is IOException or UnauthorizedAccessException or UnsupportedFormatException or InvalidDataException
            )
        {
            if (e is FileNotFoundException)
            {
                var problem = $"include '{name}' not found (included by {path[^1]})";
                AddProblem(problem);
                _missing.Add(problem);
                _missingIncludes.Add((name, path[^1]));
            }
            else
            {
                AddProblem($"include '{name}' cannot be read (included by {path[^1]}): {e.Message}");
            }

            return false;
        }

        var odf = Odf.Parse(Windows1252.Encoding.GetString(bytes));
        visited.Add(name);
        // The definition's own notes carry no name: the check line and the preview already show it.
        _notes.AddRange(odf.Problems.Select(p => path.Count == 0 ? p : $"{name}: {p}"));
        _chain.Add(name);

        var origin =
            files.Overrides(name) ? ValueOrigin.Overridden
            : path.Count == 0 ? ValueOrigin.Own
            : ValueOrigin.Inherited;
        var own = new Dictionary<string, OdfEntry>(StringComparer.OrdinalIgnoreCase);
        // The engine's rule, not confirmed against the game's reader: a repeated key in one file keeps
        // its last value, at the first one's place.
        foreach (var e in odf.Entries)
        {
            own[e.Key] = e;
        }

        foreach (var e in own.Values)
        {
            if (_index.TryAdd(e.Key, _values.Count))
            {
                _values.Add(
                    new DefinitionValue
                    {
                        Key = e.Key,
                        Tokens = e.Tokens,
                        Origin = origin,
                        Source = name,
                    }
                );
            }
        }

        // The engine's rule, not confirmed against the game's reader: includes are visited depth first,
        // the last include first, so a later include (with everything it includes) beats an earlier one.
        return WalkIncludes(files, name, odf.Includes, path, visited);
    }

    // A fault is listed once, however many walks reach it.
    void AddProblem(string problem)
    {
        if (!_problems.Contains(problem))
        {
            _problems.Add(problem);
        }
    }

    bool WalkIncludes(
        GameFileSystem files,
        string name,
        IReadOnlyList<string> includes,
        List<string> path,
        HashSet<string> visited
    )
    {
        var cut = false;
        path.Add(name);
        for (var i = includes.Count - 1; i >= 0; i--)
        {
            cut |= Visit(files, includes[i], path, visited);
        }

        path.RemoveAt(path.Count - 1);
        if (cut)
        {
            _cut[name] = (path.Count, includes);
        }
        else
        {
            _cut.Remove(name);
        }

        return cut;
    }

    /// <summary>True when the definition or its chain defines <c>key</c> (any case).</summary>
    public bool Has(string key) => _index.ContainsKey(key);

    /// <summary>The resolved value of <c>key</c> (any case); null when no definition of the chain defines it.</summary>
    public DefinitionValue? Find(string key) => _index.TryGetValue(key, out var i) ? _values[i] : null;

    /// <summary>
    /// The values of keys <c>prefix</c> + digits (weapon1, weapon2, ...), in numeric order; a number too
    /// large for an int sorts last.
    /// </summary>
    public IReadOnlyList<DefinitionValue> Indexed(string prefix)
    {
        var found = new List<(int N, DefinitionValue Value)>();
        foreach (var v in _values)
        {
            if (
                v.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                && v.Key.Length > prefix.Length
                && v.Key.AsSpan(prefix.Length).IndexOfAnyExceptInRange('0', '9') < 0
            )
            {
                // A suffix too large for an int sorts last.
                found.Add(
                    (
                        int.TryParse(
                            v.Key.AsSpan(prefix.Length),
                            NumberStyles.None,
                            CultureInfo.InvariantCulture,
                            out var n
                        )
                            ? n
                            : int.MaxValue,
                        v
                    )
                );
            }
        }

        return found.OrderBy(f => f.N).Select(f => f.Value).ToList();
    }

    /// <summary>The tokens of <c>key</c>'s value, any number of them; empty for an empty value.</summary>
    /// <exception cref="DefinitionException">when <c>key</c> is not defined.</exception>
    public IReadOnlyList<string> GetList(string key) => Get(key).Tokens;

    /// <summary>One token, quoted or not; an empty value is "".</summary>
    public string GetString(string key)
    {
        var t = Get(key).Tokens;

        return t.Count switch
        {
            0 => "",
            1 => t[0],
            _ => throw Wrong(key, "a string", t),
        };
    }

    /// <summary>One number, with an optional <c>f</c> suffix (2.0f).</summary>
    public float GetFloat(string key)
    {
        var t = Get(key).Tokens;
        if (t.Count == 1 && TryNumber(t[0], out var f))
            return f;

        throw Wrong(key, "a number", t);
    }

    static bool TryNumber(string token, out float f) =>
        float.TryParse(
            token.EndsWith('f') || token.EndsWith('F') ? token[..^1] : token,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out f
        ) && float.IsFinite(f);

    /// <summary>
    /// A default followed by per-target overrides, each a name and a number. Throws DefinitionException when
    /// the default is no number; a malformed override line leaves the default alone and sets Malformed.
    /// </summary>
    public TargetValue GetTargetValue(string key)
    {
        var t = Get(key).Tokens;
        if (t.Count == 0 || !TryNumber(t[0], out var def))
            throw Wrong(key, "a number", t);
        var overrides = new Dictionary<string, float>();
        for (var i = 1; i < t.Count; i += 2)
        {
            if (i + 1 >= t.Count || !TryNumber(t[i + 1], out var v))
                return new TargetValue
                {
                    Default = def,
                    Overrides = new Dictionary<string, float>(),
                    Malformed = true,
                };
            else
            {
                overrides[t[i]] = v;
            }
        }

        return new TargetValue { Default = def, Overrides = overrides };
    }

    /// <summary>One whole number: written as an integer or as a float with no fraction (1.0, 1f).</summary>
    public int GetInt(string key)
    {
        var t = Get(key).Tokens;
        if (t.Count == 1 && int.TryParse(t[0], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n))
            return n;
        if (t.Count == 1 && TryNumber(t[0], out var f) && f == MathF.Floor(f) && MathF.Abs(f) < int.MaxValue)
            return (int)f;

        throw Wrong(key, "an integer", t);
    }

    DefinitionValue Get(string key) =>
        Find(key) ?? throw new DefinitionException($"{Name}: key '{key}' is not defined");

    DefinitionException Wrong(string key, string wanted, IReadOnlyList<string> tokens) =>
        new($"{Name}: key '{key}' is not {wanted}: {string.Join(" ", tokens)}");
}
