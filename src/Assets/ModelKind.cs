using System.Collections.Generic;
using OpenArmada.Formats;

namespace OpenArmada.Assets;

// Models: the .sod files of the SOD folder and its software-renderer copies.
public sealed class ModelKind : AssetKind
{
    public override string Name => "models";

    public override IEnumerable<string> Enumerate(GameFileSystem files) => files.List(".sod", "SOD", "SOD/Software");

    public override object Load(GameFileSystem files, string name) => Sod.Read(files.ReadAllBytes(name));
}
