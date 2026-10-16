using System.IO;
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
        var image = new Image();
        var error = image.LoadBmpFromBuffer(files.ReadAllBytes(name));
        if (error != Error.Ok) throw new InvalidDataException($"Godot cannot decode it ({error})");
        var summary = $"{info.Width}x{info.Height}, BMP {info.BitsPerPixel}-bit";
        return new ImagePane(summary, new[] { (name, image) });
    }
}
