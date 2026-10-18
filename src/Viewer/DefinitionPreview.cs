using Godot;
using OpenArmada.Assets;

namespace OpenArmada.Viewer;

/// <summary>Object definition preview: the resolved values, each marked own, inherited, or overridden.</summary>
public sealed class DefinitionPreview : KindPreview
{
    /// <inheritdoc/>
    public override string KindName => DefinitionKind.KindName;

    /// <inheritdoc/>
    public override Preview Create(GameFileSystem files, AssetKind kind, string name)
    {
        var def = (ResolvedDefinition)kind.Load(files, name);
        return new Preview { View = ReadOnlyText(DefinitionText.Format(def)), Notes = DefinitionText.Remarks(def) };
    }
}
