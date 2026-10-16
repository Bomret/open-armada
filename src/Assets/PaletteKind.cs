using System.Collections.Generic;
using OpenArmada.Formats;

namespace OpenArmada.Assets;

// Palettes: the .pal files in the install root (Armada.pal) and in Textures/RGB.
public sealed class PaletteKind : AssetKind
{
    public const string KindName = "palettes";

    public override string Name => KindName;

    public override IEnumerable<string> Enumerate(GameFileSystem files) => files.List(".pal", "", "Textures/RGB");

    public override object Load(GameFileSystem files, string name) => Palette.Read(files.ReadAllBytes(name));
}
