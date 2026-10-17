using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OpenArmada.Formats;

/// <summary>One object of a tech tree and the objects it requires before it can be built.</summary>
public sealed record TechEntry
{
    /// <summary>The name of the object, the first word of its line.</summary>
    public required string Name { get; init; }

    /// <summary>The names of the objects it requires, in line order; empty when the count is 0.</summary>
    public required IReadOnlyList<string> Requires { get; init; }
}

/// <summary>
/// A tech tree (.tt): one object per line as <c>name count required...</c>, where count is the number of
/// required names that follow. A tree names no race; the race of an entry is the <c>race</c> of its object
/// definition. Blank lines are skipped; any other line that does not fit is corrupt.
/// </summary>
public sealed class TechTree
{
    /// <summary>The objects in file order.</summary>
    public IReadOnlyList<TechEntry> Entries { get; }

    TechTree(IReadOnlyList<TechEntry> entries) => Entries = entries;

    /// <summary>Throws InvalidDataException naming the first bad line.</summary>
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
            entries.Add(new TechEntry { Name = parts[0], Requires = parts.Skip(2).ToList() });
        }

        return new TechTree(entries);
    }
}
