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

    // The previews by kind name. A broken registration (a preview that cannot be constructed, a
    // second preview for one kind, a preview naming no kind in `kindNames`) is skipped and described
    // in `problems`, so the browser still opens and shows it (INV-0002).
    public static IReadOnlyDictionary<string, KindPreview> Discover(IEnumerable<string> kindNames, out IReadOnlyList<string> problems)
    {
        var found = new Dictionary<string, KindPreview>();
        var list = new List<string>();
        var known = kindNames.ToHashSet();
        var types = typeof(KindPreview).Assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(KindPreview)) && !t.IsAbstract && t.GetConstructor(Type.EmptyTypes) != null);
        foreach (var t in types)
        {
            KindPreview p;
            try { p = (KindPreview)Activator.CreateInstance(t)!; }
            catch (Exception e) { list.Add($"preview {t.Name} cannot be created: {(e.InnerException ?? e).Message}"); continue; }
            if (!known.Contains(p.KindName)) list.Add($"preview {t.Name} names kind \"{p.KindName}\", which no asset kind has");
            else if (!found.TryAdd(p.KindName, p)) list.Add($"preview {t.Name} is a second preview for kind \"{p.KindName}\"");
        }
        problems = list;
        return found;
    }
}
