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

    // A definition fails when anything in its include chain is wrong (see ResolvedDefinition.Problems).
    public override IReadOnlyList<string> Check(GameFileSystem files, string name)
    {
        var problems = ResolvedDefinition.Resolve(files, name).Problems;
        if (problems.Count > 0) throw new InvalidDataException(string.Join("; ", problems));
        return Array.Empty<string>();
    }

    // Names of the files with the extension that the archives and the override layer hold. Loose files
    // elsewhere in the install (events.odf is not a definition) are left out.
    internal static IEnumerable<string> Named(GameFileSystem files, string extension) =>
        files.Inventory()
            .Where(f => f.Contains("::") || f.StartsWith("addon/", StringComparison.OrdinalIgnoreCase))
            .Select(f => f[(Math.Max(f.LastIndexOf('/'), f.LastIndexOf(':')) + 1)..])
            .Where(f => f.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase);
}
