using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using OpenArmada.Assets;

namespace OpenArmada.Sandbox;

/// <summary>
/// The map summary: what one map holds, as text lines in the rules of <c>docs/maps.md</c>. The map preview and
/// the <c>--maps</c> run show the same lines. Built without logging: a class that cannot be resolved is a line
/// of the summary and sets <see cref="HasUnresolved"/>, never an exception.
/// </summary>
public sealed record MapSummary
{
    // The game's combat hulls: a ship whose include chain reaches one of them is a warship (CONTEXT.md).
    static readonly HashSet<string> CombatHulls = new(StringComparer.OrdinalIgnoreCase)
    {
        "battle.odf",
        "cruise1.odf",
        "cruise2.odf",
        "destroy.odf",
        "scout.odf",
        "special.odf",
    };

    /// <summary>The summary's lines, in order, without the map's name.</summary>
    public required IReadOnlyList<string> Lines { get; init; }

    /// <summary>Whether a class of the map is unresolved, so whether combat at start may be understated.</summary>
    public required bool HasUnresolved { get; init; }

    /// <summary>
    /// The summary of <c>map</c>, resolving each of its classes in <c>files</c> once. Never throws for a class:
    /// a missing definition, an include-chain fault, or a definition the unit rules cannot classify makes the
    /// class unresolved.
    /// </summary>
    public static MapSummary Of(GameFileSystem files, MapAsset map)
    {
        var objects = map.Map.Objects;
        var kinds = objects
            .Select(o => o.Class)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(c => c, c => Classify(files, c), StringComparer.OrdinalIgnoreCase);
        var warTeams = Teams(objects, o => kinds[o.Class] == Kind.Warship);
        var unresolvedTeams = Teams(objects, o => kinds[o.Class] == Kind.Unresolved).Except(warTeams).ToList();
        var combat =
            warTeams.Count >= 2 ? "yes"
            : warTeams.Count + unresolvedTeams.Count >= 2 ? "unknown"
            : "no";
        var unresolved = kinds.Where(k => k.Value == Kind.Unresolved).Select(k => k.Key).ToList();
        // team 8 is the monster team unless the map seats eight players
        var neutral = objects
            .Where(o => (o.Team == 0 && kinds[o.Class] == Kind.Warship) || (o.Team == 8 && map.Starts.Count < 8))
            .GroupBy(o => o.Team)
            .OrderBy(g => g.Key)
            .Select(g => $"t{g.Key}: {Classes(g)}")
            .ToList();

        // multiplayer exactly when the map has a .mdf; its start count cannot tell the groups apart
        var multiplayer = files.Find(Path.ChangeExtension(map.Name, ".mdf")) != null;

        var lines = new List<string>
        {
            $"Group: {(multiplayer ? "multiplayer" : "campaign and other")}",
            $"Start positions: {(multiplayer ? map.Starts.Count.ToString(CultureInfo.InvariantCulture) : "none")}",
            $"Teams with a warship: {(warTeams.Count == 0 ? "none" : string.Join(", ", warTeams.Select(t => $"t{t}")))}",
            $"Combat at start: {combat}",
        };
        lines.Add($"Neutral and monster units: {(neutral.Count == 0 ? "none" : string.Join("; ", neutral))}");
        if (unresolved.Count > 0)
        {
            lines.Add($"Unresolved classes: {string.Join(", ", unresolved.Order(StringComparer.OrdinalIgnoreCase))}");
        }

        lines.Add("Objects per team:");
        lines.AddRange(
            objects.GroupBy(o => o.Team).OrderBy(g => g.Key).Select(g => $"  t{g.Key} ({g.Count()}): {Classes(g)}")
        );

        return new MapSummary { Lines = lines, HasUnresolved = unresolved.Count > 0 };
    }

    // Each class once, spelled as its first object spells it, in case-insensitive order, with ×N past one.
    static string Classes(IEnumerable<Formats.MapObject> objects) =>
        string.Join(
            ", ",
            objects
                .GroupBy(o => o.Class, StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.First().Class, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.Count() == 1 ? g.First().Class : $"{g.First().Class} ×{g.Count()}")
        );

    // The teams 1 to 8 holding an object that matches, in order; team 0 never counts.
    static List<int> Teams(IEnumerable<Formats.MapObject> objects, Func<Formats.MapObject, bool> match) =>
        objects.Where(o => o.Team > 0 && match(o)).Select(o => o.Team).Distinct().Order().ToList();

    static Kind Classify(GameFileSystem files, string cls)
    {
        try
        {
            var definition = ResolvedDefinition.Resolve(files, cls + ".odf");
            if (definition.Problems.Count > 0)
                return Kind.Unresolved;

            return
                UnitRules.Classify(definition) is Ship
                && definition.Chain.Any(n => CombatHulls.Contains(n.Replace('\\', '/').Split('/')[^1]))
                ? Kind.Warship
                : Kind.Other;
        }
        catch (Exception)
        {
            return Kind.Unresolved;
        }
    }

    enum Kind
    {
        Warship,
        Other,
        Unresolved,
    }
}
