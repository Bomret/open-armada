using System.Collections.Generic;
using OpenArmada.Formats;

namespace OpenArmada.Assets;

/// <summary>
/// Key and label maps: the .map files of the install root (Gamekey.map, Input.map, label.map), Windows-1252
/// text. Load returns a MapFile. Named keymaps because the maps kind is the .bzn missions.
/// </summary>
public sealed class KeyMapKind : AssetKind
{
    /// <summary>The kind's <see cref="Name"/>: <c>keymaps</c>.</summary>
    public const string KindName = "keymaps";

    /// <inheritdoc/>
    public override string Name => KindName;

    /// <inheritdoc/>
    public override IEnumerable<string> Enumerate(GameFileSystem files) => RootFiles(files, ".map");

    /// <inheritdoc/>
    public override object Load(GameFileSystem files, string name) =>
        MapFile.Read(name, Windows1252.Encoding.GetString(files.ReadAllBytes(name)));

    /// <inheritdoc/>
    public override IReadOnlyList<string> Check(GameFileSystem files, string name) =>
        ((MapFile)Load(files, name)).Notes;
}
