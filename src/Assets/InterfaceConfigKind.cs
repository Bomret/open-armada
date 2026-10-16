using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OpenArmada.Formats;

namespace OpenArmada.Assets;

// Interface configurations: the misc/gui_*.cfg files, which the race definitions name by
// interfaceConfiguration and which #include gui_glob.cfg. The root CCA.cfg, Collision.cfg, Logfile.cfg and
// woncfg.cfg are not interface configurations. Names carry their folder; Load returns an InterfaceConfig.
public sealed class InterfaceConfigKind : AssetKind
{
    public const string KindName = "interfaces";
    const string Folder = "misc/";

    public override string Name => KindName;

    public override IEnumerable<string> Enumerate(GameFileSystem files) =>
        files.Inventory().Where(f => f.StartsWith(Folder + "gui_", StringComparison.OrdinalIgnoreCase)
            && f.EndsWith(".cfg", StringComparison.OrdinalIgnoreCase) && f.IndexOf('/', Folder.Length) < 0);

    public override object Load(GameFileSystem files, string name)
    {
        string? Text(string n) => files.Find(Folder + n) is null ? null : Windows1252.GetString(files.ReadAllBytes(Folder + n));
        var bare = name[(name.LastIndexOf('/') + 1)..];
        return InterfaceConfig.Read(bare, Windows1252.GetString(files.ReadAllBytes(name)), Text);
    }

    public override IReadOnlyList<string> Check(GameFileSystem files, string name) => ((InterfaceConfig)Load(files, name)).Notes;

    internal static Encoding Windows1252 => CodePagesEncodingProvider.Instance.GetEncoding(1252)!;
}
