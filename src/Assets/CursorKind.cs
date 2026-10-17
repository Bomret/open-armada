using System;
using System.Collections.Generic;
using System.Linq;
using OpenArmada.Formats;

namespace OpenArmada.Assets;

/// <summary>Cursors: the .cur files of the cursors folder. Names carry the folder; Load returns a CursorImage.</summary>
public sealed class CursorKind : AssetKind
{
    /// <summary>The kind's <see cref="Name"/>: <c>cursors</c>.</summary>
    public const string KindName = "cursors";

    /// <inheritdoc/>
    public override string Name => KindName;

    /// <inheritdoc/>
    public override IEnumerable<string> Enumerate(GameFileSystem files) =>
        files
            .Inventory()
            .Where(f =>
                f.StartsWith("cursors/", StringComparison.OrdinalIgnoreCase)
                && f.EndsWith(".cur", StringComparison.OrdinalIgnoreCase)
            );

    /// <inheritdoc/>
    public override object Load(GameFileSystem files, string name) => CursorImage.Read(name, files.ReadAllBytes(name));

    /// <inheritdoc/>
    public override IReadOnlyList<string> Check(GameFileSystem files, string name) =>
        ((CursorImage)Load(files, name)).Notes;
}
