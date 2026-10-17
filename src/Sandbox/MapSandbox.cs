using System;
using System.Collections.Generic;
using System.Linq;
using OpenArmada.Assets;

namespace OpenArmada.Sandbox;

// The map sandbox of one command line: the install and the plan of the asked map, or the Problem that names
// why there is none (INV-0002: a missing install, a bad argument, an unknown or unreadable map is a message,
// never a crash).
public sealed record MapSandbox(GameFileSystem? Files, SandboxPlan? Plan, string? Problem)
{
    // `map` is the value of --map (null when it has none); `argProblems` are the command line's own problems.
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

            return new MapSandbox(files, plan, null);
        }
        catch (Exception e) // a map that cannot be read is a message, whatever the reason (INV-0002)
        {
            return Failed($"map '{choice.Name}' cannot be opened: {e.Message}");
        }
    }

    static MapSandbox Failed(string problem) => new(null, null, problem);
}
