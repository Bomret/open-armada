using System.Collections.Generic;
using OpenArmada.Formats;

namespace OpenArmada.Assets;

/// <summary>
/// Settings files: the .set files of the install root (shell.set, mshell.set, mshelldemo.set). Load returns a
/// SettingsFile.
/// </summary>
public sealed class SettingsKind : AssetKind
{
    /// <summary>The kind's <see cref="Name"/>: <c>settings</c>.</summary>
    public const string KindName = "settings";

    /// <inheritdoc/>
    public override string Name => KindName;

    /// <inheritdoc/>
    public override IEnumerable<string> Enumerate(GameFileSystem files) => RootFiles(files, ".set");

    /// <inheritdoc/>
    public override object Load(GameFileSystem files, string name) => SettingsFile.Read(name, files.ReadAllBytes(name));

    /// <inheritdoc/>
    public override IReadOnlyList<string> Check(GameFileSystem files, string name) =>
        ((SettingsFile)Load(files, name)).Notes;
}
