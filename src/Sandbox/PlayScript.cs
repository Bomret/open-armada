using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using OpenArmada.Formats;

namespace OpenArmada.Sandbox;

/// <summary>One step of a play script; every step keeps the line of the script it came from.</summary>
public abstract record PlayStep
{
    /// <summary>The 1-based line of the step in the script.</summary>
    public required int Line { get; init; }
}

/// <summary><c>wait &lt;seconds&gt;</c>: lets simulated time pass.</summary>
public sealed record WaitStep : PlayStep
{
    /// <summary>The simulated seconds to wait; zero or more.</summary>
    public required double Seconds { get; init; }
}

/// <summary><c>dump &lt;name&gt;</c>: writes a state dump to <c>&lt;name&gt;.txt</c> of the script's output folder.</summary>
public sealed record DumpStep : PlayStep
{
    /// <summary>The file stem; it holds no path separator.</summary>
    public required string Name { get; init; }
}

/// <summary><c>expect &lt;unit&gt; &lt;field&gt; &lt;op&gt; &lt;value&gt;</c>: checks one value of a unit's state.</summary>
public sealed record ExpectStep : PlayStep
{
    /// <summary>The unit's name as the script writes it (see <see cref="UnitNames"/>).</summary>
    public required string Unit { get; init; }

    /// <summary>The field: <c>shields</c>, <c>crew</c>, <c>destroyed</c>, <c>dying</c>, <c>target</c>, <c>team</c>, <c>race</c>, or a system name of <see cref="SystemNames"/>.</summary>
    public required string Field { get; init; }

    /// <summary>One of <c>&lt;</c>, <c>&lt;=</c>, <c>=</c>, <c>&gt;=</c>, <c>&gt;</c>; <c>destroyed</c>, <c>dying</c>, <c>target</c>, <c>team</c>, and <c>race</c> take only <c>=</c>.</summary>
    public required string Op { get; init; }

    /// <summary>The value as written: a number, <c>true</c> or <c>false</c>, an integer for <c>team</c>, or a unit name or <c>none</c> for <c>target</c>, a race name or <c>none</c> for <c>race</c>.</summary>
    public required string Value { get; init; }

    /// <summary>The number <see cref="Value"/> reads as, or null for a <c>destroyed</c>, <c>dying</c>, <c>target</c>, or <c>team</c> step.</summary>
    public double? Number =>
        double.TryParse(Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : null;
}

/// <summary><c>select &lt;unit&gt;</c>: a left click at the unit's projected screen point.</summary>
public sealed record SelectStep : PlayStep
{
    /// <summary>The unit's name as the script writes it.</summary>
    public required string Unit { get; init; }
}

/// <summary><c>attack &lt;unit&gt;</c>: a right click at the unit's projected screen point.</summary>
public sealed record AttackStep : PlayStep
{
    /// <summary>The unit's name as the script writes it.</summary>
    public required string Unit { get; init; }
}

/// <summary><c>transport &lt;unit&gt;</c>: the transport chord, then a left click at the unit's projected screen point.</summary>
public sealed record TransportStep : PlayStep
{
    /// <summary>The unit's name as the script writes it.</summary>
    public required string Unit { get; init; }
}

/// <summary>
/// <c>special &lt;slot&gt; [&lt;unit&gt;]</c>: the special weapon key of the slot (<c>fire_special_N</c>), then, with a unit, a
/// left click at the unit's projected screen point. Without a unit it is the key alone, which turns a running weapon off.
/// </summary>
public sealed record SpecialStep : PlayStep
{
    /// <summary>The special weapon key's number, 1 to 6.</summary>
    public required int Slot { get; init; }

    /// <summary>The unit's name as the script writes it; null for the key alone.</summary>
    public string? Unit { get; init; }
}

/// <summary><c>move &lt;x&gt; &lt;z&gt;</c>: a right click at the screen point of a point of the combat plane.</summary>
public sealed record MoveStep : PlayStep
{
    /// <summary>The x of the point, in the position coordinates of the state dump.</summary>
    public required double X { get; init; }

    /// <summary>The z of the point, in the position coordinates of the state dump.</summary>
    public required double Z { get; init; }
}

/// <summary><c>box &lt;left&gt; &lt;top&gt; &lt;right&gt; &lt;bottom&gt;</c>: a drag between two viewport points.</summary>
public sealed record BoxRectStep : PlayStep
{
    /// <summary>The drag's start x, in viewport pixels.</summary>
    public required float Left { get; init; }

