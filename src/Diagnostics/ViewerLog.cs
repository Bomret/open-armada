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
    // (a model builder, the combat view, a sprite table preview) logs it and every later load stays silent.
    // simplicity: a plain set, as the views load textures on Godot's main thread only.
    static readonly HashSet<string> LoggedTextures = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Forgets the textures logged so far, so a test starts a fresh run.</summary>
    internal static void ResetTextures() => LoggedTextures.Clear();

    /// <summary>
    /// The texture <c>name</c> did not load, once per run in <c>area</c>, the area of the view that loaded it
    /// first: WARN when the install lacks it (<see cref="FileNotFoundException"/>, absent data), else ERROR, an
    /// asset that failed to load (INV-0005); both name the texture and the reason (INV-0002).
    /// </summary>
    public static void TextureFailed(Area area, string name, Exception e)
    {
        if (!LoggedTextures.Add(name))
            return;
        if (e is FileNotFoundException)
        {
            Log.Warn(area, $"texture {Log.Quote(name)} missing: {e.Message}");
        }
        else
        {
            Log.Error(area, $"texture {Log.Quote(name)} cannot be read: {e.Message}");
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
