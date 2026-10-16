using System.Collections.Generic;
using System.IO;
using OpenArmada.Formats;

namespace OpenArmada.Assets;

// Text tables: the .ldl files of the install root (default, language, string, string_e), the launcher's
// (splash.exe) configuration script. Each is XOR-encoded with a key that Load reads at run time from the
// install's own splash.exe; a missing or short splash.exe, or text that does not decode with the key,
// throws UnsupportedFormatException with that reason. Load returns a TextTable with its imports read in.
// splash.exe, the .ldl file, and its imports are read from the install by path, as the launcher opens
// them: a copy in addon\, an archive, or the index does not replace them. An import resolves relative to
// the folder of the importing file; ReadByPath finds nothing outside the root.
public sealed class TextTableKind : AssetKind
{
    public const string KindName = "texttables";
    const string Launcher = "splash.exe";

    public override string Name => KindName;

    public override IEnumerable<string> Enumerate(GameFileSystem files) => RootFiles(files, ".ldl");

    public override object Load(GameFileSystem files, string name)
    {
        if (files.ReadByPath(Launcher) is not { } exe) throw new UnsupportedFormatException($"{name}: the text table key is read from {Launcher}, which the install lacks");
        var key = TextTable.KeyFrom(Launcher, exe);
        var raw = files.ReadByPath(name) ?? throw new FileNotFoundException($"'{name}' is not in the install root", name);
        return TextTable.Read(name, raw, key, files.ReadByPath);
    }

    public override IReadOnlyList<string> Check(GameFileSystem files, string name) => ((TextTable)Load(files, name)).Notes;

    // The root file itself: a text table is read by path, past any override of its name.
    public override IEnumerable<string> Reads(GameFileSystem files, string name) => new[] { name };
}
