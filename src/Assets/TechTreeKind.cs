using System.Collections.Generic;
using OpenArmada.Formats;

namespace OpenArmada.Assets;

// Tech trees (.tt): the build requirements of the objects a mission lets a race build.
public sealed class TechTreeKind : AssetKind
{
    public const string KindName = "techtrees";

    public override string Name => KindName;

    public override IEnumerable<string> Enumerate(GameFileSystem files) => DefinitionKind.Named(files, ".tt");

    public override object Load(GameFileSystem files, string name) =>
        TechTree.Parse(Windows1252.Encoding.GetString(files.ReadAllBytes(name)));
}
