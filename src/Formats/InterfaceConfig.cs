using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace OpenArmada.Formats;

/// <summary>One "key = value" line: Values are the quoted text (IsText) or the numbers as written.</summary>
public sealed record ConfigEntry
{
    /// <summary>The name left of the '='.</summary>
    public required string Key { get; init; }

    /// <summary>
    /// The value: one element holding the quoted text without its quotes when <see cref="IsText"/>, else the
    /// numbers as written, without a trailing "//" comment.
    /// </summary>
    public required string[] Values { get; init; }

    /// <summary>True when the value is one quoted text, false when it is numbers.</summary>
    public required bool IsText { get; init; }

    /// <summary>
    /// The configuration the line is in: the name given to <see cref="InterfaceConfig.Read"/> or the name of an
    /// include.
    /// </summary>
    public required string File { get; init; }

    /// <summary>The 1-based line in <see cref="File"/>.</summary>
    public required int Line { get; init; }
}

/// <summary>
/// A rectangle of the interface in screen pixels (Sprite: the sprite name the cfg gives it, if any).
/// A panel is a screen area with a cursor of its own; the other elements are positioned inside a panel
/// and are listed here already moved by the panel's position.
/// </summary>
public sealed record LayoutElement
{
    /// <summary>
    /// The key of the cfg line that places the element, such as an "...Area" key or a "...Panel_n" piece.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>The left edge in screen pixels, moved by the panel's position unless the element is a panel.</summary>
    public required float X { get; init; }

    /// <summary>The top edge in screen pixels, moved by the panel's position unless the element is a panel.</summary>
    public required float Y { get; init; }

    /// <summary>The width in screen pixels.</summary>
    public required float Width { get; init; }

    /// <summary>The height in screen pixels.</summary>
    public required float Height { get; init; }

    /// <summary>The sprite name the cfg gives the element, or null when it gives none.</summary>
    public required string? Sprite { get; init; }

    /// <summary>True for a panel: a "gPanelArea" key whose group g also has a "gDefaultCursor" key.</summary>
    public required bool IsPanel { get; init; }
}

/// <summary>
/// An interface configuration (the misc/gui_*.cfg files): "key = value" lines where a value is one
/// quoted text or numbers, "//" comments, and "#include" of another configuration, expanded where it
/// stands (Armada.exe's reader 0x5537a0). A line that is none of these, an include that cannot be read
/// and an include cycle are Notes naming file:line, and the rest still loads (INV-0002).
/// </summary>
public sealed class InterfaceConfig
{
    static readonly Regex Include = new(@"^#include\s+""([^""]*)""\s*(//.*)?$");
    static readonly Regex Assign = new(@"^(\w+)\s*=\s*(.*)$");
    static readonly Regex Quoted = new(@"^""([^""]*)""\s*(//.*)?$");
    static readonly Regex Piece = new(@"^(.+Panel)_(\d+)$");

    readonly List<ConfigEntry> _entries = new();
    readonly List<string> _notes = new();
    readonly List<LayoutElement> _elements = new();

    /// <summary>The name given to <see cref="Read"/>, the top configuration of the include tree.</summary>
    public string Name { get; }

    /// <summary>
    /// The "key = value" lines of the configuration and its includes, in reading order, includes expanded where they
    /// stand.
    /// </summary>
    public IReadOnlyList<ConfigEntry> Entries => _entries;

    /// <summary>The problems that do not stop the read, each naming file:line.</summary>
    public IReadOnlyList<string> Notes => _notes;

    /// <summary>
    /// The layout rectangles: each "...Area" key of four numbers, and each "...Panel_n" key of four numbers whose
    /// "...Panel" key is text. A later line of a key overrides an earlier one. The list is ordered by line number, then
    /// by file name, so elements of different files interleave.
    /// </summary>
    public IReadOnlyList<LayoutElement> Elements => _elements;

    InterfaceConfig(string name) => Name = name;

    /// <summary><c>open</c> returns the text of an included configuration by the name written after #include, or null.</summary>
    public static InterfaceConfig Read(string name, string text, Func<string, string?> open)
    {
        var c = new InterfaceConfig(name);
        c.ReadText(
            name,
            text,
            open,
            new List<string> { name },
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { name }
        );
        c.BuildLayout();

        return c;
    }

