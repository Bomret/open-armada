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
        var images = t
            .Mips.Prepend(t.Base)
            .Select(l => ($"{l.Name}: {Describe(l.Info)}", GodotDecoders.Tga(l.Data, l.Name)))
            .ToList();

        return new ImagePane($"{Describe(t.Base.Info)}, {t.Mips.Count} mip level(s)", images);
    }

    static string Describe(TgaInfo i) =>
        $"{i.Width}x{i.Height}, TGA {(i.Indexed ? "8-bit color-mapped" : $"{i.BitsPerPixel}-bit true color")}";
}
