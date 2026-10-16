using System.Collections.Generic;
using OpenArmada.Formats;

namespace OpenArmada.Assets;

// Models: the .sod files of the SOD folder and its software-renderer copies.
public sealed class ModelKind : AssetKind
{
    public const string KindName = "models";

    public override string Name => KindName;

    public override IEnumerable<string> Enumerate(GameFileSystem files) => files.List(".sod", "SOD", "SOD/Software");

    public override object Load(GameFileSystem files, string name) => Sod.Read(files.ReadAllBytes(name));

    // What the model holds that the engine cannot draw as the data says (ModelRules.Check): notes, no failure.
    public override IReadOnlyList<string> Check(GameFileSystem files, string name) => ModelRules.Check(files, (Sod)Load(files, name));
}
