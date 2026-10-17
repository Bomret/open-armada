using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using OpenArmada.Formats;

namespace OpenArmada.Assets;

/// <summary>
/// Sprite tables: the .spr files of the Sprites folder. Names carry the folder ("Sprites/gui_map.spr");
/// an @include names a table of the same folder. Load returns a SpriteTable.
/// </summary>
public sealed class SpriteTableKind : AssetKind
{
    /// <summary>The kind's <see cref="Name"/>: <c>sprites</c>.</summary>
    public const string KindName = "sprites";
    const string Folder = "Sprites/";

    // Per install: the table texts, the tables read for their animations, and the tables as Load reads them
    // (null: it failed to load). Not thread-safe: the engine loads on one thread.
    static readonly ConditionalWeakTable<GameFileSystem, Dictionary<string, string?>> Texts = new();
    static readonly ConditionalWeakTable<GameFileSystem, Dictionary<string, SpriteTable?>> Parsed = new();
    static readonly ConditionalWeakTable<GameFileSystem, List<SpriteTable>> All = new();

    /// <inheritdoc/>
    public override string Name => KindName;

    /// <inheritdoc/>
    public override IEnumerable<string> Enumerate(GameFileSystem files) =>
        files.List(".spr", "Sprites").Select(n => Folder + n);

    /// <inheritdoc/>
    public override object Load(GameFileSystem files, string name) =>
        Read(files, Path.GetFileName(name), Tables(files, name));

    static SpriteTable Read(GameFileSystem files, string bare, IEnumerable<SpriteTable> ambient) =>
        SpriteTable.Read(bare, n => Text(files, Folder + n), ambient);

    // The text of each table, read from the install once (null: the install has none).
    static string? Text(GameFileSystem files, string name)
    {
        var cache = Texts.GetOrCreateValue(files);
        if (!cache.TryGetValue(name, out var text))
        {
            cache[name] = text = files.Find(name) is null
                ? null
                : Windows1252.Encoding.GetString(files.ReadAllBytes(name));
        }

        return text;
    }

    /// <summary>
    /// Every table of the install as <see cref="Load"/> reads it, in name order (ignoring case), read once per
    /// install; later calls return the same list, so a table changed on disk afterwards is not read again. A table
    /// that fails to load is left out. Not thread-safe.
    /// </summary>
    public IReadOnlyList<SpriteTable> Loaded(GameFileSystem files) =>
        All.GetValue(
            files,
            f =>
                Enumerate(f)
                    .OrderBy(n => n, System.StringComparer.OrdinalIgnoreCase)
                    .Select(n =>
                    {
                        try
                        {
                            return (SpriteTable)Load(f, n);
                        }
                        catch (System.Exception e)
                            when (e
                                    is IOException
                                        or InvalidDataException
                                        or System.UnauthorizedAccessException
                                        or UnsupportedFormatException
                            )
                        {
                            return null;
                        }
                    })
                    .OfType<SpriteTable>()
                    .ToList()
        );

    // The tables of the install except `except`, for the animations they define; a table that fails to
    // load adds none.
    internal IEnumerable<SpriteTable> Tables(GameFileSystem files, string? except = null)
    {
        var cache = Parsed.GetOrCreateValue(files);
        foreach (
            var other in Enumerate(files)
                .Where(n => except == null || !n.Equals(except, System.StringComparison.OrdinalIgnoreCase))
        )
        {
            if (!cache.TryGetValue(other, out var t))
            {
                try
                {
                    t = Read(files, Path.GetFileName(other), System.Array.Empty<SpriteTable>());
                }
                catch (System.Exception e)
                    when (e
                            is IOException
                                or InvalidDataException
                                or System.UnauthorizedAccessException
                                or UnsupportedFormatException
                    ) { }

                cache[other] = t;
            }

            if (t != null)
            {
                yield return t;
            }
        }
    }

    /// <summary>The textures the table's sprites name that the install lacks.</summary>
    public override IReadOnlyList<string> Unresolved(GameFileSystem files, string name) =>
        AssetReferences.Missing(
            ((SpriteTable)Load(files, name)).Sprites.Select(s => s.Texture),
            n => AssetReferences.HasTexture(files, n)
        );

    /// <summary>
    /// One note per sprite whose @tmaterial is no render type the engine draws, then the table's own notes.
    /// A missing texture is left out; Unresolved lists it.
    /// </summary>
    public override IReadOnlyList<string> Check(GameFileSystem files, string name)
    {
        var table = (SpriteTable)Load(files, name);

        return table
            .Sprites.Where(s => s.Material != null && !ModelRules.IsRenderType(s.Material))
            .Select(s =>
                $"{s.Table}:{s.Line}: sprite '{s.Name}': texture material '{s.Material}' is not a render type the engine knows"
            )
            .Concat(table.Notes)
            .ToList();
    }
}
