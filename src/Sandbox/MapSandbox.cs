using System;
using System.Collections.Generic;
using System.Linq;
using OpenArmada.Assets;
using OpenArmada.Diagnostics;

namespace OpenArmada.Sandbox;

/// <summary>
/// The map sandbox of one command line: the install and the plan of the asked map, or the Problem that names
/// why there is none (INV-0002: a missing install, a bad argument, an unknown or unreadable map is a message,
/// never a crash).
/// </summary>
public sealed record MapSandbox
{
    /// <summary>The install's files; null when there is a <see cref="Problem"/>.</summary>
    public required GameFileSystem? Files { get; init; }

    /// <summary>The plan of the asked map; null when there is a <see cref="Problem"/>.</summary>
    public required SandboxPlan? Plan { get; init; }

    /// <summary>Why there is no sandbox; null when <see cref="Files"/> and <see cref="Plan"/> are set.</summary>
    public required string? Problem { get; init; }

    /// <summary><c>map</c> is the value of --map (null when it has none); <c>argProblems</c> are the command line's own problems.</summary>
    public static MapSandbox Open(string? game, string? map, IReadOnlyList<string> argProblems)
    {
        if (argProblems.Count > 0)
        {
            Log.Error(Area.Sandbox, "map sandbox cannot open: the launch arguments have problems");

            return Unopened(string.Join("; ", argProblems));
        }

        var browser = AssetBrowser.Open(game, Array.Empty<AssetKind>());
        if (browser.Problem != null)
            return Failed(browser.Problem);
        var files = browser.Files!;
        if (map == null)
            return Failed("no map given: pass --map <name>");
        var kind = new MapKind();
        var choice = MapName.Resolve(kind.Enumerate(files).ToList(), map);
        if (choice.Name == null)
            return Failed(choice.Problem!);
        try
        {
            return Planned(files, (MapAsset)kind.Load(files, choice.Name));
        }
        catch (Exception e) // a map that cannot be read is a message, whatever the reason (INV-0002)
        {
            return Failed($"map '{choice.Name}' cannot be opened: {e.Message}");
        }
    }

    /// <summary>
    /// <c>layout</c> is the path of --layout (null when it has none); <c>argProblems</c> are the command line's own
    /// problems. The layout is read before the install opens, so its problem shows with or without an install.
    /// </summary>
    public static MapSandbox OpenLayout(string? game, string? layout, IReadOnlyList<string> argProblems)
    {
        if (argProblems.Count > 0)
        {
            Log.Error(Area.Sandbox, "map sandbox cannot open: the launch arguments have problems");

            return Unopened(string.Join("; ", argProblems));
        }

        if (layout == null)
            return Failed("no layout given: pass --layout <file>");
        var read = Layout.Load(layout);
        if (read.Problem != null)
            return Failed(read.Problem);
        var browser = AssetBrowser.Open(game, Array.Empty<AssetKind>());
        if (browser.Problem != null)
            return Failed(browser.Problem);
        try
        {
            return Planned(browser.Files!, read.Asset!);
        }
        catch (Exception e) // a plan that cannot be built is a message, whatever the reason (INV-0002)
        {
            return Failed($"layout '{layout}' cannot be opened: {e.Message}");
        }
    }

    // The sandbox of a map asset, from a map or a layout alike (INV-0009).
    static MapSandbox Planned(GameFileSystem files, MapAsset asset) =>
        new()
        {
            Files = files,
            Plan = SandboxPlan.Build(
                asset,
                SandboxPlan.Definitions(files, asset),
                model => files.Find(model + ".sod") != null
            ),
            Problem = null,
            Name = asset.Name,
        };

    /// <summary>The name of the opened map, or the path of the opened layout; null when there is a <see cref="Problem"/>.</summary>
    public string? Name { get; init; }

    /// <summary>Logs the opened map as one INFO entry with its name and its object and unit counts.</summary>
    public void LogOpened(int units) =>
        Log.Info(Area.Sandbox, $"map opened: {Name}, {Plan!.Objects.Count} objects, {units} units");

    /// <summary>Logs the model that cannot be read as one ERROR entry (an asset failed) and returns its text, the label line.</summary>
    public static string LogUnreadableModel(string model, string reason)
    {
        var text = $"model '{model}' cannot be read: {reason}";
        Log.Error(Area.Sandbox, text);

        return text;
    }

    // The problem that stops the sandbox opening is one ERROR entry. The command line's own problems are logged
    // once as WARN entries of area run at run start, so their text is not repeated here.
    static MapSandbox Failed(string problem)
    {
        Log.Error(Area.Sandbox, problem);

        return Unopened(problem);
    }

    static MapSandbox Unopened(string problem) =>
        new()
        {
            Files = null,
            Plan = null,
            Problem = problem,
        };
}
