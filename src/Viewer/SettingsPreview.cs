using System.Linq;
using Godot;
using OpenArmada.Assets;
using OpenArmada.Formats;

namespace OpenArmada.Viewer;

/// <summary>Settings file preview: its lines with their numbers, notes first.</summary>
public sealed class SettingsPreview : KindPreview
{
    /// <inheritdoc/>
    public override string KindName => SettingsKind.KindName;

    /// <inheritdoc/>
    public override Preview Create(GameFileSystem files, AssetKind kind, string name)
    {
        var s = (SettingsFile)kind.Load(files, name);

        return new Preview
        {
            View = ReadOnlyText(
                NotesBlock(s.Notes) + string.Join("\n", s.Entries.Select(e => $"{e.Line, 3}  {e.Text}")),
                true
            ),
            Notes = s.Notes,
        };
    }
}
