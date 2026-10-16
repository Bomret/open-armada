using System.Collections.Generic;
using OpenArmada.Formats;

namespace OpenArmada.Assets;

// Key and label maps: the .map files of the install root (Gamekey.map, Input.map, label.map), Windows-1252
// text. Load returns a MapFile. Named keymaps because the maps kind is the .bzn missions.
public sealed class KeyMapKind : AssetKind
{
    public const string KindName = "keymaps";

    public override string Name => KindName;

    public override IEnumerable<string> Enumerate(GameFileSystem files) => RootFiles(files, ".map");

    public override object Load(GameFileSystem files, string name) =>
        MapFile.Read(name, Windows1252.Encoding.GetString(files.ReadAllBytes(name)));

    public override IReadOnlyList<string> Check(GameFileSystem files, string name) =>
        ((MapFile)Load(files, name)).Notes;
}
