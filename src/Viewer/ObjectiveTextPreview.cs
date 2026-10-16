using Godot;
using OpenArmada.Assets;

namespace OpenArmada.Viewer;

// Objective text preview: the text, read-only.
public sealed class ObjectiveTextPreview : KindPreview
{
    public override string KindName => ObjectiveTextKind.KindName;

    public override Control Create(GameFileSystem files, AssetKind kind, string name) =>
        ReadOnlyText((string)kind.Load(files, name), wrap: true);
}
