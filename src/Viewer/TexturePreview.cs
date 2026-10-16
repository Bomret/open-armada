using System.IO;
using System.Linq;
using Godot;
using OpenArmada.Assets;
using OpenArmada.Formats;

namespace OpenArmada.Viewer;

// Texture preview: the texture at native size, then each of its mip levels.
public sealed class TexturePreview : KindPreview
{
    public override string KindName => TextureKind.KindName;

    public override Control Create(GameFileSystem files, AssetKind kind, string name)
    {
        var t = (TextureAsset)kind.Load(files, name);
        var levels = new[] { (t.Name, t.Info, t.Data) }.Concat(t.Mips.Select(m => (m.Name, m.Info, m.Data)));
        var images = levels.Select(l => ($"{l.Name}: {Describe(l.Info)}", Decode(l.Data, l.Name))).ToList();
        return new ImagePane($"{Describe(t.Info)}, {t.Mips.Count} mip level(s)", images);
    }

    static string Describe(TgaInfo i) =>
        $"{i.Width}x{i.Height}, TGA {(i.Indexed ? "8-bit color-mapped" : $"{i.BitsPerPixel}-bit true color")}";

    static Image Decode(byte[] data, string name)
    {
        var image = new Image();
        var error = image.LoadTgaFromBuffer(data);
        return error == Error.Ok ? image : throw new InvalidDataException($"{name}: Godot cannot decode it ({error})");
    }
}
