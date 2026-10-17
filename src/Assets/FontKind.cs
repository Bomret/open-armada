using System.Collections.Generic;
using OpenArmada.Formats;

namespace OpenArmada.Assets;

/// <summary>Fonts: the .FNT files of the install root (BZONE.FNT, BZONEJ.FNT). Load returns a BitmapFont.</summary>
public sealed class FontKind : AssetKind
{
    /// <summary>The kind's <see cref="Name"/>: <c>fonts</c>.</summary>
    public const string KindName = "fonts";

    /// <inheritdoc/>
    public override string Name => KindName;

    /// <inheritdoc/>
    public override IEnumerable<string> Enumerate(GameFileSystem files) => RootFiles(files, ".fnt");

    /// <inheritdoc/>
    public override object Load(GameFileSystem files, string name) => BitmapFont.Read(name, files.ReadAllBytes(name));
}
