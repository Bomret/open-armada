using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;

namespace OpenArmada.Assets;

/// <summary>
/// The team colors of the game: teamcolor.odf (in assets.zfs, or addon/) holds <c>name = r g b</c> for each
/// of 27 named slots (Armada.exe FUN_00448aa0 reads the file into the table at 0x633ac8). A model's
/// <c>team*</c> materials take one of these colors. As in the game's getter (0x554320, <c>_strnicmp</c> over the
/// file's lines), a slot takes the first key that starts with its name, regardless of case. A slot no key
/// matches is black and is no note, as the game copies a zero vector (0x68bb00) without a report. A first
/// match that is not at least three numbers is black and a note; the scan does not go on to a later key.
/// </summary>
public sealed class TeamColors
{
    /// <summary>The game's slot names in table order: 0 neutral, 1 to 8 the players' colors, then race colors.</summary>
    public static readonly IReadOnlyList<string> SlotNames = new[]
    {
        "empty",
        "white",
        "red",
        "blue",
        "green",
        "yellow",
        "purple",
        "cyan",
        "brown",
        "orange",
        "pink",
        "magenta",
        "gray",
        "black",
        "ferengi",
        "dominion",
        "klingon",
        "romulan",
        "borg",
        "federation",
        "cardassian",
        "breen",
        "sona",
        "alien",
        "brickred",
        "burntsienna",
        "cerulean",
    };

    readonly Dictionary<string, Vector3> _colors = new(StringComparer.OrdinalIgnoreCase);
    readonly List<string> _notes = new();

    /// <summary>
    /// What the asset check prints without failing: a missing teamcolor.odf, and each slot whose first matching key
    /// is not at least three numbers (that slot is black).
    /// </summary>
    public IReadOnlyList<string> Notes => _notes;

    /// <summary>Throws KeyNotFoundException naming the slot when it is not one of SlotNames.</summary>
    public Vector3 Color(string slot) =>
        _colors.TryGetValue(slot, out var color)
            ? color
            : throw new KeyNotFoundException($"unknown team-color slot '{slot}'");

    /// <summary>
    /// Reads teamcolor.odf from the install and fills every slot of <see cref="SlotNames"/>. A missing file is a
    /// note, and every slot is black.
    /// </summary>
    /// <exception cref="OpenArmada.Formats.UnsupportedFormatException">when teamcolor.odf is a compressed archive entry.</exception>
    public static TeamColors Load(GameFileSystem files)
    {
        var team = new TeamColors();
        ResolvedDefinition? file = null;
        try
        {
            file = ResolvedDefinition.Resolve(files, "teamcolor.odf");
        }
        catch (FileNotFoundException)
        {
            team._notes.Add("teamcolor.odf not found: every team color is black");
        }

        foreach (var name in SlotNames)
        {
            var match = file?.Values.FirstOrDefault(v => v.Key.StartsWith(name, StringComparison.OrdinalIgnoreCase));
            var color = Vector3.Zero;
            var valid = match != null && TryColor(match.Tokens, out color);
            if (match != null && !valid)
            {
                team._notes.Add(
                    $"teamcolor.odf: color '{name}' (key '{match.Key}') is not at least three numbers: black"
                );
            }

            team._colors[name] = valid ? color : Vector3.Zero;
        }

        return team;
    }

    static bool TryColor(IReadOnlyList<string> t, out Vector3 color)
    {
        color = default;
        if (t.Count < 3)
            return false;
        var v = new float[3];
        for (var i = 0; i < 3; i++)
        {
            if (!ResolvedDefinition.TryNumber(t[i], out v[i]))
                return false;
        }

        color = new Vector3(v[0], v[1], v[2]);

        return true;
    }
}
