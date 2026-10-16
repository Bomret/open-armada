using Godot;
using OpenArmada.Assets;
using OpenArmada.Formats;
using OpenArmada.Rendering;

namespace OpenArmada.Viewer;

// Model preview: the SOD model in its own 3D viewport.
public sealed class ModelPreview : KindPreview
{
    public override string KindName => ModelKind.KindName;

    public override Control Create(GameFileSystem files, AssetKind kind, string name) =>
        new ModelPreviewPane(new SodBuilder(files).Build((Sod)kind.Load(files, name)));
}
