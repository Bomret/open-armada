using System.Collections.Generic;
using Godot;

namespace OpenArmada.Viewer;

/// <summary>
/// Images at native size, one under the other, each with a caption, below a summary line. Scrolls
/// when the images are larger than the pane.
/// </summary>
public partial class ImagePane : ScrollContainer
{
    /// <summary>Adds a VBoxContainer child with <c>summary</c> as the first label, then each image under its caption, unscaled and with nearest filtering.</summary>
    public ImagePane(string summary, IEnumerable<(string Caption, Image Image)> images)
    {
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        SizeFlagsVertical = SizeFlags.ExpandFill;
        var box = new VBoxContainer();
        AddChild(box);
        box.AddChild(new Label { Text = summary });
        foreach (var (caption, image) in images)
        {
            box.AddChild(new Label { Text = caption });
            box.AddChild(
                new TextureRect
                {
                    Texture = ImageTexture.CreateFromImage(image),
                    StretchMode = TextureRect.StretchModeEnum.Keep,
                    TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
                }
            );
        }
    }
}
