using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

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

    /// <summary>The field: <c>shields</c>, <c>crew</c>, <c>destroyed</c>, <c>target</c>, or a system name of <see cref="SystemNames"/>.</summary>
    public required string Field { get; init; }

    /// <summary>One of <c>&lt;</c>, <c>&lt;=</c>, <c>=</c>, <c>&gt;=</c>, <c>&gt;</c>; <c>destroyed</c> and <c>target</c> take only <c>=</c>.</summary>
    public required string Op { get; init; }

    /// <summary>The value as written: a number, <c>true</c> or <c>false</c>, or a unit name or <c>none</c> for <c>target</c>.</summary>
    public required string Value { get; init; }

    /// <summary>The number <see cref="Value"/> reads as, or null for a <c>destroyed</c> or <c>target</c> step.</summary>
    public double? Number =>
        double.TryParse(Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : null;
}

/// <summary>
/// A play script: a text file of steps, one per line, run in file order. Blank lines and lines that start with
/// <c>#</c> are skipped. A line that cannot be read is a problem and adds no step. Parsing never logs and never
/// throws for bad text (INV-0002, INV-0007); the play run reports the problems.
/// </summary>
public sealed class PlayScript
{
    /// <summary>The fixed seed of the play run's combat random source (INV-0006).</summary>
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
                if (args[0] is "." or ".." || args[0].IndexOfAny(['/', '\\']) >= 0)
                    return (null, $"'{args[0]}' is no file name");

                return (new DumpStep { Line = number, Name = args[0] }, null);
            case "expect":
                return ParseExpect(args, number);
            default:
                return (null, $"unknown step '{words[0]}'");
        }
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
        else if (field is not ("shields" or "crew" or "destroyed" or "target"))
        {
            return (null, $"unknown field '{args[1]}'");
        }

        var op = args[2];
        if (!Ops.Contains(op))
            return (null, $"unknown operator '{op}', use one of < <= = >= >");
        if (field is "destroyed" or "target")
        {
            if (op != "=")
                return (null, $"'{field}' takes only '=', not '{op}'");
            if (field == "destroyed" && args[3] is not ("true" or "false"))
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
