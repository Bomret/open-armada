using System.Linq;
using Godot;
using OpenArmada.Assets;
using OpenArmada.Formats;

namespace OpenArmada.Viewer;

/// <summary>Text table preview: each entry as <c>block.key = "value"</c> with the file it came from, notes first.</summary>
public sealed class TextTablePreview : KindPreview
{
    /// <inheritdoc/>
    public override string KindName => TextTableKind.KindName;

    /// <inheritdoc/>
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
