using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace OpenArmada.Formats;

/// <summary>
/// One binding or label. Gamekey.map lines "NAME KEYS" have no Section and Sign; the blocks of Input.map
/// and label.map ("name {" ... "}") hold "+ key value" and "- key value" lines, a label's value read
/// without its braces.
/// </summary>
public sealed record MapEntry
{
    /// <summary>The name of the block the line is in; empty for a flat line.</summary>
    public required string Section { get; init; }

    /// <summary>The <c>+</c> or <c>-</c> that starts a block line; empty for a flat line.</summary>
    public required string Sign { get; init; }

    /// <summary>The NAME of a flat line, or the key of a block line.</summary>
    public required string Key { get; init; }

    /// <summary>
    /// The KEYS of a flat line, or the value of a block line; a value in braces is read without them.
    /// </summary>
    public required string Value { get; init; }

    /// <summary>The 1-based line in the file.</summary>
    public required int Line { get; init; }
}

/// <summary>
/// A key map or label map (.map): '#' comments, flat "NAME KEYS" lines and blocks (the grammar of
/// Armada.exe's input parser 0x474440 as seen in the install's files). A line that fits none of these is a
/// Note naming file:line, and the rest still loads (INV-0002).
/// </summary>
public sealed class MapFile
{
    static readonly Regex Flat = new(@"^([A-Za-z_]\w*)\s+(\S.*)$");
    static readonly Regex Open = new(@"^(\S+)\s*\{$");
    static readonly Regex Item = new(@"^([+-])\s+(\S+)\s+(\S.*)$");
    static readonly Regex Label = new(@"^\{(.*)\}$");

    readonly List<MapEntry> _entries = new();
    readonly List<string> _notes = new();

    /// <summary>The file name given to <see cref="Read"/>; it prefixes every note.</summary>
    public string Name { get; }

    /// <summary>The bindings and labels in file order.</summary>
    public IReadOnlyList<MapEntry> Entries => _entries;

    /// <summary>
    /// The problems that do not stop the read, each naming file:line: an unrecognized line or a block that is not
    /// closed.
    /// </summary>
    public IReadOnlyList<string> Notes => _notes;

    MapFile(string name) => Name = name;

    /// <summary>Reads a key map or label map. The method does not throw on bad lines; it adds notes.</summary>
    /// <param name="name">The file name, used in notes.</param>
    /// <param name="text">The decoded text of the file.</param>
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
            if (line.Length == 0 || line.StartsWith('#'))
                continue;
            if (section == null)
            {
                if (Open.Match(line) is { Success: true } o)
                {
                    section = o.Groups[1].Value;
                    openedAt = i + 1;
                }
                else if (Flat.Match(line) is { Success: true } f)
                {
                    m._entries.Add(
                        new MapEntry
                        {
                            Section = "",
                            Sign = "",
                            Key = f.Groups[1].Value,
                            Value = f.Groups[2].Value,
                            Line = i + 1,
                        }
                    );
                }
                else
                {
                    m._notes.Add($"{at}: unrecognized line '{line}'");
                }
            }
            else if (line == "}")
            {
                section = null;
            }
            else if (Item.Match(line) is { Success: true } it)
            {
                var value = it.Groups[3].Value.Trim();
                if (Label.Match(value) is { Success: true } l)
                {
                    value = l.Groups[1].Value;
                }

                m._entries.Add(
                    new MapEntry
                    {
                        Section = section,
                        Sign = it.Groups[1].Value,
                        Key = it.Groups[2].Value,
                        Value = value,
                        Line = i + 1,
                    }
                );
            }
            else
            {
                m._notes.Add($"{at}: unrecognized line '{line}' in block '{section}'");
            }
        }

        if (section != null)
        {
            m._notes.Add($"{name}:{openedAt}: block '{section}' is not closed");
        }

        return m;
    }
}
