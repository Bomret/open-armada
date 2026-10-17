using System.Collections.Generic;
using OpenArmada.Formats;

namespace OpenArmada.Assets;

/// <summary>Mission objective texts: the members of objtxt.zfs, plain text in Windows-1252.</summary>
public sealed class ObjectiveTextKind : AssetKind
{
    /// <summary>The kind's <see cref="Name"/>: <c>objectives</c>.</summary>
    public const string KindName = "objectives";

    /// <inheritdoc/>
    public override string Name => KindName;

    /// <inheritdoc/>
    public override IEnumerable<string> Enumerate(GameFileSystem files) => files.Members("objtxt.zfs");

    /// <summary>The text of one objective.</summary>
    public override object Load(GameFileSystem files, string name) =>
        Windows1252.Encoding.GetString(files.ReadAllBytes(name));
}
