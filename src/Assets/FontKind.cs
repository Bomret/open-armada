using System;
using System.Collections.Generic;
using System.Linq;
using OpenArmada.Formats;

namespace OpenArmada.Assets;

// Fonts: the .FNT files of the install root (BZONE.FNT, BZONEJ.FNT). Load returns a BitmapFont.
public sealed class FontKind : AssetKind
{
    public const string KindName = "fonts";

    public override string Name => KindName;

    public override IEnumerable<string> Enumerate(GameFileSystem files) =>
        files.Inventory().Where(f => f.EndsWith(".fnt", StringComparison.OrdinalIgnoreCase) && !f.Contains('/') && !f.Contains("::"));

    public override object Load(GameFileSystem files, string name) => BitmapFont.Read(name, files.ReadAllBytes(name));
}
