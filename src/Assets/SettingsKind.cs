using System.Collections.Generic;
using OpenArmada.Formats;

namespace OpenArmada.Assets;

// Settings files: the .set files of the install root (shell.set, mshell.set, mshelldemo.set). Load returns a
// SettingsFile.
public sealed class SettingsKind : AssetKind
{
    public const string KindName = "settings";

    public override string Name => KindName;

    public override IEnumerable<string> Enumerate(GameFileSystem files) => RootFiles(files, ".set");

    public override object Load(GameFileSystem files, string name) => SettingsFile.Read(name, files.ReadAllBytes(name));

    public override IReadOnlyList<string> Check(GameFileSystem files, string name) => ((SettingsFile)Load(files, name)).Notes;
}
