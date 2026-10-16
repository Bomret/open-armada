using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using OpenArmada.Assets;

namespace OpenArmada.Viewer;

// The asset browser preview of one asset kind. A preview lives in its own file as a subclass with a
// parameterless constructor and is picked up by Discover, so adding a preview edits no shared list.
// It lives here, not on AssetKind, because AssetKind is Godot-free.
public abstract class KindPreview
{
    // Name of the AssetKind this previews.
    public abstract string KindName { get; }

    // Builds the preview of one asset. Throws when the asset fails to load; the browser shows the
    // asset's name and the reason in its place.
    public abstract Control Create(GameFileSystem files, AssetKind kind, string name);

    // A non-editable text box that fills the preview area; `wrap` breaks long lines at the box's edge.
    protected static TextEdit ReadOnlyText(string text, bool wrap = false) =>
        new()
        {
            Text = text,
            Editable = false,
            WrapMode = wrap ? TextEdit.LineWrappingMode.Boundary : TextEdit.LineWrappingMode.None,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };

    // The notes of an asset as a block ahead of its text; empty when there are none.
    protected static string NotesBlock(IReadOnlyList<string> notes) => notes.Count == 0 ? "" : "NOTES\n" + string.Join("\n", notes) + "\n\n";

    // The previews by kind name, matched against `kindNames` (the registered kinds, AssetKind.Discover,
    // which exist with or without an install). Broken registrations are skipped and described in
    // `problems`; see PreviewMatch.
    public static IReadOnlyDictionary<string, KindPreview> Discover(IEnumerable<string> kindNames, out IReadOnlyList<string> problems)
    {
        var candidates = typeof(KindPreview).Assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(KindPreview)) && !t.IsAbstract)
            .Select(t => (t.Name, (Func<(string, KindPreview)>)(() =>
            {
                if (t.GetConstructor(Type.EmptyTypes) == null) throw new InvalidOperationException("it has no parameterless constructor");
                var p = (KindPreview)Activator.CreateInstance(t)!;
                return (p.KindName, p);
            })));
        return PreviewMatch.Match(candidates, kindNames, out problems);
    }
}
