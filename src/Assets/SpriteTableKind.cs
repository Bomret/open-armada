using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenArmada.Formats;

namespace OpenArmada.Assets;

// Sprite tables: the .spr files of the Sprites folder. Names carry the folder ("Sprites/gui_map.spr");
// an @include names a table of the same folder. Load returns a SpriteTable.
public sealed class SpriteTableKind : AssetKind
{
    public const string KindName = "sprites";
    const string Folder = "Sprites/";

    public override string Name => KindName;

    public override IEnumerable<string> Enumerate(GameFileSystem files) => files.List(".spr", "Sprites").Select(n => Folder + n);

    public override object Load(GameFileSystem files, string name) =>
        SpriteTable.Read(Path.GetFileName(name), n => files.Find(Folder + n) is null ? null : System.Text.Encoding.Latin1.GetString(files.ReadAllBytes(Folder + n)));

    // One note per sprite whose texture (a .tga) the install lacks, then the table's own notes.
    public override IReadOnlyList<string> Check(GameFileSystem files, string name)
    {
        var table = (SpriteTable)Load(files, name);
        return table.Sprites.Where(s => files.Find(s.Texture + ".tga") is null)
            .Select(s => $"{s.Table}:{s.Line}: sprite '{s.Name}': texture '{s.Texture}' not found")
            .Concat(table.Notes).ToList();
    }
}
