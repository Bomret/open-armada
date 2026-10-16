using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OpenArmada.Formats;

// startrek.zix, read as the game does (REA report: FUN_004b8eb0, FUN_004b8af0, FUN_004b8dc0): a count
// line, a volume section and a path section (each ended by a line starting with '-'), then
// "<dir index> <file name>" lines. Blank lines are skipped and '#' starts a comment.
public sealed class ZixIndex
{
    // The path section in file order; Dirs[i] is the folder or archive of dir index i + 1.
    // A path that is not a "DIR:" line (a CD path) is unsupported and null, and keeps its slot.
    public IReadOnlyList<string?> Dirs { get; }

    // File name (looked up ignoring case) -> 1-based dir index. A name listed twice keeps the first line.
    public IReadOnlyDictionary<string, int> Entries { get; }

    // One line per index line that was skipped, naming it and the reason.
    public IReadOnlyList<string> Faults { get; }

    ZixIndex(List<string?> dirs, Dictionary<string, int> entries, List<string> faults) =>
        (Dirs, Entries, Faults) = (dirs, entries, faults);

    // Throws InvalidDataException when the header or a section is missing. A bad entry line is
    // skipped and listed in Faults, so the other entries still resolve.
    public static ZixIndex Parse(string text)
    {
        var lines = text.Split('\n')
            .Select(l => l.Split('#')[0].Trim())
            .Where(l => l.Length > 0)
            .GetEnumerator();
        if (!lines.MoveNext() || !int.TryParse(lines.Current, out _))
            throw new InvalidDataException("index header line (the entry count) is missing");
        var dirs = new List<string?>();
        var faults = new List<string>();
        for (var section = 1; section <= 2; section++)
            while (true)
            {
                if (!lines.MoveNext()) throw new InvalidDataException($"index section {section} is not closed by a '-' line");
                if (lines.Current[0] == '-') break;
                if (section != 2) continue; // the volume table only serves CD installs
                var parts = lines.Current.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2 && parts[0].Equals("dir:", StringComparison.OrdinalIgnoreCase)) dirs.Add(parts[1].Trim());
                else
                {
                    dirs.Add(null);
                    faults.Add($"path line '{lines.Current}' is not a DIR: line (unsupported)");
                }
            }
        var entries = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        while (lines.MoveNext())
        {
            var parts = lines.Current.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2 || !int.TryParse(parts[0], out var dir))
                faults.Add($"index line '{lines.Current}' is not '<dir index> <file name>'");
            else
            {
                // The game keys names by 16 bytes (FUN_004b8eb0 copies with strncpy(.., 16)); the stock
                // index has a name that runs past them, and the 16 bytes are the archive's entry name.
                var name = parts[1].Trim();
                name = name[..Math.Min(16, name.Length)];
                if (dir < 1 || dir > dirs.Count) faults.Add($"{name}: dir index {dir} is outside the {dirs.Count} listed dirs");
                else entries.TryAdd(name, dir);
            }
        }
        return new ZixIndex(dirs, entries, faults);
    }
}
