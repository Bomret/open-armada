using Godot;
using OpenArmada.Assets;

namespace OpenArmada.Viewer;

/// <summary>Object definition preview: the resolved values, each marked own, inherited, or overridden.</summary>
public sealed class DefinitionPreview : KindPreview
{
    /// <inheritdoc/>
    public override string KindName => DefinitionKind.KindName;

    /// <inheritdoc/>
    public override Control Create(GameFileSystem files, AssetKind kind, string name) =>
        ReadOnlyText(DefinitionText.Format((ResolvedDefinition)kind.Load(files, name)));
}
