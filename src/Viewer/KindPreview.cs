using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using OpenArmada.Assets;

namespace OpenArmada.Viewer;

/// <summary>
/// The asset browser preview of one asset kind. A preview lives in its own file as a subclass with a
/// parameterless constructor and is picked up by Discover, so adding a preview edits no shared list.
/// It lives here, not on AssetKind, because AssetKind is Godot-free.
/// </summary>
public abstract class KindPreview
{
    /// <summary>Name of the AssetKind this previews.</summary>
    public abstract string KindName { get; }

    /// <summary>
    /// Builds the preview of one asset: its Control as the view, and the notes the view shows, which the browser
    /// logs (INV-0010); the preview never logs them. Throws when the asset fails to load; the browser shows the
    /// asset's name and the reason in its place.
    /// </summary>
    public abstract Preview Create(GameFileSystem files, AssetKind kind, string name);

    /// <summary>A non-editable text box that fills the preview area; <c>wrap</c> breaks long lines at the box's edge.</summary>
    protected static TextEdit ReadOnlyText(string text, bool wrap = false) =>
        new()
        {
            Text = text,
            Editable = false,
            WrapMode = wrap ? TextEdit.LineWrappingMode.Boundary : TextEdit.LineWrappingMode.None,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };

    /// <summary>The notes of an asset as a block ahead of its text; empty when there are none.</summary>
    protected static string NotesBlock(IReadOnlyList<string> notes) =>
        notes.Count == 0 ? "" : "NOTES\n" + string.Join("\n", notes) + "\n\n";

    /// <summary>
    /// The previews by kind name, matched against <c>kindNames</c> (the registered kinds, AssetKind.Discover,
    /// which exist with or without an install). Broken registrations are skipped and described in
    /// <c>problems</c>; see PreviewMatch.
    /// </summary>
    public static IReadOnlyDictionary<string, KindPreview> Discover(
        IEnumerable<string> kindNames,
        out IReadOnlyList<string> problems
    )
    {
        var candidates = typeof(KindPreview)
            .Assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(KindPreview)) && !t.IsAbstract)
            .Select(t =>
                (
                    t.Name,
                    (Func<(string, KindPreview)>)(
                        () =>
                        {
                            var p = PreviewMatch.Create<KindPreview>(t);

                            return (p.KindName, p);
                        }
                    )
                )
            );

        return PreviewMatch.Match(candidates, kindNames, out problems);
    }
}
