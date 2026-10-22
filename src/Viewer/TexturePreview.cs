using System.Linq;
using Godot;
using OpenArmada.Assets;
using OpenArmada.Formats;

namespace OpenArmada.Viewer;

/// <summary>Texture preview: the texture at native size, then each of its mip levels.</summary>
public sealed class TexturePreview : KindPreview
{
    /// <inheritdoc/>
    public override string KindName => TextureKind.KindName;

    /// <inheritdoc/>
    public override Preview Create(GameFileSystem files, AssetKind kind, string name)
    {
        var t = (TextureAsset)kind.Load(files, name);
        var images = t
            .Mips.Prepend(t.Base)
            .Select(l => ($"{l.Name}: {Describe(l.Info)}", GodotDecoders.Tga(l.Data, l.Name)))
            .ToList();

        return new Preview { View = new ImagePane($"{Describe(t.Base.Info)}, {t.Mips.Count} mip level(s)", images) };
    }

    static string Describe(TgaInfo i) =>
        $"{i.Width}x{i.Height}, TGA {(i.Indexed ? "8-bit color-mapped" : $"{i.BitsPerPixel}-bit true color")}";
}
