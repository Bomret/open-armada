using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OpenArmada.Formats;

// One object of a tech tree and the objects it requires before it can be built.
public sealed record TechEntry(string Name, IReadOnlyList<string> Requires);

// A tech tree (.tt): one object per line as `name count required...`, where count is the number of
// required names that follow. A tree names no race; the race of an entry is the `race` of its object
// definition. Blank lines are skipped; any other line that does not fit is corrupt.
public sealed class TechTree
{
    public IReadOnlyList<TechEntry> Entries { get; }

    TechTree(IReadOnlyList<TechEntry> entries) => Entries = entries;

    // Throws InvalidDataException naming the first bad line.
    public static TechTree Parse(string text)
    {
        var entries = new List<TechEntry>();
        var n = 0;
        foreach (var line in text.Split('\n'))
        {
            n++;
            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                continue;
            if (parts.Length < 2 || !int.TryParse(parts[1], out var count) || count < 0 || parts.Length != 2 + count)
                throw new InvalidDataException(
                    $"tech tree line {n}: expected 'name count' followed by that many names"
                );
            entries.Add(new TechEntry(parts[0], parts.Skip(2).ToList()));
        }
        return new TechTree(entries);
    }
}
