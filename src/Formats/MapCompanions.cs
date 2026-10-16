using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;

namespace OpenArmada.Formats;

// A start location of the .mdf: a cell of the 118 x 118 grid (0..117 on both axes) the map is stretched onto.
public readonly record struct StartCell(int X, int Y);

// The .mdf next to a multiplayer map: `StartLocations = n`, then `Start1 = x y` to `Startn = x y`.
public static class Mdf
{
    public const int MaxCell = 117;

    // Throws InvalidDataException when the count is outside 2..8, a start is missing or outside the grid,
    // a line is given twice, or a line is not one of these (the game would fall back to built-in defaults; the reader reports).
    public static IReadOnlyList<StartCell> Read(string text)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0)
                continue;
            var eq = line.IndexOf('=');
            if (eq < 0)
                throw new InvalidDataException($"mdf line '{line}' is not 'name = values'");
            var key = line[..eq].Trim();
            if (!values.TryAdd(key, line[(eq + 1)..].Trim()))
                throw new InvalidDataException($"mdf line '{key}' is given twice");
        }
        if (
            !values.Remove("StartLocations", out var countText)
            || !int.TryParse(countText, out var count)
            || count is < 2 or > 8
        )
            throw new InvalidDataException("mdf StartLocations is missing or outside 2..8");
        var cells = new List<StartCell>();
        for (var k = 1; k <= count; k++)
        {
            var key = $"Start{k}";
            if (!values.Remove(key, out var v))
                throw new InvalidDataException($"mdf has StartLocations = {count} but no {key}");
            var xy = v.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (
                xy.Length != 2
                || !int.TryParse(xy[0], out var x)
                || !int.TryParse(xy[1], out var y)
                || x is < 0 or > MaxCell
                || y is < 0 or > MaxCell
            )
                throw new InvalidDataException($"mdf {key} is not two cells of 0..{MaxCell}: '{v}'");
            cells.Add(new StartCell(x, y));
        }
        if (values.Count > 0)
            throw new InvalidDataException($"mdf line '{values.Keys.First()}' is not read");
        return cells;
    }
}

// The scripted camera paths of 3D_Paths/*.pth: a point count, then x y z per point (relative to the first point).
public static class Pth
{
    public static IReadOnlyList<Vector3> Read(string text)
    {
        var t = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (
            t.Length == 0
            || !int.TryParse(t[0], out var count)
            || count < 0
            || (t.Length - 1) % 3 != 0
            || count != (t.Length - 1) / 3
        )
            throw new InvalidDataException(
                $"pth does not hold a count and exactly that many points of three numbers ({t.Length} values)"
            );
        float F(string s) =>
            float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) && float.IsFinite(f)
                ? f
                : throw new InvalidDataException($"pth value '{s}' is not a finite number");
        return Enumerable
            .Range(0, count)
            .Select(i => new Vector3(F(t[1 + i * 3]), F(t[2 + i * 3]), F(t[3 + i * 3])))
            .ToList();
    }
}
