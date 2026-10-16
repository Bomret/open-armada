using System;
using System.Collections.Generic;
using System.Linq;
using OpenArmada.Formats;

namespace OpenArmada.Assets;

// Key and label maps: the .map files of the install root (Gamekey.map, Input.map, label.map), Windows-1252
// text. Load returns a MapFile.
public sealed class MapKind : AssetKind
{
    public const string KindName = "maps";

    public override string Name => KindName;

    public override IEnumerable<string> Enumerate(GameFileSystem files) =>
        files.Inventory().Where(f => f.EndsWith(".map", StringComparison.OrdinalIgnoreCase) && !f.Contains('/') && !f.Contains("::"));

    public override object Load(GameFileSystem files, string name) =>
        MapFile.Read(name, InterfaceConfigKind.Windows1252.GetString(files.ReadAllBytes(name)));

    public override IReadOnlyList<string> Check(GameFileSystem files, string name) => ((MapFile)Load(files, name)).Notes;
}
