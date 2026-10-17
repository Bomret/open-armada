using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

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

    /// <summary>WARN: the sprite table <c>table</c> shows texture <c>texture</c> as not found or unreadable.</summary>
    public static void TextureNotLoaded(string table, string texture, Exception e) =>
        Log.Warn(Area.Browser, $"{table}: texture {Log.Quote(texture)} not loaded: {e.Message}");

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

    /// <summary>WARN in area render: the model builder cannot use texture <c>name</c> and draws untextured.</summary>
    public static void TextureUnavailable(string name, string? reason) =>
        Log.Warn(Area.Render, $"texture {Log.Quote(name)} unavailable ({reason}), rendering untextured");

    /// <summary>
    /// WARN in area render, one entry per texture in <c>misses</c> (name to reason) that is not in
    /// <c>problems</c> and not yet in <c>logged</c>: a texture that became a Problems entry is logged by the
    /// preview, not twice. Each texture it logs is added to <c>logged</c>, which the builder keeps for its
    /// lifetime, so a texture is logged once per builder however many models it builds.
    /// </summary>
    public static void TexturesUnavailable(
        IReadOnlyDictionary<string, string?> misses,
        IEnumerable<string> problems,
        ISet<string> logged
    )
    {
        foreach (var (name, reason) in misses)
        {
            if (!problems.Contains(name, StringComparer.OrdinalIgnoreCase) && logged.Add(name))
            {
                TextureUnavailable(name, reason);
            }
        }
    }

    /// <summary>
    /// Why a screenshot was not saved, or null when it was: a viewport with no image (a headless run has no
    /// renderer) fails before any save, else <c>saveError</c> is the save's own error.
    /// </summary>
    public static string? CaptureFailure(bool captured, string? saveError) =>
        captured ? saveError : "no viewport image to capture (a headless run has no renderer)";

    /// <summary>
    /// A screenshot run's result: INFO with the absolute path when <c>failure</c> is null, else ERROR with the
    /// path and the reason.
    /// </summary>
    public static void Screenshot(string path, string? failure)
    {
        var full = Path.GetFullPath(path);
        if (failure == null)
        {
            Log.Info(Area.Browser, $"screenshot saved: {Log.Quote(full)}");
        }
        else
        {
            Log.Error(Area.Browser, $"screenshot not saved: {Log.Quote(full)}: {failure}");
        }
    }
}
