using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenArmada.Formats;

namespace OpenArmada.Assets;

// The asset check: loads every asset of every kind and reports each failure with name and reason.
public static class AssetCheck
{
    // Returns the process exit code: 0 when no asset failed, 1 otherwise. Unsupported assets are
    // listed in their own category and do not fail the check. A reader that throws fails only
    // its own asset (INV-0002); nothing here lets an exception escape. `decode`, when given, runs
    // the engine's own decoder on each asset the readers accepted and throws when it rejects the
    // asset (ADR 0002); the Godot-free tests pass none.
    public static int Run(string? game, IEnumerable<AssetKind> kinds, TextWriter output,
        Action<AssetKind, GameFileSystem, string>? decode = null)
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

        int loadedTotal = 0, unsupportedTotal = 0, failedTotal = 0, unresolved = 0;
        foreach (var missing in files.Missing) output.WriteLine($"MISSING {missing}");
        foreach (var fault in files.Faults)
        {
            output.WriteLine($"FAIL {fault}");
            failedTotal++;
        }
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
                    var notes = kind.Check(files, name);
                    // Notes print before the decoder runs, so a decode failure does not lose them.
                    foreach (var note in notes) output.WriteLine($"NOTE {kind.Name}/{name}: {note}");
                    // The game's own decoders run after the readers accepted the file.
                    decode?.Invoke(kind, files, name);
                    // Unresolved references list apart from failures and do not fail the asset.
                    foreach (var missing in kind.Unresolved(files, name))
                    {
                        output.WriteLine($"UNRESOLVED {kind.Name}/{name}: {missing}");
                        unresolved++;
                    }
                    loaded++;
                }
                catch (UnsupportedFormatException e)
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
        output.WriteLine($"{unresolved} unresolved references");
        failedTotal += Classify(files, output);
        return failedTotal == 0 ? 0 : 1;
    }

    // Classifies every file of the install (InstallClassifier), prints the counts, each known but not
    // loaded file, the out of scope groups and each unclassified file. Returns the number unclassified,
    // each of which fails the check; an install it cannot list (an unreadable folder) fails it once, with
    // the reason, which names the folder.
    static int Classify(GameFileSystem files, TextWriter output)
    {
        IReadOnlyList<Classified> classified;
        try { classified = InstallClassifier.Classify(files.Inventory()); }
        catch (Exception e)
        {
            output.WriteLine($"FAIL files: cannot classify the install: {e.Message}");
            return 1;
        }
        foreach (var c in classified.Where(c => c.Class == FileClass.KnownNotLoaded)) output.WriteLine($"NOT LOADED {c.Entry}: {c.Group}");
        foreach (var g in classified.Where(c => c.Class == FileClass.OutOfScope).GroupBy(c => c.Group))
            output.WriteLine($"OUT OF SCOPE {g.Key}: {g.Count()} files");
        var unclassified = classified.Where(c => c.Class == FileClass.Unclassified).ToList();
        foreach (var c in unclassified) output.WriteLine(c.Group == "" ? $"UNCLASSIFIED {c.Entry}" : $"UNCLASSIFIED {c.Entry}: {c.Group}");
        int N(FileClass k) => classified.Count(c => c.Class == k);
        output.WriteLine($"files: {N(FileClass.Loaded)} loaded, {N(FileClass.OutOfScope)} out of scope, {N(FileClass.KnownNotLoaded)} known but not loaded, {unclassified.Count} unclassified");
        return unclassified.Count;
    }
}
