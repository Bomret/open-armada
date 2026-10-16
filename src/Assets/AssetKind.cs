using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenArmada.Assets;

// One kind of asset (models, textures, ...). A kind lives in its own file as a subclass with a
// parameterless constructor and is picked up by Discover, so adding a kind edits no shared list.
// A kind's asset browser preview is a KindPreview in the Viewer, named after the kind.
public abstract class AssetKind
{
    public abstract string Name { get; }

    // Names of every asset of this kind that the install holds; empty when it holds none.
    public abstract IEnumerable<string> Enumerate(GameFileSystem files);

    // Loads one asset. Throws when it is corrupt; throws UnsupportedFormatException when the asset
    // is known but its format is not supported.
    public abstract object Load(GameFileSystem files, string name);

    // What the asset check runs per asset. Kinds with a stricter check override it. Returns notes the
    // check prints without failing the asset.
    public virtual IReadOnlyList<string> Check(GameFileSystem files, string name)
    {
        Load(files, name);
        return Array.Empty<string>();
    }

    public static IReadOnlyList<AssetKind> Discover() =>
        typeof(AssetKind).Assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(AssetKind)) && !t.IsAbstract && t.GetConstructor(Type.EmptyTypes) != null)
            .Select(t => (AssetKind)Activator.CreateInstance(t)!)
            .OrderBy(k => k.Name, StringComparer.Ordinal)
            .ToList();
}
