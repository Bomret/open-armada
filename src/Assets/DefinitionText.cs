using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OpenArmada.Formats;

namespace OpenArmada.Assets;

/// <summary>The text the browser previews of a definition and of a tech tree.</summary>
public static class DefinitionText
{
    const string Unknown = "unknown race";

    /// <summary>
    /// The lines <see cref="Format(ResolvedDefinition)"/> shows for the definition's problems, then its notes
    /// (<c>problem: …</c>, <c>note: …</c>); the preview logs the same lines.
    /// </summary>
    public static IReadOnlyList<string> Remarks(ResolvedDefinition def) =>
        def.Problems.Select(p => "problem: " + p).Concat(def.Notes.Select(n => "note: " + n)).ToList();

    /// <summary>
    /// The chain, the problems, then one line per key: <c>key = value    [mark]</c>, the mark saying whether the
    /// value is the definition's own, inherited from an include, or overridden by the override layer.
    /// </summary>
    public static string Format(ResolvedDefinition def)
    {
        var sb = new StringBuilder();
        sb.Append(def.Name).Append(" (").AppendJoin(" <- ", def.Chain).Append(")\n");
        foreach (var remark in Remarks(def))
        {
            sb.Append(remark).Append('\n');
        }

        sb.Append('\n');
        foreach (var v in def.Values)
        {
            var mark = v.Origin switch
            {
                ValueOrigin.Own => "own",
                ValueOrigin.Inherited => $"inherited from {v.Source}",
                _ => $"overridden by the override layer, {v.Source}",
            };
            sb.Append(v.Key)
                .Append(" = ")
                .AppendJoin(' ', v.Tokens.Select(t => t.Any(char.IsWhiteSpace) ? $"\"{t}\"" : t))
                .Append("    [")
                .Append(mark)
                .Append("]\n");
        }

        return sb.ToString();
    }

    /// <summary>
    /// The requirements of each entry, grouped under the race of its object definition (the <c>race</c> key);
    /// an entry whose definition is missing or has no race falls under "unknown race", listed last.
    /// </summary>
    public static string Format(GameFileSystem files, TechTree tree)
    {
        var sb = new StringBuilder();
        var groups = tree
            .Entries.GroupBy(e => RaceOf(files, e.Name))
            .OrderBy(g => g.Key == Unknown)
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase);
        foreach (var g in groups)
        {
            sb.Append(g.Key).Append('\n');
            foreach (var e in g.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
            {
                sb.Append("  ")
                    .Append(e.Name)
                    .Append(": ")
                    .Append(e.Requires.Count == 0 ? "no requirements" : "requires " + string.Join(", ", e.Requires))
                    .Append('\n');
            }
        }

        return sb.ToString();
    }

    static string RaceOf(GameFileSystem files, string definition)
    {
        try
        {
            return ResolvedDefinition.Resolve(files, definition).GetString("race");
        }
        catch (Exception e)
            when (e
                    is System.IO.FileNotFoundException
                        or System.IO.InvalidDataException
                        or UnsupportedFormatException
                        or DefinitionException
            )
        {
            return Unknown;
        }
    }
}
