using System.Collections.Generic;
using OpenArmada.Formats;

namespace OpenArmada.Assets;

// Text tables: the .ldl files of the install root (default, language, string, string_e), the launcher's
// (splash.exe) configuration script. Each is XOR-encoded with a key that Load reads at run time from the
// install's own splash.exe; a missing or short splash.exe, or text that does not decode with the key,
// throws UnsupportedFormatException with that reason. Load returns a TextTable with its imports read in.
public sealed class TextTableKind : AssetKind
{
    public const string KindName = "texttables";
    const string Launcher = "splash.exe";

    public override string Name => KindName;

    public override IEnumerable<string> Enumerate(GameFileSystem files) => RootFiles(files, ".ldl");

    public override object Load(GameFileSystem files, string name)
    {
        if (files.Find(Launcher) is null) throw new UnsupportedFormatException($"{name}: the text table key is read from {Launcher}, which the install lacks");
        var key = TextTable.KeyFrom(Launcher, files.ReadAllBytes(Launcher));
        return TextTable.Read(name, files.ReadAllBytes(name), key, n => files.Find(n) is null ? null : files.ReadAllBytes(n));
    }

    public override IReadOnlyList<string> Check(GameFileSystem files, string name) => ((TextTable)Load(files, name)).Notes;
}
