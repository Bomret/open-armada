using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace OpenArmada.Formats;

// One "key = value" line: Values are the quoted text (IsText) or the numbers as written.
public sealed record ConfigEntry(string Key, string[] Values, bool IsText, string File, int Line);

// A rectangle of the interface in screen pixels (Sprite: the sprite name the cfg gives it, if any).
// A panel is a screen area with a cursor of its own; the other elements are positioned inside a panel
// and are listed here already moved by the panel's position.
public sealed record LayoutElement(string Name, float X, float Y, float Width, float Height, string? Sprite, bool IsPanel);

// An interface configuration (the misc/gui_*.cfg files): "key = value" lines where a value is one
// quoted text or numbers, "//" comments, and "#include" of another configuration, expanded where it
// stands (Armada.exe's reader 0x5537a0). A line that is none of these, an include that cannot be read
// and an include cycle are Notes naming file:line, and the rest still loads (INV-0002).
public sealed class InterfaceConfig
{
    static readonly Regex Include = new(@"^#include\s+""([^""]*)""\s*(//.*)?$");
    static readonly Regex Assign = new(@"^(\w+)\s*=\s*(.*)$");
    static readonly Regex Quoted = new(@"^""([^""]*)""\s*(//.*)?$");
    static readonly Regex Piece = new(@"^(.+Panel)_(\d+)$");

    readonly List<ConfigEntry> _entries = new();
    readonly List<string> _notes = new();
    readonly List<LayoutElement> _elements = new();

    public string Name { get; }
    public IReadOnlyList<ConfigEntry> Entries => _entries;
    public IReadOnlyList<string> Notes => _notes;
    public IReadOnlyList<LayoutElement> Elements => _elements;

    InterfaceConfig(string name) => Name = name;

    // `open` returns the text of an included configuration by the name written after #include, or null.
    public static InterfaceConfig Read(string name, string text, Func<string, string?> open)
    {
        var c = new InterfaceConfig(name);
        c.ReadText(name, text, open, new List<string> { name }, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { name });
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
            if (line.Length == 0 || line.StartsWith("//")) continue;
            if (line.StartsWith('#'))
            {
                var inc = Include.Match(line);
                if (!inc.Success) { _notes.Add($"{at}: unrecognized line '{line}'"); continue; }
                var target = inc.Groups[1].Value;
                if (stack.Contains(target, StringComparer.OrdinalIgnoreCase)) { _notes.Add($"{at}: include cycle through '{target}'"); continue; }
                if (!seen.Add(target)) continue; // read already: adds nothing the second time
                var body = open(target);
                if (body == null) { _notes.Add($"{at}: include '{target}' not found"); continue; }
                stack.Add(target);
                ReadText(target, body, open, stack, seen);
                stack.RemoveAt(stack.Count - 1);
                continue;
            }
            var m = Assign.Match(line);
            if (!m.Success) { _notes.Add($"{at}: unrecognized line '{line}'"); continue; }
            var key = m.Groups[1].Value;
            var value = m.Groups[2].Value.Trim();
            if (value.StartsWith('"'))
            {
                var q = Quoted.Match(value);
                if (q.Success) _entries.Add(new ConfigEntry(key, new[] { q.Groups[1].Value }, true, file, i + 1));
                else _notes.Add($"{at}: unterminated text in '{line}'");
                continue;
            }
            var tokens = value.Split("//")[0].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0 || tokens.Any(t => !double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out _)))
                _notes.Add($"{at}: value of '{key}' is neither text nor numbers: '{value}'");
            else _entries.Add(new ConfigEntry(key, tokens, false, file, i + 1));
        }
    }

    static float[]? Numbers(ConfigEntry e) =>
        !e.IsText && e.Values.Length == 4 ? e.Values.Select(v => (float)double.Parse(v, CultureInfo.InvariantCulture)).ToArray() : null;

    void BuildLayout()
    {
        var byKey = new Dictionary<string, ConfigEntry>();
        foreach (var e in _entries) byKey[e.Key] = e; // a later line overrides an earlier one
        string? Text(string key) => byKey.TryGetValue(key, out var e) && e.IsText ? e.Values[0] : null;

        // A panel group "g" has gDefaultCursor and gPanelArea.
        var panels = byKey.Keys.Where(k => k.EndsWith("PanelArea") && byKey.ContainsKey(k[..^9] + "DefaultCursor") && Numbers(byKey[k]) != null)
            .Select(k => k[..^9]).ToList();
        (float X, float Y) Origin(string key, out bool isPanel)
        {
            isPanel = panels.Contains(key[..Math.Max(0, key.Length - 9)]) && key.EndsWith("PanelArea");
            if (isPanel) return (0, 0);
            var g = panels.Where(p => key.StartsWith(p)).OrderByDescending(p => p.Length).FirstOrDefault();
            if (g == null) return (0, 0);
            var n = Numbers(byKey[g + "PanelArea"])!;
            return (n[0], n[1]);
        }

        foreach (var e in byKey.Values.OrderBy(e => e.Line).ThenBy(e => e.File))
        {
            if (e.Key.EndsWith("Area"))
            {
                if (Numbers(e) is not { } n) { _notes.Add($"{e.File}:{e.Line}: '{e.Key}' is not four numbers (x y width height)"); continue; }
                var o = Origin(e.Key, out var isPanel);
                var stem = e.Key[..^4];
                _elements.Add(new LayoutElement(e.Key, n[0] + o.X, n[1] + o.Y, n[2], n[3], Text(stem) ?? Text(stem + "Image"), isPanel));
            }
            else if (Piece.Match(e.Key) is { Success: true } p && Numbers(e) is { } r && Text(p.Groups[1].Value) is { } sprite)
            {
                var o = Origin(e.Key, out _);
                _elements.Add(new LayoutElement(e.Key, r[0] + o.X, r[1] + o.Y, r[2], r[3], $"{sprite}.{p.Groups[2].Value}", false));
            }
        }
    }
}
