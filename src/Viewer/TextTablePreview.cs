using System.Linq;
using Godot;
using OpenArmada.Assets;
using OpenArmada.Formats;

namespace OpenArmada.Viewer;

// Text table preview: each entry as `block.key = "value"` with the file it came from, notes first.
public sealed class TextTablePreview : KindPreview
{
    public override string KindName => TextTableKind.KindName;

    public override Control Create(GameFileSystem files, AssetKind kind, string name)
    {
        var table = (TextTable)kind.Load(files, name);

        return ReadOnlyText(
            NotesBlock(table.Notes)
                + string.Join("\n", table.Entries.Select(e => $"{e.File}:{e.Line}  {e.Block}.{e.Key} = \"{e.Value}\"")),
            true
        );
    }
}
