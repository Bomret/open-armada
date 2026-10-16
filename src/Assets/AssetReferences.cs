using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenArmada.Assets;

// How one asset's reference to a texture or a sprite resolves, shared by the kinds that hold such
// references (AssetKind.Unresolved).
internal static class AssetReferences
{
    // A texture by its bare name (a ".tga" suffix is allowed): full color before 8-bit, as Find does.
    internal static bool HasTexture(GameFileSystem files, string name) =>
        files.Find(name.EndsWith(".tga", StringComparison.OrdinalIgnoreCase) ? name : name + ".tga") != null;

    // The names of the sprites of every table of the install.
    internal static HashSet<string> SpriteNames(GameFileSystem files) =>
        new SpriteTableKind().Tables(files).SelectMany(t => t.Sprites.Select(s => s.Name)).ToHashSet(StringComparer.OrdinalIgnoreCase);

    // The names in `names` that `resolves` rejects, each once, in first-seen order.
    internal static IReadOnlyList<string> Missing(IEnumerable<string> names, Func<string, bool> resolves) =>
        names.Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Where(n => !resolves(n)).ToList();
}
