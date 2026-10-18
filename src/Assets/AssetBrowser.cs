using System;
using System.Collections.Generic;
using System.Linq;
using OpenArmada.Diagnostics;

namespace OpenArmada.Assets;

/// <summary>
/// The asset browser's state and selection flow: the categories (one per asset kind), the assets of the selected
/// kind, the messages the browser shows, and the log entries of what it shows. Godot-free; the Viewer draws it.
/// </summary>
public sealed class AssetBrowser
{
    readonly IReadOnlyDictionary<string, CreatePreview> _previews;

    AssetBrowser(
        string? problem,
        GameFileSystem? files,
        IReadOnlyList<AssetKind> kinds,
        IReadOnlyDictionary<string, CreatePreview> previews
    )
    {
        Problem = problem;
        Files = files;
        Kinds = kinds;
        _previews = previews;
        if (files != null && kinds.Count > 0)
        {
            SelectKind(0);
        }
    }

    /// <summary>
    /// Builds the preview of asset <c>name</c> of <c>kind</c> from <c>files</c>, and returns it with the asset's notes
    /// without logging them. Throws when the asset fails to load.
    /// </summary>
    public delegate Preview CreatePreview(GameFileSystem files, AssetKind kind, string name);

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
    /// Opens the install at <c>game</c> with the kinds sorted by name, and selects the first kind without logging
    /// it. <c>previews</c> builds the preview of each kind by name; a kind it lacks has no preview, and so does every
    /// kind when it is null. Does not throw: when <c>game</c> is null or empty, or the install cannot be opened, the
    /// browser has no files and no kinds, and <see cref="Problem"/> holds the reason.
    /// </summary>
    public static AssetBrowser Open(
        string? game,
        IEnumerable<AssetKind> kinds,
        IReadOnlyDictionary<string, CreatePreview>? previews = null
    )
    {
        previews ??= new Dictionary<string, CreatePreview>();
        if (string.IsNullOrEmpty(game))
            return new AssetBrowser(
                "no install given: pass --game <Armada install dir> or set ARMADA_DIR",
                null,
                Array.Empty<AssetKind>(),
                previews
            );
        try
        {
            return new AssetBrowser(
                null,
                new GameFileSystem(game),
                kinds.OrderBy(k => k.Name, StringComparer.Ordinal).ToList(),
                previews
            );
        }
        catch (Exception e) // no install, or the root cannot be listed (permissions, I/O)
        {
            return new AssetBrowser(e.Message, null, Array.Empty<AssetKind>(), previews);
        }
    }

    /// <summary>
    /// Shows what the browser opens on: with <see cref="Problem"/> set, one ERROR entry and the problem as the pane
    /// text, and nothing else. Otherwise the selected kind and its first asset, or, with <c>model</c> (the --model
    /// entrance, see <see cref="SelectModel"/>), the models kind and that model; only the kind and asset shown are
    /// logged. An install with no kinds shows nothing.
    /// </summary>
    public Pane Start(string? model)
    {
        if (Problem != null)
        {
            ViewerLog.Failed(Problem);

            return new Pane { Text = Problem };
        }

        if (Kind == null)
            return new Pane { Text = "" };
        var wanted = model == null ? -1 : SelectModel(model);

        return Show(Kinds.ToList().IndexOf(Kind), wanted);
    }

    /// <summary>
    /// Shows <c>Kinds[index]</c>: selects it (see <see cref="SelectKind"/>) and writes one INFO entry naming it, then
    /// shows its first asset (see <see cref="ShowAsset"/>). A kind whose listing failed writes its
    /// <see cref="Notice"/> as one ERROR entry and the pane shows it; a kind with no assets writes nothing more and
    /// the pane shows <c>no &lt;kind&gt; in this install</c>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">when <c>index</c> is outside <see cref="Kinds"/>.</exception>
    public Pane ShowKind(int index) => Show(index, -1);

    /// <summary>
    /// Shows <c>Assets[index]</c> of the selected kind: one INFO entry naming the asset and its kind, then its
    /// preview and each note it returns as one WARN entry <c>&lt;asset&gt;: &lt;note&gt;</c>, on every showing (INV-0010).
    /// A kind with no preview writes <c>no preview for &lt;kind&gt;</c> as one WARN entry and the pane shows
    /// it. A preview that throws writes one ERROR entry with <see cref="FailureMessage"/>, and the pane shows the same
    /// text (INV-0002).
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">when <c>index</c> is outside <see cref="Assets"/>.</exception>
    public Pane ShowAsset(int index)
    {
        var kind = Kind!;
        var name = Assets[index];

        ViewerLog.AssetSelected(kind.Name, name);

        if (!_previews.TryGetValue(kind.Name, out var create))
        {
            ViewerLog.NoPreview(kind.Name);

            return new Pane { Text = $"no preview for {kind.Name}", Asset = index };
        }

        Preview preview;
        try
        {
            preview = create(Files!, kind, name);
        }
        catch (Exception e) // a failed asset never stops the browser (INV-0002)
        {
            var message = FailureMessage(name, e);

            ViewerLog.Failed(message);

            return new Pane { Text = message, Asset = index };
        }

        ViewerLog.Notes(name, preview.Notes);

        return new Pane
        {
            Text = "",
            Preview = preview.View,
            Asset = index,
        };
    }

    // Shows Kinds[index] and its asset at `wanted`, or else its first.
    Pane Show(int index, int wanted)
    {
        SelectKind(index);

        ViewerLog.CategorySelected(Kind!.Name);
        ViewerLog.Failed(Notice);

        if (Assets.Count == 0)
            return new Pane { Text = Notice ?? $"no {Kind.Name} in this install" };

        return ShowAsset(Math.Max(0, wanted));
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

/// <summary>What the asset browser's preview pane shows after a selection.</summary>
public sealed record Pane
{
    /// <summary>The message the pane shows; empty when it shows <see cref="Preview"/>.</summary>
    public required string Text { get; init; }

    /// <summary>The view the preview built, opaque to the browser; null when the pane shows <see cref="Text"/>.</summary>
    public object? Preview { get; init; }

    /// <summary>The index in <see cref="AssetBrowser.Assets"/> of the asset shown; -1 when none is.</summary>
    public int Asset { get; init; } = -1;
}

/// <summary>The preview of one asset: the view a preview built and the asset's notes, which it shows and never logs.</summary>
public sealed record Preview
{
    /// <summary>The view the preview built, opaque to the browser.</summary>
    public required object View { get; init; }

    /// <summary>The notes of the asset, the same list the view shows; empty when there are none.</summary>
    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();
}
