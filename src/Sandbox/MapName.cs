using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenArmada.Sandbox;

/// <summary>The map the user asked for: the install's name of it, or the problem that names why there is none.</summary>
public sealed record MapChoice
{
    /// <summary>The install's name of the map, as the install spells it; null when there is none.</summary>
    public required string? Name { get; init; }

    /// <summary>Why there is no map; null when <see cref="Name"/> is set.</summary>
    public required string? Problem { get; init; }
}

/// <summary>
/// Matches the map name of the command line against the install's map names ("bzn/2duel.bzn") without
/// regard to case, with or without the folder and the ".bzn" extension.
/// </summary>
public static class MapName
{
    /// <summary>
    /// The first of <c>installMaps</c> that matches <c>requested</c>; when none matches, a problem that lists the
    /// install's map names, bare and sorted.
    /// </summary>
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