    void ReadText(string file, string text, Func<string, string?> open, List<string> stack, HashSet<string> seen)
    {
        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            var at = $"{file}:{i + 1}";
            if (line.Length == 0 || line.StartsWith("//"))
                continue;
            if (line.StartsWith('#'))
            {
                var inc = Include.Match(line);
                if (!inc.Success)
                {
                    _notes.Add($"{at}: unrecognized line '{line}'");

                    continue;
                }

                var target = inc.Groups[1].Value;
                if (stack.Contains(target, StringComparer.OrdinalIgnoreCase))
                {
                    _notes.Add($"{at}: include cycle through '{target}'");

                    continue;
                }

                if (!seen.Add(target))
                {
                    _notes.Add($"{at}: include '{target}' already read; skipped");

                    continue;
                } // adds nothing the second time

                var body = open(target);
                if (body == null)
                {
                    _notes.Add($"{at}: include '{target}' not found");

                    continue;
                }

                stack.Add(target);
                ReadText(target, body, open, stack, seen);
                stack.RemoveAt(stack.Count - 1);

                continue;
            }

            var m = Assign.Match(line);
            if (!m.Success)
            {
                _notes.Add($"{at}: unrecognized line '{line}'");

                continue;
            }

            var key = m.Groups[1].Value;
            var value = m.Groups[2].Value.Trim();
            if (value.StartsWith('"'))
            {
                var q = Quoted.Match(value);
                if (q.Success)
                {
                    _entries.Add(
                        new ConfigEntry
                        {
                            Key = key,
                            Values = new[] { q.Groups[1].Value },
                            IsText = true,
                            File = file,
                            Line = i + 1,
                        }
                    );
                }
                else
                {
                    _notes.Add($"{at}: unterminated text in '{line}'");
                }

                continue;
            }

            var tokens = value.Split("//")[0].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (
                tokens.Length == 0
                || tokens.Any(t =>
                    !double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
                    || !double.IsFinite(v)
                )
            )
            {
                _notes.Add($"{at}: value of '{key}' is neither text nor numbers: '{value}'");
            }
            else
            {
                _entries.Add(
                    new ConfigEntry
                    {
                        Key = key,
                        Values = tokens,
                        IsText = false,
                        File = file,
                        Line = i + 1,
                    }
                );
            }
        }
    }

    static float[]? Numbers(ConfigEntry e) =>
        !e.IsText && e.Values.Length == 4
            ? e.Values.Select(v => (float)double.Parse(v, CultureInfo.InvariantCulture)).ToArray()
            : null;

    void BuildLayout()
    {
        var byKey = new Dictionary<string, ConfigEntry>();
        foreach (var e in _entries)
        {
            byKey[e.Key] = e; // a later line overrides an earlier one
        }

        string? Text(string key) => byKey.TryGetValue(key, out var e) && e.IsText ? e.Values[0] : null;

        // A panel group "g" has gDefaultCursor and gPanelArea.
        var origins = new Dictionary<string, (float X, float Y)>();
        foreach (var (k, e) in byKey)
        {
            if (k.EndsWith("PanelArea") && byKey.ContainsKey(k[..^9] + "DefaultCursor") && Numbers(e) is { } n)
            {
                origins[k[..^9]] = (n[0], n[1]);
            }
        }

        (float X, float Y) Origin(string key) =>
            origins
                .Where(o => key.StartsWith(o.Key))
                .OrderByDescending(o => o.Key.Length)
                .Select(o => o.Value)
                .FirstOrDefault();

        foreach (var e in byKey.Values.OrderBy(e => e.Line).ThenBy(e => e.File))
        {
            if (e.Key.EndsWith("Area"))
            {
                if (Numbers(e) is not { } n)
                {
                    _notes.Add($"{e.File}:{e.Line}: '{e.Key}' is not four numbers (x y width height)");

                    continue;
                }

                var isPanel = e.Key.EndsWith("PanelArea") && origins.ContainsKey(e.Key[..^9]);
                var o = isPanel ? (X: 0f, Y: 0f) : Origin(e.Key);
                var stem = e.Key[..^4];
                _elements.Add(
                    new LayoutElement
                    {
                        Name = e.Key,
                        X = n[0] + o.X,
                        Y = n[1] + o.Y,
                        Width = n[2],
                        Height = n[3],
                        Sprite = Text(stem) ?? Text(stem + "Image"),
                        IsPanel = isPanel,
                    }
                );
            }
            else if (Piece.Match(e.Key) is { Success: true } p && Text(p.Groups[1].Value) is { } sprite)
            {
                if (Numbers(e) is not { } r)
                {
                    _notes.Add($"{e.File}:{e.Line}: '{e.Key}' is not four numbers (x y width height)");

                    continue;
                }

                var o = Origin(e.Key);
                _elements.Add(
                    new LayoutElement
                    {
                        Name = e.Key,
                        X = r[0] + o.X,
                        Y = r[1] + o.Y,
                        Width = r[2],
                        Height = r[3],
                        Sprite = $"{sprite}.{p.Groups[2].Value}",
                        IsPanel = false,
                    }
                );
            }
        }
    }
}
