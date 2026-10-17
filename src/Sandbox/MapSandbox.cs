using System;
using System.Collections.Generic;
using System.Linq;
using OpenArmada.Assets;

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
            return Failed(string.Join("; ", argProblems));
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
            var asset = (MapAsset)kind.Load(files, choice.Name);
            var plan = SandboxPlan.Build(
                asset,
                SandboxPlan.Definitions(files, asset),
                model => files.Find(model + ".sod") != null
            );

            return new MapSandbox
            {
                Files = files,
                Plan = plan,
                Problem = null,
            };
        }
        catch (Exception e) // a map that cannot be read is a message, whatever the reason (INV-0002)
        {
            return Failed($"map '{choice.Name}' cannot be opened: {e.Message}");
        }
    }

    static MapSandbox Failed(string problem) =>
        new()
        {
            Files = null,
            Plan = null,
            Problem = problem,
        };
}
