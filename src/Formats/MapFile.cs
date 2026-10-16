using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace OpenArmada.Formats;

// One binding or label. Gamekey.map lines "NAME KEYS" have no Section and Sign; the blocks of Input.map
// and label.map ("name {" ... "}") hold "+ key value" and "- key value" lines, a label's value read
// without its braces.
public sealed record MapEntry(string Section, string Sign, string Key, string Value, int Line);

// A key map or label map (.map): '#' comments, flat "NAME KEYS" lines and blocks (the grammar of
// Armada.exe's input parser 0x474440 as seen in the install's files). A line that fits none of these is a
// Note naming file:line, and the rest still loads (INV-0002).
public sealed class MapFile
{
    static readonly Regex Flat = new(@"^([A-Za-z_]\w*)\s+(\S.*)$");
    static readonly Regex Open = new(@"^(\S+)\s*\{$");
    static readonly Regex Item = new(@"^([+-])\s+(\S+)\s+(\S.*)$");
    static readonly Regex Label = new(@"^\{(.*)\}$");

    readonly List<MapEntry> _entries = new();
    readonly List<string> _notes = new();

    public string Name { get; }
    public IReadOnlyList<MapEntry> Entries => _entries;
    public IReadOnlyList<string> Notes => _notes;

    MapFile(string name) => Name = name;

    public static MapFile Read(string name, string text)
    {
        var m = new MapFile(name);
        string? section = null;
        var openedAt = 0;
        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            var at = $"{name}:{i + 1}";
            if (line.Length == 0 || line.StartsWith('#')) continue;
            if (section == null)
            {
                if (Open.Match(line) is { Success: true } o) { section = o.Groups[1].Value; openedAt = i + 1; }
                else if (Flat.Match(line) is { Success: true } f) m._entries.Add(new MapEntry("", "", f.Groups[1].Value, f.Groups[2].Value, i + 1));
                else m._notes.Add($"{at}: unrecognized line '{line}'");
            }
            else if (line == "}") section = null;
            else if (Item.Match(line) is { Success: true } it)
            {
                var value = it.Groups[3].Value.Trim();
                if (Label.Match(value) is { Success: true } l) value = l.Groups[1].Value;
                m._entries.Add(new MapEntry(section, it.Groups[1].Value, it.Groups[2].Value, value, i + 1));
            }
            else m._notes.Add($"{at}: unrecognized line '{line}' in block '{section}'");
        }
        if (section != null) m._notes.Add($"{name}:{openedAt}: block '{section}' is not closed");
        return m;
    }
}
