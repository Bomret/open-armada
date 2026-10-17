using Godot;
using OpenArmada.Assets;
using OpenArmada.Diagnostics;

namespace OpenArmada.Viewer;

/// <summary>Object definition preview: the resolved values, each marked own, inherited, or overridden.</summary>
public sealed class DefinitionPreview : KindPreview
{
    /// <inheritdoc/>
    public override string KindName => DefinitionKind.KindName;

    /// <inheritdoc/>
    public override Control Create(GameFileSystem files, AssetKind kind, string name)
    {
        var def = (ResolvedDefinition)kind.Load(files, name);
        ViewerLog.Notes(name, DefinitionText.Remarks(def));

        return ReadOnlyText(DefinitionText.Format(def));
    }
}
