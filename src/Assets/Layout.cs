using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using OpenArmada.Formats;

namespace OpenArmada.Assets;

/// <summary>A layout read into the map model: the map asset, or the problem that names why there is none.</summary>
public sealed record LayoutRead
{
    /// <summary>The map asset the layout describes; null when there is a <see cref="Problem"/>.</summary>
    public required MapAsset? Asset { get; init; }

    /// <summary>Why the layout cannot be read, naming the file and the line or JSON path; null when <see cref="Asset"/> is set.</summary>
    public required string? Problem { get; init; }
}

/// <summary>
/// Reader of a layout (ADR 0007): JSONC text that places objects for the map sandbox, read into the same map
/// asset a .bzn map gives, so the plan takes no layout type (INV-0009).
/// </summary>
public static class Layout
{
    static readonly JsonDocumentOptions Options = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// The full path of the layout at <c>path</c>: a relative path resolves against <c>start</c>, the directory the
    /// command was started in, which must be absolute; an absolute path stays as it is. Godot makes the
    /// <c>--path</c> project folder the working directory, so the caller passes the start directory in.
    /// </summary>
    public static string Resolve(string path, string start) => Path.GetFullPath(path, start);

    /// <summary>
    /// The directory the command was started in: <c>pwd</c>, the shell's <c>PWD</c> environment value, when it is
    /// set and fully qualified, else <c>currentDirectory</c>. Godot makes the <c>--path</c> folder the working
    /// directory, so only <c>PWD</c> still names the shell's start directory.
    /// </summary>
    public static string Start(string? pwd, string currentDirectory) =>
        pwd != null && Path.IsPathFullyQualified(pwd) ? pwd : currentDirectory;

    /// <summary>
    /// Reads the layout file at <c>path</c>, which names it in problems; a relative path resolves against the process
    /// working directory, so a caller under Godot passes a path from <see cref="Resolve"/>. A file that is missing, a
    /// directory, or unreadable is the result's problem, never an exception.
    /// </summary>
    public static LayoutRead Load(string path)
    {
        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception e) // a layout that cannot be read is a message, whatever the reason (INV-0002)
        {
            return Fail(path, $"cannot be read: {e.Message}");
        }