    /// <summary>The drag's start y, in viewport pixels.</summary>
    public required float Top { get; init; }

    /// <summary>The drag's end x, in viewport pixels.</summary>
    public required float Right { get; init; }

    /// <summary>The drag's end y, in viewport pixels.</summary>
    public required float Bottom { get; init; }
}

/// <summary><c>box &lt;unit&gt;...</c>: a drag around the named units, which selects exactly them.</summary>
public sealed record BoxUnitsStep : PlayStep
{
    /// <summary>The units' names as the script writes them; at least one.</summary>
    public required IReadOnlyList<string> Units { get; init; }
}

/// <summary><c>press &lt;key&gt;</c>: a key press and release in one frame.</summary>
public sealed record PressStep : PlayStep
{
    /// <summary>
    /// The key as Godot names it (<c>Escape</c>, <c>Left</c>, <c>A</c>), or a chord with its modifiers first
    /// (<c>Shift+G</c>); the executor resolves it.
    /// </summary>
    public required string Key { get; init; }
}

/// <summary><c>hold &lt;key&gt; &lt;seconds&gt;</c>: presses a key, lets the time pass like a wait, and releases it.</summary>
public sealed record HoldStep : PlayStep
{
    /// <summary>The key as Godot names it.</summary>
    public required string Key { get; init; }

    /// <summary>The simulated seconds the key stays down; zero or more.</summary>
    public required double Seconds { get; init; }
}

/// <summary>The release of a key a <see cref="HoldStep"/> pressed; the run adds it when the hold's time has passed.</summary>
public sealed record ReleaseStep : PlayStep
{
    /// <summary>The key as Godot names it.</summary>
    public required string Key { get; init; }
}

/// <summary><c>scroll &lt;up|down&gt; [count]</c>: mouse wheel notches, which change the camera height.</summary>
public sealed record ScrollStep : PlayStep
{
    /// <summary>Whether the wheel turns up.</summary>
    public required bool Up { get; init; }

    /// <summary>The number of notches; one or more.</summary>
    public required int Count { get; init; }
}

/// <summary><c>turn &lt;pixels&gt;</c>: a middle-button drag of that many pixels to the right, which rotates the camera.</summary>
public sealed record TurnStep : PlayStep
{
    /// <summary>The horizontal drag distance in viewport pixels; negative drags left.</summary>
    public required double Pixels { get; init; }
}

/// <summary>
/// <c>shot &lt;name&gt; [unit [scale]]</c>: writes a screenshot to <c>&lt;name&gt;.png</c> of the script's output folder,
/// the whole frame or, with a unit, the crop around it (<see cref="PlayTarget.CropOf"/>) enlarged by the scale.
/// </summary>
public sealed record ShotStep : PlayStep
{
    /// <summary>The file stem; it holds no path separator.</summary>
    public required string Name { get; init; }

    /// <summary>The unit's name as the script writes it, or null for the whole frame.</summary>
    public string? Unit { get; init; }

    /// <summary>The whole-number factor the crop is enlarged by; one or more.</summary>
    public int Scale { get; init; } = 1;
}

/// <summary>
/// A play script: a text file of steps, one per line, run in file order. Blank lines and lines that start with
/// <c>#</c> are skipped. A line that cannot be read is a problem and adds no step. Parsing never logs and never
/// throws for bad text (INV-0002, INV-0007); the play run reports the problems.
/// </summary>
public sealed class PlayScript
{
    /// <summary>The fixed seed of the play run's combat random source (INV-0008).</summary>
    // stand-in: any fixed number gives a repeatable run; the game's own rolls are not seeded this way.
    public const int Seed = 1;

    static readonly string[] Ops = ["<=", ">=", "<", ">", "="];

    /// <summary>The script's path as given; problems and log entries name it.</summary>
    public required string Path { get; init; }

    /// <summary>The script's file name without extension: the name of its output folder under <c>play-out/</c>.</summary>
    public string Stem => System.IO.Path.GetFileNameWithoutExtension(Path);

    /// <summary>The steps in file order.</summary>
    public required IReadOnlyList<PlayStep> Steps { get; init; }

    /// <summary>One line per problem, each naming the script and, where one applies, the line.</summary>
    public required IReadOnlyList<string> Problems { get; init; }

