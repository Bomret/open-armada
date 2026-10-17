using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenArmada.Assets;

/// <summary>
/// The asset browser's state: the categories (one per asset kind), the assets of the selected kind,
/// and the messages the browser shows. Godot-free; the Viewer draws it.
/// </summary>
public sealed class AssetBrowser
{
    AssetBrowser(string? problem, GameFileSystem? files, IReadOnlyList<AssetKind> kinds)
    {
        Problem = problem;
        Files = files;
        Kinds = kinds;
        if (files != null && kinds.Count > 0)
        {
            SelectKind(0);
        }
    }

    /// <summary>Set when there is no install to browse: names what is missing.</summary>
    public string? Problem { get; }

    /// <summary>The install being browsed; null when <see cref="Problem"/> is set.</summary>
    public GameFileSystem? Files { get; }

    /// <summary>The categories, one per asset kind, sorted by name; empty when <see cref="Problem"/> is set.</summary>
    public IReadOnlyList<AssetKind> Kinds { get; }

    /// <summary>The selected kind; null until a kind is selected.</summary>
    public AssetKind? Kind { get; private set; }

    /// <summary>Assets of the selected kind, sorted by name; empty when the kind has none.</summary>
    public IReadOnlyList<string> Assets { get; private set; } = Array.Empty<string>();

    /// <summary>Set when the selected kind's assets could not be listed.</summary>
    public string? Notice { get; private set; }

    /// <summary>
    /// Opens the install at <c>game</c> with the kinds sorted by name, and selects the first kind. Does not throw:
    /// when <c>game</c> is null or empty, or the install cannot be opened, the browser has no files and no kinds,
    /// and <see cref="Problem"/> holds the reason.
    /// </summary>
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

    /// <summary>
    /// Selects <c>Kinds[index]</c> and lists its assets into <see cref="Assets"/>, clearing <see cref="Notice"/>.
    /// A kind whose listing throws gets an empty list and a <see cref="Notice"/> with the reason.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">when <c>index</c> is outside <see cref="Kinds"/>.</exception>
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

    /// <summary>
    /// The --model entrance: selects the kind named "models", whatever its position, and returns the
    /// index in Assets of the model <c>name</c> (with or without ".sod", any case); -1 when the install
    /// has no models kind (the selection is left alone) or the model is absent.
    /// </summary>
    public int SelectModel(string name)
    {
        int kind = -1;
        for (int i = 0; i < Kinds.Count; i++)
        {
            if (Kinds[i].Name == ModelKind.KindName)
            {
                kind = i;
            }
        }

        if (kind < 0)
            return -1;
        SelectKind(kind);
        for (int i = 0; i < Assets.Count; i++)
        {
            if (
                Assets[i].Equals(name, StringComparison.OrdinalIgnoreCase)
                || Assets[i].Equals(name + ".sod", StringComparison.OrdinalIgnoreCase)
            )
                return i;
        }

        return -1;
    }

    /// <summary>What the preview pane shows in place of a preview that failed to load.</summary>
    public static string FailureMessage(string name, Exception e) => $"{name}: {e.Message}";
}
