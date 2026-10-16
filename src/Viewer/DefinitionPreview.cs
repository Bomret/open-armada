using Godot;
using OpenArmada.Assets;

namespace OpenArmada.Viewer;

// Object definition preview: the resolved values, each marked own, inherited, or overridden.
public sealed class DefinitionPreview : KindPreview
{
    public override string KindName => DefinitionKind.KindName;

    public override Control Create(GameFileSystem files, AssetKind kind, string name) =>
        new TextEdit
        {
            Text = DefinitionText.Format((ResolvedDefinition)kind.Load(files, name)),
            Editable = false,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
}
