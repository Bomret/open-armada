using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OpenArmada.Assets;

// Object definitions (.odf): the archive's and the override layer's, each name once.
public sealed class DefinitionKind : AssetKind
{
    public const string KindName = "definitions";

    public override string Name => KindName;

    public override IEnumerable<string> Enumerate(GameFileSystem files) => Named(files, ".odf");

    // The resolved definition.
    public override object Load(GameFileSystem files, string name) => ResolvedDefinition.Resolve(files, name);

    // A definition fails when its include chain is wrong (ResolvedDefinition.Failures: an unreadable include,
    // a cycle, a chain too deep); a missing include is an unresolved reference, not a failure. A line the
    // reader does not recognize is a note (ResolvedDefinition.Notes).
    public override IReadOnlyList<string> Check(GameFileSystem files, string name)
    {
        var def = ResolvedDefinition.Resolve(files, name);
        if (def.Failures.Count > 0) throw new InvalidDataException(string.Join("; ", def.Failures));
        return def.Notes;
    }

    // The keys whose values name another asset, found in the install's definitions: a model (a SOD name
    // with or without ".sod"), a definition (with ".odf"), a texture or a sprite. Any key whose value ends
    // ".wav" names a sound. Of the install's keys whose values resolve, these name only models:
    // baseName, shotGeometry, dbName, particleArt, workerBeeName.
    static readonly string[] ModelKeys = { "baseName", "shotGeometry", "dbName", "particleArt", "workerBeeName" };
    static readonly string[] DefinitionKeys = { "physicsFile" };
    static readonly string[] VisualKeys =
    {
        "Sprite", "texture", "AnimTexture", "particleTexture", "smokeTexture", "steamTexture", "sprayTexture",
        "effectTexture", "explosionSprite", "mapSprite", "weldingBeamSprite",
    };

    // The models, definitions, sounds, textures and sprites the definition's own and overriding values
    // name that the install lacks, and the parents (includes) of its own file that the install lacks. A
    // value or a missing parent it inherits is the parent's to report.
    public override IReadOnlyList<string> Unresolved(GameFileSystem files, string name)
    {
        var def = ResolvedDefinition.Resolve(files, name);
        var values = def.Values.Where(v => v.Origin != ValueOrigin.Inherited && v.Tokens.Count > 0).ToList();
        IEnumerable<string> Of(string[] keys) => values.Where(v => keys.Contains(v.Key, StringComparer.OrdinalIgnoreCase)).Select(v => v.Tokens[0]);
        bool Has(string n) => files.Find(n) != null;
        // A bare sound name resolves through the index; one the index lacks resolves in any folder of sounds/.
        var sounds = new Lazy<HashSet<string>>(() => new SoundKind().Enumerate(files).Select(Path.GetFileName).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase));
        var sprites = new Lazy<HashSet<string>>(() => AssetReferences.SpriteNames(files));
        return AssetReferences.Missing(def.MissingOwnIncludes, _ => false)
            .Concat(AssetReferences.Missing(Of(ModelKeys), n => Has(n.EndsWith(".sod", StringComparison.OrdinalIgnoreCase) ? n : n + ".sod")))
            .Concat(AssetReferences.Missing(Of(DefinitionKeys), Has))
            .Concat(AssetReferences.Missing(values.Select(v => v.Tokens[0]).Where(t => t.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)), n => Has(n) || sounds.Value.Contains(n)))
            .Concat(AssetReferences.Missing(Of(VisualKeys), n => AssetReferences.HasTexture(files, n) || sprites.Value.Contains(n)))
            .ToList();
    }

    // Names of the files with the extension that the archives and the override layer hold. Loose files
    // elsewhere in the install (events.odf is not a definition) are left out, and so are loose files in a
    // subfolder of addon (the game reads addon non-recursively, so no name reaches them).
    internal static IEnumerable<string> Named(GameFileSystem files, string extension) =>
        files.Inventory()
            .Where(f => f.Contains("::") || (f.StartsWith("addon/", StringComparison.OrdinalIgnoreCase) && f.IndexOf('/', 6) < 0))
            .Select(f => f[(Math.Max(f.LastIndexOf('/'), f.LastIndexOf(':')) + 1)..])
            .Where(f => f.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase);
}
