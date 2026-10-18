using System;
using System.Linq;
using OpenArmada.Assets;
using OpenArmada.Diagnostics;

namespace OpenArmada.Sandbox;

/// <summary>The <c>--maps</c> run: writes the map summary of every map of an install through <see cref="Log"/>.</summary>
public static class MapSummaryReport
{
    /// <summary>
    /// Writes one entry per map of the install's <c>bzn</c> folder in area <see cref="Area.Maps"/>, in name
    /// order, every line starting with the map's name: <c>INFO</c>, or <c>WARN</c> when a class is unresolved.
    /// A map that cannot be read is one <c>ERROR</c> with its reason, and the run goes on (INV-0002). Returns
    /// the exit code: 1 when no install is given or it cannot be opened (the asset check's text), else 0.
    /// </summary>
    public static int Run(string? game)
    {
        if (string.IsNullOrEmpty(game))
        {
            Log.Error(Area.Maps, AssetCheck.NoInstall);

            return 1;
        }

        GameFileSystem files;
        try
        {
            files = new GameFileSystem(game);
        }
        catch (Exception e)
        {
            Log.Error(Area.Maps, e.Message);

            return 1;
        }

        var kind = new MapKind();
        foreach (var name in kind.Enumerate(files).Order(StringComparer.OrdinalIgnoreCase))
        {
            // "bzn/2blue.bzn" -> "2blue", the name --map takes; a subfolder stays in it
            var map = name[4..^4];
            MapSummary summary;
            try
            {
                summary = MapSummary.Of(files, (MapAsset)kind.Load(files, name));
            }
            catch (Exception e)
            {
                // an asset failed to load: one entry with its name and reason (INV-0002, INV-0005)
                if (Log.IsAbsent(e))
                {
                    Log.Warn(Area.Maps, $"{map}: {e.Message}");
                }
                else
                {
                    Log.Error(Area.Maps, $"{map}: {e.Message}");
                }

                continue;
            }

            var text = string.Join("\n", summary.Lines.Select(l => $"{map}: {l}"));
            if (summary.HasUnresolved)
            {
                Log.Warn(Area.Maps, text);
            }
            else
            {
                Log.Info(Area.Maps, text);
            }
        }

        return 0;
    }
}
