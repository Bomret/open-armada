using System.Collections.Generic;
using OpenArmada.Formats;

namespace OpenArmada.Assets;

/// <summary>Tech trees (.tt): the build requirements of the objects a mission lets a race build.</summary>
public sealed class TechTreeKind : AssetKind
{
    /// <summary>The kind's <see cref="Name"/>: <c>techtrees</c>.</summary>
    public const string KindName = "techtrees";

    /// <inheritdoc/>
    public override string Name => KindName;

    /// <inheritdoc/>
    public override IEnumerable<string> Enumerate(GameFileSystem files) => DefinitionKind.Named(files, ".tt");

    /// <inheritdoc/>
    public override object Load(GameFileSystem files, string name) =>
        TechTree.Parse(Windows1252.Encoding.GetString(files.ReadAllBytes(name)));
}
