using System.Collections.Generic;
using System.Text;

namespace OpenArmada.Assets;

// Mission objective texts: the members of objtxt.zfs, plain text in Windows-1252.
public sealed class ObjectiveTextKind : AssetKind
{
    public const string KindName = "objectives";

    public override string Name => KindName;

    public override IEnumerable<string> Enumerate(GameFileSystem files) => files.Members("objtxt.zfs");

    // The text of one objective.
    public override object Load(GameFileSystem files, string name) =>
        CodePagesEncodingProvider.Instance.GetEncoding(1252)!.GetString(files.ReadAllBytes(name));
}
