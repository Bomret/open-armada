using Godot;
using OpenArmada.Assets;
using OpenArmada.Formats;

namespace OpenArmada.Viewer;

// Bitmap preview: the image at native size with its dimensions and format.
public sealed class BitmapPreview : KindPreview
{
    public override string KindName => BitmapKind.KindName;

    public override Control Create(GameFileSystem files, AssetKind kind, string name)
    {
        var info = (BmpInfo)kind.Load(files, name);
        var image = GodotDecoders.Bmp(files.ReadAllBytes(name), name);
        var summary = $"{info.Width}x{info.Height}, BMP {info.BitsPerPixel}-bit";
        return new ImagePane(summary, new[] { (name, image) });
    }
}
