using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using OpenArmada.Formats;

namespace OpenArmada.Assets;

// Where a resolved value comes from: the definition itself, one of its includes, or a file of the
// override layer (which wins over the archive's copy of the same definition, whichever level it is).
public enum ValueOrigin { Own, Inherited, Overridden }

// One resolved key. Source is the definition (as named in the include) that holds the winning value.
public sealed record DefinitionValue(string Key, IReadOnlyList<string> Tokens, ValueOrigin Origin, string Source);

// A key that is missing or whose value has the wrong type. The message names the definition and the key.
public sealed class DefinitionException : Exception
{
    public DefinitionException(string message) : base(message) { }
}

// An object definition with its include chain and the override layer applied: one value per key, the
// definition's own values over its includes', a later include over an earlier one. Keys and include
// names match regardless of case. A fault of the chain (missing include, cycle) is listed in Problems,
// a line the reader does not recognize in Notes, and the rest still resolves; a lookup of a missing
// key or of the wrong type throws DefinitionException for that key alone.
public sealed class ResolvedDefinition
{
    const int MaxDepth = 64;

    readonly Dictionary<string, int> _index = new(StringComparer.OrdinalIgnoreCase);
    readonly List<DefinitionValue> _values = new();
    readonly List<string> _chain = new();
    readonly List<string> _problems = new();
    readonly List<string> _notes = new();

    ResolvedDefinition(string name) => Name = name;

    public string Name { get; }

    // The definitions that contributed, own first, then includes from the latest include back.
    public IReadOnlyList<string> Chain => _chain;

    // Every key once: the definition's own keys in file order, then the inherited ones.
    public IReadOnlyList<DefinitionValue> Values => _values;

    // One line per fault of the chain: a missing include, an include cycle, a chain too deep.
    public IReadOnlyList<string> Problems => _problems;

    // "<definition>: line N: reason" per line a definition of the chain holds that the reader does not
    // recognize (the game's own files have a few). Such a line is left out of the values.
    public IReadOnlyList<string> Notes => _notes;

    // Throws FileNotFoundException when the definition itself is not in the install.
    public static ResolvedDefinition Resolve(GameFileSystem files, string name)
    {
        var def = new ResolvedDefinition(name);
        def.Visit(files, name, new List<string>());
        return def;
    }

    void Visit(GameFileSystem files, string name, List<string> path)
    {
        if (path.Any(p => p.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            _problems.Add($"include cycle {string.Join(" -> ", path.Append(name))}");
            return;
        }
        if (path.Count >= MaxDepth)
        {
            _problems.Add($"include chain deeper than {MaxDepth} at '{name}'");
            return;
        }
        byte[] bytes;
        try { bytes = files.ReadAllBytes(name); }
        catch (FileNotFoundException) when (path.Count > 0)
        {
            _problems.Add($"include '{name}' not found (included by {path[^1]})");
            return;
        }
        var odf = Odf.Parse(Windows1252.Encoding.GetString(bytes));
        _notes.AddRange(odf.Problems.Select(p => $"{name}: {p}"));
        _chain.Add(name);

        var origin = files.Overrides(name) ? ValueOrigin.Overridden : path.Count == 0 ? ValueOrigin.Own : ValueOrigin.Inherited;
        var own = new Dictionary<string, OdfEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in odf.Entries) own[e.Key] = e; // a repeated key in one file: the last wins, at the first's place
        foreach (var e in own.Values)
            if (_index.TryAdd(e.Key, _values.Count)) _values.Add(new DefinitionValue(e.Key, e.Tokens, origin, name));

        path.Add(name);
        for (var i = odf.Includes.Count - 1; i >= 0; i--) Visit(files, odf.Includes[i], path);
        path.RemoveAt(path.Count - 1);
    }

    public bool Has(string key) => _index.ContainsKey(key);

    public DefinitionValue? Find(string key) => _index.TryGetValue(key, out var i) ? _values[i] : null;

    // The values of keys `prefix` + digits (weapon1, weapon2, ...), in numeric order.
    public IReadOnlyList<DefinitionValue> Indexed(string prefix) =>
        _values.Where(v => v.Key.Length > prefix.Length && v.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                           && v.Key.AsSpan(prefix.Length).ToString().All(char.IsAsciiDigit))
            .OrderBy(v => int.TryParse(v.Key.AsSpan(prefix.Length), out var n) ? n : int.MaxValue)
            .ToList();

    public IReadOnlyList<string> GetList(string key) => Get(key).Tokens;

    // One token, quoted or not; an empty value is "".
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

    // One number, with an optional `f` suffix (2.0f).
    public float GetFloat(string key)
    {
        var t = Get(key).Tokens;
        if (t.Count == 1 && float.TryParse(t[0].EndsWith('f') || t[0].EndsWith('F') ? t[0][..^1] : t[0],
                NumberStyles.Float, CultureInfo.InvariantCulture, out var f)) return f;
        throw Wrong(key, "a number", t);
    }

    public int GetInt(string key)
    {
        var t = Get(key).Tokens;
        if (t.Count == 1 && int.TryParse(t[0], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n)) return n;
        throw Wrong(key, "an integer", t);
    }

    DefinitionValue Get(string key) =>
        Find(key) ?? throw new DefinitionException($"{Name}: key '{key}' is not defined");

    DefinitionException Wrong(string key, string wanted, IReadOnlyList<string> tokens) =>
        new($"{Name}: key '{key}' is not {wanted}: {string.Join(" ", tokens)}");
}
