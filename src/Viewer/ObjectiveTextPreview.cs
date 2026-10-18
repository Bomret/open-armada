using Godot;
using OpenArmada.Assets;

namespace OpenArmada.Viewer;

/// <summary>Objective text preview: the text, read-only.</summary>
public sealed class ObjectiveTextPreview : KindPreview
{
    /// <inheritdoc/>
    public override string KindName => ObjectiveTextKind.KindName;

    /// <inheritdoc/>
    public override Preview Create(GameFileSystem files, AssetKind kind, string name) =>
        new Preview { View = ReadOnlyText((string)kind.Load(files, name), wrap: true) };
}
