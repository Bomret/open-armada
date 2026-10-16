using System;
using System.Collections.Generic;
using System.Linq;
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
        files
            .Inventory()
            .Where(f =>
                f.StartsWith(Folder + "gui_", StringComparison.OrdinalIgnoreCase)
                && f.EndsWith(".cfg", StringComparison.OrdinalIgnoreCase)
                && f.IndexOf('/', Folder.Length) < 0
            );

    public override object Load(GameFileSystem files, string name) => Read(files, name, null);

    // The entries of the files a load reads: the copy the bare name resolves to (an override in addon\ wins
    // over misc/), then each include. A load that fails keeps the entries read before it failed.
    public override IEnumerable<string> Reads(GameFileSystem files, string name)
    {
        var reads = new List<string>();
        try
        {
            Read(files, name, reads);
        }
        catch (Exception) { }
        return reads.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    static InterfaceConfig Read(GameFileSystem files, string name, List<string>? reads)
    {
        string Open(string n)
        {
            if (files.Entry(n) is { } entry)
                reads?.Add(entry);
            return Windows1252.Encoding.GetString(files.ReadAllBytes(n));
        }
        // The bare name resolves through the game's precedence (addon override first, then the index);
        // the misc folder is the fallback for an install whose index does not list the file.
        string? Text(string n) =>
            (
                files.Find(n) is not null ? n
                : files.Find(Folder + n) is not null ? Folder + n
                : null
            )
                is { } found
                ? Open(found)
                : null;
        var bare = name[(name.LastIndexOf('/') + 1)..];
        return InterfaceConfig.Read(bare, Text(bare) ?? Open(name), Text);
    }

    public override IReadOnlyList<string> Check(GameFileSystem files, string name) =>
        ((InterfaceConfig)Load(files, name)).Notes;
}