    /// <summary>Reads the script at <c>path</c>; a file that cannot be read is one problem and no steps.</summary>
    public static PlayScript Read(string path)
    {
        try
        {
            return Parse(File.ReadAllText(path), path);
        }
        catch (Exception e) // a missing or unreadable script is a problem, whatever the reason (INV-0007)
        {
            return new PlayScript
            {
                Path = path,
                Steps = [],
                Problems = [$"{path}: script cannot be read: {e.Message}"],
            };
        }
    }

    /// <summary>Parses <c>text</c>; <c>path</c> only names the script in problems and in <see cref="Path"/>.</summary>
    public static PlayScript Parse(string text, string path)
    {
        var steps = new List<PlayStep>();
        var problems = new List<string>();
        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0 || line[0] == '#')
                continue;
            var (step, problem) = ParseLine(line, i + 1);
            if (step != null)
            {
                steps.Add(step);
            }
            else
            {
                problems.Add($"{path} line {i + 1}: {problem}");
            }
        }

        return new PlayScript
        {
            Path = path,
            Steps = steps,
            Problems = problems,
        };
    }

    static (PlayStep? Step, string? Problem) ParseLine(string line, int number)
    {
        var words = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var args = words.Skip(1).ToArray();
        switch (words[0])
        {
            case "wait":
                if (args.Length != 1)
                    return Wrong(words[0], 1, args.Length);
                if (!Number(args[0], out var seconds) || seconds < 0)
                    return (null, $"'{args[0]}' is not a number of seconds of zero or more");

                return (new WaitStep { Line = number, Seconds = seconds }, null);
            case "dump":
                if (args.Length != 1)
                    return Wrong(words[0], 1, args.Length);
                if (NoFileName(args[0]))
                    return (null, $"'{args[0]}' is no file name");

                return (new DumpStep { Line = number, Name = args[0] }, null);
            case "expect":
                return ParseExpect(args, number);
            default:
                return ParseInput(words[0], args, number);
        }
    }

    static bool NoFileName(string name) => name is "." or ".." || name.IndexOfAny(['/', '\\']) >= 0;

    // The steps that reach the sandbox as input events.
    static (PlayStep? Step, string? Problem) ParseInput(string word, string[] args, int number)
    {
        switch (word)
        {
            case "select" or "attack" or "transport" or "press" when args.Length != 1:
                return Wrong(word, 1, args.Length);
            case "transport":
                return (new TransportStep { Line = number, Unit = args[0] }, null);
            case "special":
                if (args.Length is not (1 or 2))
                    return (null, $"'special' takes 1 or 2 arguments, found {args.Length}");
                if (
                    !int.TryParse(args[0], NumberStyles.None, CultureInfo.InvariantCulture, out var slot)
                    || slot is < 1 or > 6
                )
                    return (null, $"'{args[0]}' is not a special weapon key number from 1 to 6");

                return (
                    new SpecialStep
                    {
                        Line = number,
                        Slot = slot,
                        Unit = args.Length > 1 ? args[1] : null,
                    },
                    null
                );
            case "select":
                return (new SelectStep { Line = number, Unit = args[0] }, null);
            case "attack":
                return (new AttackStep { Line = number, Unit = args[0] }, null);
            case "press":
                return (new PressStep { Line = number, Key = args[0] }, null);
            case "shot":
                if (args.Length is not (1 or 2 or 3))
                    return (null, $"'shot' takes 1 to 3 arguments, found {args.Length}");
                if (NoFileName(args[0]))
                    return (null, $"'{args[0]}' is no file name");
                var scale = 1;
                if (
                    args.Length == 3
                    && (!int.TryParse(args[2], NumberStyles.None, CultureInfo.InvariantCulture, out scale) || scale < 1)
                )
                    return (null, $"'{args[2]}' is not a whole number of one or more");

                return (
                    new ShotStep
                    {
                        Line = number,
                        Name = args[0],
                        Unit = args.Length > 1 ? args[1] : null,
                        Scale = scale,
                    },
                    null
                );
            case "move":
                if (args.Length != 2)
                    return Wrong(word, 2, args.Length);
                if (!Number(args[0], out var x))
                    return (null, $"'{args[0]}' is not a number");
                if (!Number(args[1], out var z))
                    return (null, $"'{args[1]}' is not a number");

                return (
                    new MoveStep
                    {
                        Line = number,
                        X = x,
                        Z = z,
                    },
                    null
                );
            case "box":
                return ParseBox(args, number);
            case "hold":
                if (args.Length != 2)
                    return Wrong(word, 2, args.Length);
                if (!Number(args[1], out var seconds) || seconds < 0)
                    return (null, $"'{args[1]}' is not a number of seconds of zero or more");

                return (
                    new HoldStep
                    {
                        Line = number,
                        Key = args[0],
                        Seconds = seconds,
                    },
                    null
                );
            case "scroll":
                if (args.Length is not (1 or 2))
                    return (null, $"'scroll' takes 1 or 2 arguments, found {args.Length}");
                if (args[0] is not ("up" or "down"))
                    return (null, $"'{args[0]}' is not up or down");
                var count = 1;
                if (
                    args.Length == 2
                    && (!int.TryParse(args[1], NumberStyles.None, CultureInfo.InvariantCulture, out count) || count < 1)
                )
                    return (null, $"'{args[1]}' is not a whole number of one or more");

                return (
                    new ScrollStep
                    {
                        Line = number,
                        Up = args[0] == "up",
                        Count = count,
                    },
                    null
                );
            case "turn":
                if (args.Length != 1)
                    return Wrong(word, 1, args.Length);
                if (!Number(args[0], out var pixels))
                    return (null, $"'{args[0]}' is not a number");

                return (new TurnStep { Line = number, Pixels = pixels }, null);
            default:
                return (null, $"unknown step '{word}'");
        }
    }

    // Four numbers are a screen rectangle; other numbers are a problem; anything else is a list of unit names.
    static (PlayStep?, string?) ParseBox(string[] args, int number)
    {
        if (args.Length == 0)
            return (null, "'box' takes 4 numbers or one or more unit names, found 0 arguments");
        if (args.Length != 4 && args.All(a => Number(a, out _)))
            return (null, $"'box' takes 4 numbers or unit names, found {args.Length} numbers");
        if (args.Length == 4 && Number(args[0], out _))
        {
            var edges = new float[4];
            for (var i = 0; i < 4; i++)
            {
                if (!Number(args[i], out var edge))
                    return (null, $"'{args[i]}' is not a number");
                edges[i] = (float)edge;
            }

            return (
                new BoxRectStep
                {
                    Line = number,
                    Left = edges[0],
                    Top = edges[1],
                    Right = edges[2],
                    Bottom = edges[3],
                },
                null
            );
        }

        return (new BoxUnitsStep { Line = number, Units = args }, null);
    }

    static (PlayStep?, string?) ParseExpect(string[] args, int number)
    {
        if (args.Length != 4)
            return Wrong("expect", 4, args.Length);
        var field = args[1].ToLowerInvariant();
        var system = SystemNames.All.FirstOrDefault(s => s.Equals(field, StringComparison.OrdinalIgnoreCase));
        if (system != null)
        {
            field = system;
        }
        else if (
            field is not ("shields" or "crew" or "special" or "destroyed" or "dying" or "target" or "team" or "race")
        )
        {
            return (null, $"unknown field '{args[1]}'");
        }

        var op = args[2];
        if (!Ops.Contains(op))
            return (null, $"unknown operator '{op}', use one of < <= = >= >");
        if (field is "destroyed" or "dying" or "target" or "team" or "race")
        {
            if (op != "=")
                return (null, $"'{field}' takes only '=', not '{op}'");
            if (field == "team")
            {
                if (!int.TryParse(args[3], NumberStyles.None, CultureInfo.InvariantCulture, out _))
                    return (null, $"'{args[3]}' is not an integer");
            }
            else if (field is not ("target" or "race") && args[3] is not ("true" or "false"))
                return (null, $"'{args[3]}' is not true or false");
        }
        else
        {
            if (!Number(args[3], out _))
                return (null, $"'{args[3]}' is not a number");
        }

        return (
            new ExpectStep
            {
                Line = number,
                Unit = args[0],
                Field = field,
                Op = op,
                Value = args[3],
            },
            null
        );
    }

    static (PlayStep?, string?) Wrong(string step, int want, int got) =>
        (null, $"'{step}' takes {want} argument{(want == 1 ? "" : "s")}, found {got}");

    static bool Number(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);
}
