using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OpenArmada.Assets;

// The asset check: loads every asset of every kind and reports each failure with name and reason.
public static class AssetCheck
{
    // Returns the process exit code: 0 when no asset failed, 1 otherwise. Unsupported assets are
    // listed in their own category and do not fail the check. A reader that throws fails only
    // its own asset (INV-0002); nothing here lets an exception escape.
    public static int Run(string? game, IEnumerable<AssetKind> kinds, TextWriter output)
    {
        if (string.IsNullOrEmpty(game))
        {
            output.WriteLine("no install given: pass --game <Armada install dir> or set ARMADA_DIR");
            return 1;
        }
        GameFileSystem files;
        try { files = new GameFileSystem(game); }
        catch (Exception e)
        {
            output.WriteLine(e.Message);
            return 1;
        }

        int loadedTotal = 0, unsupportedTotal = 0, failedTotal = 0;
        foreach (var kind in kinds)
        {
            int loaded = 0, unsupported = 0, failed = 0;
            List<string> names;
            try { names = kind.Enumerate(files).ToList(); }
            catch (Exception e)
            {
                output.WriteLine($"FAIL {kind.Name}: cannot list assets: {e.Message}");
                failedTotal++;
                continue;
            }
            foreach (var name in names)
            {
                try
                {
                    kind.Check(files, name);
                    loaded++;
                }
                catch (NotSupportedException e)
                {
                    output.WriteLine($"UNSUPPORTED {kind.Name}/{name}: {e.Message}");
                    unsupported++;
                }
                catch (Exception e)
                {
                    output.WriteLine($"FAIL {kind.Name}/{name}: {e.Message}");
                    failed++;
                }
            }
            output.WriteLine(names.Count == 0
                ? $"{kind.Name}: empty (no assets in the install)"
                : $"{kind.Name}: {loaded} loaded, {unsupported} unsupported, {failed} failed");
            loadedTotal += loaded;
            unsupportedTotal += unsupported;
            failedTotal += failed;
        }
        output.WriteLine($"{loadedTotal} loaded, {unsupportedTotal} unsupported, {failedTotal} failed");
        return failedTotal == 0 ? 0 : 1;
    }
}
