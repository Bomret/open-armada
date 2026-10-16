using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OpenArmada.Formats;

// One line of a settings file, trimmed.
public sealed record SettingsEntry(int Line, string Text);

// A settings file (.set) in its text form: the shell's list of missions, one ".bzn" name or "none" per line
// (mshell.set). A line of anything else is a Note naming file:line (INV-0002). The binary form (shell.set, a
// record of the shell's saved player settings whose layout is not established) throws
// UnsupportedFormatException.
public sealed class SettingsFile
{
    public string Name { get; }
    public IReadOnlyList<SettingsEntry> Entries { get; }
    public IReadOnlyList<string> Notes { get; }

    SettingsFile(string name, IReadOnlyList<SettingsEntry> entries, IReadOnlyList<string> notes) =>
        (Name, Entries, Notes) = (name, entries, notes);

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
            entries.Add(new SettingsEntry(i + 1, text));
            if (
                !text.Equals("none", System.StringComparison.OrdinalIgnoreCase)
                && !text.EndsWith(".bzn", System.StringComparison.OrdinalIgnoreCase)
            )
                notes.Add($"{name}:{i + 1}: unrecognized line '{text}' (expected a .bzn name or none)");
        }
        return new SettingsFile(name, entries, notes);
    }
}
