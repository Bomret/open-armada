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

    // A sprite of any table of the install.
    internal static bool HasSprite(GameFileSystem files, string name) =>
        new SpriteTableKind().Tables(files).Any(t => t.Sprites.Any(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase)));

    // The names in `names` that `resolves` rejects, each once, in first-seen order.
    internal static IReadOnlyList<string> Missing(IEnumerable<string> names, Func<string, bool> resolves) =>
        names.Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Where(n => !resolves(n)).ToList();
}
