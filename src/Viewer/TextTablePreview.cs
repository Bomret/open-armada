using Godot;
using OpenArmada.Assets;

namespace OpenArmada.Viewer;

// Text table preview: loading throws while no text table can be decoded, so the browser shows the reason
// in its place (the table's id and text entries replace this once a cipher is established).
public sealed class TextTablePreview : KindPreview
{
    public override string KindName => TextTableKind.KindName;

    public override Control Create(GameFileSystem files, AssetKind kind, string name) =>
        TextPane.Create(kind.Load(files, name).ToString() ?? "");
}
