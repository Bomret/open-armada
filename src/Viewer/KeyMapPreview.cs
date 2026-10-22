using System.Linq;
using Godot;
using OpenArmada.Assets;
using OpenArmada.Formats;

namespace OpenArmada.Viewer;

/// <summary>Key and label map preview: each binding as "section  sign key  value", notes first.</summary>
public sealed class KeyMapPreview : KindPreview
{
    /// <inheritdoc/>
    public override string KindName => KeyMapKind.KindName;

    /// <inheritdoc/>
    public override Preview Create(GameFileSystem files, AssetKind kind, string name)
    {
        var map = (MapFile)kind.Load(files, name);

        return new Preview
        {
            View = ReadOnlyText(
                NotesBlock(map.Notes)
                    + string.Join("\n", map.Entries.Select(e => $"{e.Section, -28} {e.Sign} {e.Key, -14} {e.Value}")),
                true
            ),
            Notes = map.Notes,
        };
    }
}
