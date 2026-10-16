using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace OpenArmada.Assets;

// How one asset's reference to a texture or a sprite resolves, shared by the kinds that hold such
// references (AssetKind.Unresolved).
internal static class AssetReferences
{
    // A texture by its bare name (a ".tga" suffix is allowed): full color before 8-bit, as Find does.
    internal static bool HasTexture(GameFileSystem files, string name) =>
        files.Find(name.EndsWith(".tga", StringComparison.OrdinalIgnoreCase) ? name : name + ".tga") != null;

    // The sets below are built once per install, on first use.
    static readonly ConditionalWeakTable<GameFileSystem, HashSet<string>> Sprites = new();
    static readonly ConditionalWeakTable<GameFileSystem, HashSet<string>> Sounds = new();
    static readonly ConditionalWeakTable<GameFileSystem, HashSet<string>> Definitions = new();

    // The names of the sprites of every table of the install.
    internal static HashSet<string> SpriteNames(GameFileSystem files) =>
        Sprites.GetValue(files, f => new SpriteTableKind().Tables(f).SelectMany(t => t.Sprites.Select(s => s.Name)).ToHashSet(StringComparer.OrdinalIgnoreCase));

    // The sounds found in sounds/**: each file name, and each path relative to sounds/ with forward slashes.
    internal static HashSet<string> SoundNames(GameFileSystem files) =>
        Sounds.GetValue(files, f =>
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in new SoundKind().Enumerate(f))
            {
                set.Add(System.IO.Path.GetFileName(path));
                set.Add(path.StartsWith("sounds/", StringComparison.OrdinalIgnoreCase) ? path[7..] : path);
            }
            return set;
        });

    // True when a sound value (a bare name, or a path with a folder, either separator) is in sounds/**.
    internal static bool HasSound(GameFileSystem files, string value) => SoundNames(files).Contains(value.Replace('\\', '/'));

    // The definitions the install enumerates (DefinitionKind.Named).
    internal static HashSet<string> DefinitionNames(GameFileSystem files) =>
        Definitions.GetValue(files, f => DefinitionKind.Named(f, ".odf").ToHashSet(StringComparer.OrdinalIgnoreCase));

    // The names in `names` that `resolves` rejects, each once, in first-seen order.
    internal static IReadOnlyList<string> Missing(IEnumerable<string> names, Func<string, bool> resolves) =>
        names.Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Where(n => !resolves(n)).ToList();
}
