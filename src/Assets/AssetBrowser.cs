using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenArmada.Assets;

// The asset browser's state: the categories (one per asset kind), the assets of the selected kind,
// and the messages the browser shows. Godot-free; the Viewer draws it.
public sealed class AssetBrowser
{
    AssetBrowser(string? problem, GameFileSystem? files, IReadOnlyList<AssetKind> kinds)
    {
        Problem = problem;
        Files = files;
        Kinds = kinds;
        if (files != null && kinds.Count > 0)
            SelectKind(0);
    }

    // Set when there is no install to browse: names what is missing.
    public string? Problem { get; }
    public GameFileSystem? Files { get; }
    public IReadOnlyList<AssetKind> Kinds { get; }
    public AssetKind? Kind { get; private set; }

    // Assets of the selected kind, sorted by name; empty when the kind has none.
    public IReadOnlyList<string> Assets { get; private set; } = Array.Empty<string>();

    // Set when the selected kind's assets could not be listed.
    public string? Notice { get; private set; }

    public static AssetBrowser Open(string? game, IEnumerable<AssetKind> kinds)
    {
        if (string.IsNullOrEmpty(game))
            return new AssetBrowser(
                "no install given: pass --game <Armada install dir> or set ARMADA_DIR",
                null,
                Array.Empty<AssetKind>()
            );
        try
        {
            return new AssetBrowser(
                null,
                new GameFileSystem(game),
                kinds.OrderBy(k => k.Name, StringComparer.Ordinal).ToList()
            );
        }
        catch (Exception e) // no install, or the root cannot be listed (permissions, I/O)
        {
            return new AssetBrowser(e.Message, null, Array.Empty<AssetKind>());
        }
    }

    public void SelectKind(int index)
    {
        Kind = Kinds[index];
        Notice = null;
        try
        {
            Assets = Kind.Enumerate(Files!).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (Exception e) // a kind that cannot list is an empty list with a reason (INV-0002)
        {
            Assets = Array.Empty<string>();
            Notice = $"cannot list {Kind.Name}: {e.Message}";
        }
    }

    // The --model entrance: selects the kind named "models", whatever its position, and returns the
    // index in Assets of the model `name` (with or without ".sod", any case); -1 when the install
    // has no models kind (the selection is left alone) or the model is absent.
    public int SelectModel(string name)
    {
        int kind = -1;
        for (int i = 0; i < Kinds.Count; i++)
            if (Kinds[i].Name == ModelKind.KindName)
                kind = i;
        if (kind < 0)
            return -1;
        SelectKind(kind);
        for (int i = 0; i < Assets.Count; i++)
            if (
                Assets[i].Equals(name, StringComparison.OrdinalIgnoreCase)
                || Assets[i].Equals(name + ".sod", StringComparison.OrdinalIgnoreCase)
            )
                return i;
        return -1;
    }

    // What the preview pane shows in place of a preview that failed to load.
    public static string FailureMessage(string name, Exception e) => $"{name}: {e.Message}";
}
