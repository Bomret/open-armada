using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using OpenArmada.Formats;

namespace OpenArmada.Assets;

// Sprite tables: the .spr files of the Sprites folder. Names carry the folder ("Sprites/gui_map.spr");
// an @include names a table of the same folder. Load returns a SpriteTable.
public sealed class SpriteTableKind : AssetKind
{
    public const string KindName = "sprites";
    const string Folder = "Sprites/";

    public override string Name => KindName;

    public override IEnumerable<string> Enumerate(GameFileSystem files) => files.List(".spr", "Sprites").Select(n => Folder + n);

    public override object Load(GameFileSystem files, string name) => Read(files, Path.GetFileName(name), Tables(files, name));

    static SpriteTable Read(GameFileSystem files, string bare, IEnumerable<SpriteTable> ambient) =>
        SpriteTable.Read(bare, n => files.Find(Folder + n) is null ? null : System.Text.Encoding.Latin1.GetString(files.ReadAllBytes(Folder + n)), ambient);

    // Tables read for their animations, per install (null: it failed to load). Not thread-safe: the
    // engine loads on one thread.
    static readonly ConditionalWeakTable<GameFileSystem, Dictionary<string, SpriteTable?>> Parsed = new();

    // The tables of the install except `except`, for the animations they define; a table that fails to
    // load adds none.
    internal IEnumerable<SpriteTable> Tables(GameFileSystem files, string? except = null)
    {
        var cache = Parsed.GetOrCreateValue(files);
        foreach (var other in Enumerate(files).Where(n => except == null || !n.Equals(except, System.StringComparison.OrdinalIgnoreCase)))
        {
            if (!cache.TryGetValue(other, out var t))
            {
                try { t = Read(files, Path.GetFileName(other), System.Array.Empty<SpriteTable>()); }
                catch (InvalidDataException) { }
                cache[other] = t;
            }
            if (t != null) yield return t;
        }
    }

    // The textures the table's sprites name that the install lacks.
    public override IReadOnlyList<string> Unresolved(GameFileSystem files, string name) =>
        AssetReferences.Missing(((SpriteTable)Load(files, name)).Sprites.Select(s => s.Texture), n => AssetReferences.HasTexture(files, n));

    // One note per sprite whose @tmaterial is no render type the engine draws, then the table's own notes.
    // A missing texture is left out; Unresolved lists it.
    public override IReadOnlyList<string> Check(GameFileSystem files, string name)
    {
        var table = (SpriteTable)Load(files, name);
        return table.Sprites.Where(s => s.Material != null && !ModelRules.IsRenderType(s.Material))
            .Select(s => $"{s.Table}:{s.Line}: sprite '{s.Name}': texture material '{s.Material}' is not a render type the engine knows")
            .Concat(table.Notes).ToList();
    }
}
