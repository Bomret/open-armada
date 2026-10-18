using Godot;
using OpenArmada.Assets;
using OpenArmada.Formats;

namespace OpenArmada.Viewer;

/// <summary>Tech tree preview: the build requirements per race.</summary>
public sealed class TechTreePreview : KindPreview
{
    /// <inheritdoc/>
    public override string KindName => TechTreeKind.KindName;

    /// <inheritdoc/>
    public override Preview Create(GameFileSystem files, AssetKind kind, string name) =>
        new Preview { View = ReadOnlyText(DefinitionText.Format(files, (TechTree)kind.Load(files, name))) };
}
