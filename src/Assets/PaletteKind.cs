using System.Collections.Generic;
using OpenArmada.Formats;

namespace OpenArmada.Assets;

/// <summary>Palettes: the .pal files in the install root (Armada.pal) and in Textures/RGB.</summary>
public sealed class PaletteKind : AssetKind
{
    /// <summary>The kind's <see cref="Name"/>: <c>palettes</c>.</summary>
    public const string KindName = "palettes";

    /// <inheritdoc/>
    public override string Name => KindName;

    /// <inheritdoc/>
    public override IEnumerable<string> Enumerate(GameFileSystem files) => files.List(".pal", "", "Textures/RGB");

    /// <inheritdoc/>
    public override object Load(GameFileSystem files, string name) => Palette.Read(files.ReadAllBytes(name));
}
