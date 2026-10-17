using System;
using System.Collections.Generic;
using System.Linq;
using OpenArmada.Diagnostics;
using OpenArmada.Formats;

namespace OpenArmada.Assets;

/// <summary>The asset check: loads every asset of every kind and reports each failure with name and reason.</summary>
public static class AssetCheck
{
    /// <summary>
    /// Writes the whole report through <see cref="Log"/> in area <see cref="Area.Check"/> and returns the
    /// process exit code: 1 exactly when it wrote an <c>ERROR</c> entry, 0 otherwise (counted here, never
    /// read back from <see cref="Log"/>, so a failing sink changes nothing). Unsupported assets are
    /// listed in their own category and do not fail the check. A reader that throws fails only
    /// its own asset (INV-0002); nothing here lets an exception escape. <c>decode</c>, when given, runs
    /// the engine's own decoder on each asset the readers accepted and throws when it rejects the
    /// asset (ADR 0002); the Godot-free tests pass none.
    /// </summary>
    public static int Run(
        string? game,
        IEnumerable<AssetKind> kinds,
        Action<AssetKind, GameFileSystem, string>? decode = null
    )
    {
        if (string.IsNullOrEmpty(game))
        {
            Log.Error(Area.Check, "no install given: pass --game <Armada install dir> or set ARMADA_DIR");

            return 1;
        }

        GameFileSystem files;
        try
        {
            files = new GameFileSystem(game);
        }
        catch (Exception e)
        {
            Log.Error(Area.Check, e.Message);

            return 1;
        }

        int loadedTotal = 0,
            unsupportedTotal = 0,
            failedTotal = 0,
            unresolved = 0;
        foreach (var missing in files.Missing)
        {
            Log.Warn(Area.Check, $"MISSING {missing}");
        }

        foreach (var fault in files.Faults)
        {
            Log.Error(Area.Check, $"FAIL {fault}");
            failedTotal++;
        }

        foreach (var kind in kinds)
        {
            int loaded = 0,
                unsupported = 0,
                failed = 0;
            List<string> names;
            try
            {
                names = kind.Enumerate(files).ToList();
            }
            catch (Exception e)
            {
                Log.Error(Area.Check, $"FAIL {kind.Name}: cannot list assets: {e.Message}");
                failedTotal++;

                continue;
            }

            foreach (var name in names)
            {
                try
                {
                    var notes = kind.Check(files, name);
                    // Notes print before the decoder runs, so a decode failure does not lose them.
                    foreach (var note in notes)
                    {
                        Log.Warn(Area.Check, $"NOTE {kind.Name}/{name}: {note}");
                    }

                    // The game's own decoders run after the readers accepted the file.
                    decode?.Invoke(kind, files, name);
                    // Unresolved references list apart from failures and do not fail the asset.
                    foreach (var missing in kind.Unresolved(files, name))
                    {
                        Log.Warn(Area.Check, $"UNRESOLVED {kind.Name}/{name}: {missing}");
                        unresolved++;
                    }

                    loaded++;
                }
                catch (UnsupportedFormatException e)
                {
                    Log.Warn(Area.Check, $"UNSUPPORTED {kind.Name}/{name}: {e.Message}");
                    unsupported++;
                }
                catch (Exception e)
                {
                    Log.Error(Area.Check, $"FAIL {kind.Name}/{name}: {e.Message}");
                    failed++;
                }
            }

            Log.Info(
                Area.Check,
                names.Count == 0
                    ? $"{kind.Name}: empty (no assets in the install)"
                    : $"{kind.Name}: {loaded} loaded, {unsupported} unsupported, {failed} failed"
            );
            loadedTotal += loaded;
            unsupportedTotal += unsupported;
            failedTotal += failed;
        }

        Log.Info(Area.Check, $"{loadedTotal} loaded, {unsupportedTotal} unsupported, {failedTotal} failed");
        Log.Info(Area.Check, $"{unresolved} unresolved references");
        failedTotal += Classify(files);

        return failedTotal == 0 ? 0 : 1;
    }

    // Classifies every file of the install (InstallClassifier), prints the counts, each known but not
    // loaded file, the out of scope groups and each unclassified file. Returns the number unclassified,
    // each of which fails the check; an install it cannot list (an unreadable folder) fails it once, with
    // the reason, which names the folder.
    static int Classify(GameFileSystem files)
    {
        IReadOnlyList<Classified> classified;
        try
        {
            classified = InstallClassifier.Classify(files.Inventory(), files);
        }
        catch (Exception e)
        {
            Log.Error(Area.Check, $"FAIL files: cannot classify the install: {e.Message}");

            return 1;
        }

        foreach (var c in classified.Where(c => c.Class == FileClass.KnownNotLoaded))
        {
            Log.Info(Area.Check, $"NOT LOADED {c.Entry}: {c.Group}");
        }

        foreach (var g in classified.Where(c => c.Class == FileClass.OutOfScope).GroupBy(c => c.Group))
        {
            Log.Info(Area.Check, $"OUT OF SCOPE {g.Key}: {g.Count()} files");
        }

        var unclassified = classified.Where(c => c.Class == FileClass.Unclassified).ToList();
        foreach (var c in unclassified)
        {
            Log.Error(Area.Check, c.Group == "" ? $"UNCLASSIFIED {c.Entry}" : $"UNCLASSIFIED {c.Entry}: {c.Group}");
        }

        int N(FileClass k) => classified.Count(c => c.Class == k);
        Log.Info(
            Area.Check,
            $"files: {N(FileClass.Loaded)} loaded, {N(FileClass.OutOfScope)} out of scope, {N(FileClass.KnownNotLoaded)} known but not loaded, {unclassified.Count} unclassified"
        );

        return unclassified.Count;
    }
}
