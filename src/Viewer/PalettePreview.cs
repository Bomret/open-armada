using System.Collections.Generic;
using Godot;
using OpenArmada.Assets;

namespace OpenArmada.Viewer;

// Palette preview: the 256 colors as a 16x16 grid of swatches, row-major by index.
public sealed class PalettePreview : KindPreview
{
    public override string KindName => PaletteKind.KindName;

    public override Control Create(GameFileSystem files, AssetKind kind, string name)
    {
        var colors = (IReadOnlyList<(byte R, byte G, byte B)>)kind.Load(files, name);
        var image = Image.CreateEmpty(16, 16, false, Image.Format.Rgb8);
        for (var i = 0; i < colors.Count; i++)
            image.SetPixel(i % 16, i / 16, Color.Color8(colors[i].R, colors[i].G, colors[i].B));
        var swatches = new TextureRect
        {
            Texture = ImageTexture.CreateFromImage(image),
            CustomMinimumSize = new Vector2(512, 512),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
        };
        var box = new VBoxContainer();
        box.AddChild(new Label { Text = $"{colors.Count} colors, 8 bits per channel" });
        box.AddChild(swatches);
        return box;
    }
}
