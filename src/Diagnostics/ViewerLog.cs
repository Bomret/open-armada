using System;
using System.Collections.Generic;
using System.IO;

namespace OpenArmada.Diagnostics;

/// <summary>
/// The entries the asset browser writes, with their level, area, and text. Godot-free so the decisions are
/// tested; the Viewer calls these where it shows the same thing on screen.
/// </summary>
public static class ViewerLog
{
    /// <summary>INFO: a category was selected.</summary>
    public static void CategorySelected(string category) => Log.Info(Area.Browser, $"category selected: {category}");

    /// <summary>INFO: an asset was selected, naming its category.</summary>
    public static void AssetSelected(string category, string asset) =>
        Log.Info(Area.Browser, $"asset selected: {asset} (category {category})");

    /// <summary>
    /// ERROR: the browser cannot open or a category cannot list its assets; <c>text</c> is what the pane shows
    /// (<c>AssetBrowser.Problem</c>, <c>Notice</c>, or <c>FailureMessage</c> of a failed preview). Nothing is written when it is null.
    /// </summary>
    public static void Failed(string? text)
    {
        if (text != null)
        {
            Log.Error(Area.Browser, text);
        }
    }

    /// <summary>WARN, one entry per problem: a start problem of the preview registry.</summary>
    public static void StartProblems(IEnumerable<string> problems)
    {
        foreach (var problem in problems)
        {
            Log.Warn(Area.Browser, problem);
        }
    }

    /// <summary>WARN: no preview exists for <c>kind</c>.</summary>
    public static void NoPreview(string kind) => Log.Warn(Area.Browser, $"no preview for {kind}");

    /// <summary>WARN, one entry per note: the notes a preview of <c>asset</c> shows.</summary>
    public static void Notes(string asset, IEnumerable<string> notes)
    {
        foreach (var note in notes)
        {
            Log.Warn(Area.Browser, $"{asset}: {note}");
        }
    }

    // The textures logged this run, by name: one texture fails for one cause, so the first view that loads it
    // (a model builder, the combat view, a browser preview) logs it as WARN or ERROR; a later load names only
    // its asset, as INFO, once per asset and texture (UsedTextures), and a later load with no asset stays silent.
    // simplicity: plain sets, as the views load textures on Godot's main thread only.
    static readonly HashSet<string> LoggedTextures = new(StringComparer.OrdinalIgnoreCase);
    static readonly HashSet<string> UsedTextures = new(StringComparer.OrdinalIgnoreCase); // "asset\ntexture"

    // The sprite tables logged this run, and the configurations that used one, by the same rule as the textures.
    static readonly HashSet<string> LoggedTables = new(StringComparer.OrdinalIgnoreCase);
    static readonly HashSet<string> UsedTables = new(StringComparer.OrdinalIgnoreCase); // "configuration\ntable"

    /// <summary>Forgets the textures and sprite tables logged so far, so a test starts a fresh run.</summary>
    internal static void ResetRun()
    {
        LoggedTextures.Clear();
        UsedTextures.Clear();
        LoggedTables.Clear();
        UsedTables.Clear();
    }

    /// <summary>
    /// The sprite table <c>table</c> that interface configuration <c>configuration</c> needs did not load: one
    /// ERROR per run naming the configuration, the table, and the reason, an asset that failed to load (INV-0005).
    /// A later, different configuration that needs it is one INFO that points back to the first entry; the same
    /// configuration again writes nothing.
    /// </summary>
    public static void SpriteTableFailed(string table, Exception e, string configuration)
    {
        if (!UsedTables.Add($"{configuration}\n{table}"))
            return;
        if (!LoggedTables.Add(table))
        {
            Log.Info(
                Area.Browser,
                $"{configuration}: sprite table {Log.Quote(table)} did not load (see its first entry above)"
            );

            return;
        }

        Log.Error(Area.Browser, $"{configuration}: sprite table {Log.Quote(table)} cannot be read: {e.Message}");
    }

    /// <summary>
    /// The texture <c>name</c> did not load, once per run in <c>area</c>, the area of the view that loaded it
    /// first: WARN when the install lacks it (<see cref="Log.IsAbsent"/>, absent data), else ERROR, an
    /// asset that failed to load (INV-0005); both name the texture and the reason (INV-0002). <c>asset</c>,
    /// when known, is the asset that uses the texture: the first entry names it, and a later load of the same texture by another asset is
    /// one INFO per asset and texture that points back to the first entry, so a search for that asset finds why.
    /// </summary>
    public static void TextureFailed(Area area, string name, Exception e, string? asset = null)
    {
        if (asset != null && !UsedTextures.Add($"{asset}\n{name}"))
            return;
        if (!LoggedTextures.Add(name))
        {
            if (asset != null)
            {
                Log.Info(area, $"{asset}: texture {Log.Quote(name)} did not load (see its first entry above)");
            }

            return;
        }
        var user = asset == null ? "" : $"{asset}: ";
        if (Log.IsAbsent(e))
        {
            Log.Warn(area, $"{user}texture {Log.Quote(name)} missing: {e.Message}");
        }
        else
        {
            Log.Error(area, $"{user}texture {Log.Quote(name)} cannot be read: {e.Message}");
        }
    }

    /// <summary>
    /// Why a screenshot was not saved, or null when it was: a viewport with no image (a headless run has no
    /// renderer) fails before any save, else <c>saveError</c> is the save's own error.
    /// </summary>
    public static string? CaptureFailure(bool captured, string? saveError) =>
        captured ? saveError : "no viewport image to capture (a headless run has no renderer)";

    /// <summary>
    /// A screenshot run's result in <c>area</c>, the mode that took it: INFO with the absolute path when
    /// <c>failure</c> is null, else ERROR with the path and the reason. The caller globalizes a Godot path
    /// (user://, res://) first.
    /// </summary>
    public static void Screenshot(Area area, string path, string? failure)
    {
        var full = Path.GetFullPath(path);
        if (failure == null)
        {
            Log.Info(area, $"screenshot saved: {Log.Quote(full)}");
        }
        else
        {
            Log.Error(area, $"screenshot not saved: {Log.Quote(full)}: {failure}");
        }
    }
}
