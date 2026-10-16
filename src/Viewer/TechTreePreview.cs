using Godot;
using OpenArmada.Assets;
using OpenArmada.Formats;

namespace OpenArmada.Viewer;

// Tech tree preview: the build requirements per race.
public sealed class TechTreePreview : KindPreview
{
    public override string KindName => TechTreeKind.KindName;

    public override Control Create(GameFileSystem files, AssetKind kind, string name) =>
        ReadOnlyText(DefinitionText.Format(files, (TechTree)kind.Load(files, name)));
}
