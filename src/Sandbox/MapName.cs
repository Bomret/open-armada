using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenArmada.Sandbox;

// The map the user asked for: the install's name of it, or the problem that names why there is none.
public sealed record MapChoice
{
    public required string? Name { get; init; }
    public required string? Problem { get; init; }
}

// Matches the map name of the command line against the install's map names ("bzn/2duel.bzn") without
// regard to case, with or without the folder and the ".bzn" extension.
public static class MapName
{
    public static MapChoice Resolve(IReadOnlyCollection<string> installMaps, string requested)
    {
        var wanted = Bare(requested);
        var hit = installMaps.FirstOrDefault(m => Bare(m).Equals(wanted, StringComparison.OrdinalIgnoreCase));

        return hit != null
            ? new MapChoice { Name = hit, Problem = null }
            : new MapChoice
            {
                Name = null,
                Problem =
                    $"no map '{requested}' in this install; maps: {string.Join(", ", installMaps.Select(Bare).Order(StringComparer.OrdinalIgnoreCase))}",
            };
    }

    static string Bare(string name)
    {
        var file = name[(name.LastIndexOfAny(new[] { '/', '\\' }) + 1)..];

        return file.EndsWith(".bzn", StringComparison.OrdinalIgnoreCase) ? file[..^4] : file;
    }
}
