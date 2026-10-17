using System.Collections.Generic;

namespace OpenArmada.Assets;

/// <summary>
/// The team colors: teamcolor.odf, read once as one asset. The check lists a missing file and a slot whose
/// first matching key is not three numbers (each such slot is black), once for the install.
/// </summary>
public sealed class TeamColorKind : AssetKind
{
    /// <summary>The kind's <see cref="Name"/>: <c>teamcolors</c>.</summary>
    public const string KindName = "teamcolors";
    const string File = "teamcolor.odf";

    /// <inheritdoc/>
    public override string Name => KindName;

    /// <inheritdoc/>
    public override IEnumerable<string> Enumerate(GameFileSystem files) => new[] { File };

    /// <inheritdoc/>
    public override object Load(GameFileSystem files, string name) => TeamColors.Load(files);

    /// <inheritdoc/>
    public override IReadOnlyList<string> Check(GameFileSystem files, string name) => TeamColors.Load(files).Notes;
}
