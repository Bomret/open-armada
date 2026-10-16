using System;
using System.Collections.Generic;
using System.Linq;
using OpenArmada.Formats;

namespace OpenArmada.Assets;

// Cursors: the .cur files of the cursors folder. Names carry the folder; Load returns a CursorImage.
public sealed class CursorKind : AssetKind
{
    public const string KindName = "cursors";

    public override string Name => KindName;

    public override IEnumerable<string> Enumerate(GameFileSystem files) =>
        files.Inventory().Where(f => f.StartsWith("cursors/", StringComparison.OrdinalIgnoreCase) && f.EndsWith(".cur", StringComparison.OrdinalIgnoreCase));

    public override object Load(GameFileSystem files, string name) => CursorImage.Read(name, files.ReadAllBytes(name));
}
