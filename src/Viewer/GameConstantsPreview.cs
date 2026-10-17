using System.Globalization;
using System.Linq;
using Godot;
using OpenArmada.Assets;
using OpenArmada.Formats;

namespace OpenArmada.Viewer;

// Game constants preview: the values in file order with their type and value (a kept text in quotes
// when it is a string, bare when it is an expression), notes first.
public sealed class GameConstantsPreview : KindPreview
{
    public override string KindName => GameConstantsKind.KindName;

    public override Control Create(GameFileSystem files, AssetKind kind, string name)
    {
        var c = (GameConstants)kind.Load(files, name);
        var includes = c.Includes.Count == 0 ? "" : "INCLUDES\n" + string.Join("\n", c.Includes) + "\n\n";
        var width = c.Values.Count == 0 ? 0 : c.Values.Max(v => v.Name.Length);
        var values = c.Values.Select(v =>
            $"{v.Type, -6} {v.Name.PadRight(width)}  {(v.Number is { } n ? n.ToString(CultureInfo.InvariantCulture) : v.IsString ? $"\"{v.Text}\"" : v.Text)}"
        );

        return ReadOnlyText(NotesBlock(c.Notes) + includes + string.Join("\n", values), true);
    }
}
