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

    public static IReadOnlyDictionary<string, KindPreview> Discover() =>
        typeof(KindPreview).Assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(KindPreview)) && !t.IsAbstract && t.GetConstructor(Type.EmptyTypes) != null)
            .Select(t => (KindPreview)Activator.CreateInstance(t)!)
            .ToDictionary(p => p.KindName);
}
