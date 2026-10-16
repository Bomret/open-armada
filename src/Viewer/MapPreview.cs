using System.Linq;
using Godot;
using OpenArmada.Assets;
using OpenArmada.Formats;

namespace OpenArmada.Viewer;

// Key and label map preview: each binding as "section  sign key  value", notes first.
public sealed class MapPreview : KindPreview
{
    public override string KindName => MapKind.KindName;

    public override Control Create(GameFileSystem files, AssetKind kind, string name)
    {
        var map = (MapFile)kind.Load(files, name);
        var notes = map.Notes.Count == 0 ? "" : "NOTES\n" + string.Join("\n", map.Notes) + "\n\n";
        return TextPane.Create(notes + string.Join("\n", map.Entries.Select(e => $"{e.Section,-28} {e.Sign} {e.Key,-14} {e.Value}")));
    }
}
