using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OpenArmada.Formats;

/// <summary>One line of a settings file, trimmed.</summary>
public sealed record SettingsEntry
{
    /// <summary>The 1-based line in the file.</summary>
    public required int Line { get; init; }

    /// <summary>The line's text, trimmed and never empty.</summary>
    public required string Text { get; init; }
}

/// <summary>
/// A settings file (.set) in its text form: the shell's list of missions, one ".bzn" name or "none" per line
/// (mshell.set). A line of anything else is a Note naming file:line (INV-0002). The binary form (shell.set, a
/// record of the shell's saved player settings whose layout is not established) throws
/// UnsupportedFormatException.
/// </summary>
public sealed class SettingsFile
{
    /// <summary>The file name given to <see cref="Read"/>; it prefixes every note.</summary>
    public string Name { get; }

    /// <summary>The non-blank lines in file order, including the lines that are notes.</summary>
    public IReadOnlyList<SettingsEntry> Entries { get; }

    /// <summary>The lines that are neither a ".bzn" name nor "none", each naming file:line.</summary>
    public IReadOnlyList<string> Notes { get; }

    SettingsFile(string name, IReadOnlyList<SettingsEntry> entries, IReadOnlyList<string> notes) =>
        (Name, Entries, Notes) = (name, entries, notes);

    /// <summary>Reads a settings file in its text form, decoded as Windows-1252.</summary>
    /// <param name="name">The file name, used in exception messages and notes.</param>
    /// <param name="data">The whole file.</param>
    /// <exception cref="UnsupportedFormatException">
    /// when the file holds a control byte other than tab, line feed, vertical tab, form feed or carriage return
    /// (the binary form).
    /// </exception>
    public static SettingsFile Read(string name, byte[] data)
    {
        if (data.Any(b => b < 9 || (b > 13 && b < 32)))
            throw new UnsupportedFormatException(
                $"{name}: a binary settings record ({data.Length} bytes); its layout is not established"
            );
        var entries = new List<SettingsEntry>();
        var notes = new List<string>();
        var lines = Windows1252.Encoding.GetString(data).Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var text = lines[i].Trim();
            if (text.Length == 0)
                continue;
            entries.Add(new SettingsEntry { Line = i + 1, Text = text });
            if (
                !text.Equals("none", System.StringComparison.OrdinalIgnoreCase)
                && !text.EndsWith(".bzn", System.StringComparison.OrdinalIgnoreCase)
            )
            {
                notes.Add($"{name}:{i + 1}: unrecognized line '{text}' (expected a .bzn name or none)");
            }
        }

        return new SettingsFile(name, entries, notes);
    }
}
