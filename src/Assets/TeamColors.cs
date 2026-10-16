using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;

namespace OpenArmada.Assets;

// The team colors of the game: teamcolor.odf (in assets.zfs, or addon/) holds `name = r g b` for each
// of 27 named slots (Armada.exe FUN_00448aa0 reads the file into the table at 0x633ac8). A model's
// `team*` materials take one of these colors. A slot the file does not give is black, as in the game;
// each such slot and a line that is not three numbers is a note.
public sealed class TeamColors
{
    // The game's slot names in table order: 0 neutral, 1 to 8 the players' colors, then race colors.
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

    public IReadOnlyList<string> Notes => _notes;

    // Throws KeyNotFoundException naming the slot when it is not one of SlotNames.
    public Vector3 Color(string slot) =>
        _colors.TryGetValue(slot, out var color)
            ? color
            : throw new KeyNotFoundException($"unknown team-color slot '{slot}'");

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
            var tokens = file?.Find(name)?.Tokens;
            var color = Vector3.Zero;
            var valid = tokens != null && TryColor(tokens, out color);
            if (file != null && tokens == null)
                team._notes.Add($"teamcolor.odf has no color '{name}': black");
            else if (tokens != null && !valid)
                team._notes.Add($"teamcolor.odf: color '{name}' is not three numbers: black");
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
            if (
                !float.TryParse(t[i].TrimEnd('f'), NumberStyles.Float, CultureInfo.InvariantCulture, out v[i])
                || !float.IsFinite(v[i])
            )
                return false;
        color = new Vector3(v[0], v[1], v[2]);
        return true;
    }
}