        return Read(path, text);
    }

    /// <summary>
    /// Reads the layout text of <c>fileName</c>; the file name only names the layout in problems and in the map's
    /// description. Never throws for bad text: every fault is the result's problem.
    /// </summary>
    public static LayoutRead Read(string fileName, string text)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(text, Options);
        }
        catch (JsonException e)
        {
            return Fail(fileName, $"line {(e.LineNumber ?? 0) + 1}: {Reason(e)}");
        }

        using (doc)
        {
            try
            {
                return new LayoutRead { Asset = Asset(fileName, doc.RootElement), Problem = null };
            }
            catch (LayoutException e)
            {
                return Fail(fileName, $"{e.Path}: {e.Message}");
            }
        }
    }

    static MapAsset Asset(string fileName, JsonElement root)
    {
        Fields(root, "", "bounds", "objects", "background", "teamColors", "ambient");
        var bounds = Required(root, "", "bounds");
        Fields(bounds, "bounds", "minX", "maxX", "minZ", "maxZ");
        var (minX, maxX) = Range(bounds, "bounds", "minX", "maxX");
        var (minZ, maxZ) = Range(bounds, "bounds", "minZ", "maxZ");
        var objects = Required(root, "", "objects");
        if (objects.ValueKind != JsonValueKind.Array)
            throw new LayoutException("objects", $"must be an array, not {Kind(objects)}");
        var placed = objects.EnumerateArray().Select((o, i) => Object(o, $"objects[{i}]", i)).ToList();
        var slots = new int[Bzn.TeamCount]; // 0: the team's own team-color slot, as on a map
        if (root.TryGetProperty("teamColors", out var colors))
        {
            Fields(colors, "teamColors", TeamKeys);
            foreach (var c in colors.EnumerateObject())
            {
                slots[Array.IndexOf(TeamKeys, c.Name)] = WholeNumber(c.Value, $"teamColors.{c.Name}");
            }
        }

        return new MapAsset
        {
            Name = fileName,
            Map = new BznMap
            {
                Version = 0,
                // The description seeds the asteroid fields; without the folder, a layout's rocks repeat from any
                // working directory.
                Description = Path.GetFileName(fileName),
                MissionFile = "",
                TerrainName = "",
                // Y is not read by the sandbox; a layout gives only the ground plane.
                Bounds = new MapBounds
                {
                    MinX = minX,
                    MaxX = maxX,
                    MinY = 0,
                    MaxY = 0,
                    MinZ = minZ,
                    MaxZ = maxZ,
                },
                Ambient = root.TryGetProperty("ambient", out var ambient)
                    ? Vector(ambient, "ambient")
                    // stand-in: the asset browser's model preview light, for a layout that gives no ambient color.
                    : new Vector3(0.35f, 0.35f, 0.4f),
                BackgroundArt = root.TryGetProperty("background", out var background)
                    ? Text(background, "background")
                    : "",
                TechTree = "",
                Race = "",
                Objects = placed,
                Teams = slots
                    .Select(slot => new MapTeam
                    {
                        Unknown1 = 0,
                        Unknown2 = 0,
                        Unknown3 = 0,
                        Unknown4 = 0,
                        Unknown5 = 0,
                        Color = slot,
                        SecondaryColor = 0,
                    })
                    .ToList(),
                MissionClass = "",
                MissionScript = "",
                MissionRules = "",
                MissionName = "",
                Aois = [],
                Paths = [],
            },
            Starts = [],
            Overview = null,
            ScriptedPaths = [],
        };
    }

    static readonly string[] Axes = ["right", "up", "front"];

    // The keys of teamColors: the team numbers, at their own index.
    static readonly string[] TeamKeys = Enumerable.Range(0, Bzn.TeamCount).Select(t => t.ToString()).ToArray();

    // Handle and id come from the index, so two objects with identical fields stay two objects and their
    // asteroid-field seeds are the same on every read.
    static MapObject Object(JsonElement o, string path, int index)
    {
        Fields(o, path, "class", "team", "position", "label", "right", "up", "front");
        var name = Text(Required(o, path, "class"), path + ".class");
        if (name.Length == 0)
            throw new LayoutException(path + ".class", "must not be empty");
        var t = WholeNumber(Required(o, path, "team"), path + ".team");
        if (t < 0 || t >= Bzn.TeamCount)
            throw new LayoutException(path + ".team", $"team {t} is outside 0..{Bzn.TeamCount - 1}");
        var given = Axes.Where(a => o.TryGetProperty(a, out _)).ToList();
        if (given.Count is > 0 and < 3)
            throw new LayoutException(
                path,
                $"gives {string.Join(", ", given)} but not {string.Join(", ", Axes.Except(given))}; give all three axes or none"
            );
        // Zero or collapsed axes reach the plan unchanged; it reports them as a degenerate orientation.
        var axes =
            given.Count == 3
                ? Axes.Select(a => Vector(o.GetProperty(a), $"{path}.{a}")).ToArray()
                : [Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ];

        return new MapObject
        {
            Class = name,
            Handle = (uint)index,
            Team = t,
            Label = o.TryGetProperty("label", out var label) ? Text(label, path + ".label") : name,
            Id = (uint)index,
            Position = Vector(Required(o, path, "position"), path + ".position"),
            Right = axes[0],
            Up = axes[1],
            Front = axes[2],
            DisplayName = "",
            Race = "",
            Prefix = [],
            Tail = [],
            Offset = 0,
        };
    }

    // An object whose keys are all in allowed, each once; path names it in a problem.
    static void Fields(JsonElement e, string path, params string[] allowed)
    {
        if (e.ValueKind != JsonValueKind.Object)
            throw new LayoutException(Where(path), $"must be an object, not {Kind(e)}");
        var seen = new HashSet<string>();
        foreach (var p in e.EnumerateObject())
        {
            if (!allowed.Contains(p.Name))
                throw new LayoutException(Join(path, p.Name), $"unknown key; keys here: {string.Join(", ", allowed)}");
            if (!seen.Add(p.Name))
                throw new LayoutException(Join(path, p.Name), "duplicate key");
        }
    }

    static JsonElement Required(JsonElement e, string path, string key) =>
        e.TryGetProperty(key, out var value)
            ? value
            : throw new LayoutException(Where(path), $"missing required key '{key}'");

    static (float Min, float Max) Range(JsonElement e, string path, string minKey, string maxKey)
    {
        var min = Number(Required(e, path, minKey), Join(path, minKey));
        var max = Number(Required(e, path, maxKey), Join(path, maxKey));
        if (min >= max)
            throw new LayoutException(Join(path, minKey), $"{min} is not below {maxKey} {max}");

        return (min, max);
    }

    static Vector3 Vector(JsonElement e, string path)
    {
        if (e.ValueKind != JsonValueKind.Array || e.GetArrayLength() != 3)
            throw new LayoutException(path, $"must be an array of 3 numbers [x, y, z], not {Kind(e)}");

        return new Vector3(Number(e[0], path + "[0]"), Number(e[1], path + "[1]"), Number(e[2], path + "[2]"));
    }

    static string Text(JsonElement e, string path) =>
        e.ValueKind == JsonValueKind.String
            ? e.GetString()!
            : throw new LayoutException(path, $"must be a string, not {Kind(e)}");

    static int WholeNumber(JsonElement e, string path) =>
        e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out var value)
            ? value
            : throw new LayoutException(path, $"must be a whole number, not {Kind(e)}");

    static float Number(JsonElement e, string path)
    {
        if (e.ValueKind != JsonValueKind.Number)
            throw new LayoutException(path, $"must be a number, not {Kind(e)}");
        if (!e.TryGetSingle(out var value) || !float.IsFinite(value))
            throw new LayoutException(path, $"{e.GetRawText()} is not a finite number");

        return value;
    }

    static string Kind(JsonElement e) =>
        e.ValueKind switch
        {
            JsonValueKind.Array => $"an array of {e.GetArrayLength()}",
            JsonValueKind.Null => "null",
            _ => e.GetRawText(),
        };

    static string Join(string path, string key) => path.Length == 0 ? key : $"{path}.{key}";

    static string Where(string path) => path.Length == 0 ? "the root" : path;

    // System.Text.Json appends a zero-based position ("LineNumber: 2 | BytePositionInLine: 4."); the problem
    // names the 1-based line instead.
    static string Reason(JsonException e)
    {
        var at = e.Message.IndexOf(" LineNumber:", StringComparison.Ordinal);

        return (at < 0 ? e.Message : e.Message[..at]).TrimEnd();
    }

    static LayoutRead Fail(string fileName, string problem) =>
        new() { Asset = null, Problem = $"layout '{fileName}': {problem}" };

    sealed class LayoutException(string path, string message) : Exception(message)
    {
        public string Path { get; } = path;
    }
}
