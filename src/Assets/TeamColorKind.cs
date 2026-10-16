using System.Collections.Generic;

namespace OpenArmada.Assets;

// The team colors: teamcolor.odf, read once as one asset. The check lists a missing file, a slot without
// a line, and a line that is not three numbers (each such slot is black), once for the install.
public sealed class TeamColorKind : AssetKind
{
    public const string KindName = "teamcolors";
    const string File = "teamcolor.odf";

    public override string Name => KindName;

    public override IEnumerable<string> Enumerate(GameFileSystem files) => new[] { File };

    public override object Load(GameFileSystem files, string name) => TeamColors.Load(files);

    public override IReadOnlyList<string> Check(GameFileSystem files, string name) => TeamColors.Load(files).Notes;
}
