using System.Collections.Generic;
using Godot;

namespace OpenArmada.Viewer;

/// <summary>
/// Images one under the other, each with a caption, below a summary line. Each image is scaled to fit the pane with
/// its aspect ratio kept and its pixels kept sharp. Scrolls when there are more images than fit.
/// </summary>
public partial class ImagePane : ScrollContainer
{
    readonly VBoxContainer _box = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
    readonly List<(Label Caption, TextureRect Picture, Vector2 Size)> _images = new();
    readonly Label _summary;

    /// <summary>Adds a VBoxContainer child with <c>summary</c> as the first label, then each image under its caption, with nearest filtering.</summary>
    public ImagePane(string summary, IEnumerable<(string Caption, Image Image)> images)
    {
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        SizeFlagsVertical = SizeFlags.ExpandFill;
        HorizontalScrollMode = ScrollMode.Disabled;
        AddChild(_box);
        _summary = new Label { Text = summary };
        _box.AddChild(_summary);
        foreach (var (caption, image) in images)
        {
            var label = new Label { Text = caption };
            var picture = new TextureRect
            {
                Texture = ImageTexture.CreateFromImage(image),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
                SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
            };
            _box.AddChild(label);
            _box.AddChild(picture);
            _images.Add((label, picture, image.GetSize()));
        }

        Resized += Fit;
    }

    // Scales each image to the largest size that fits the pane's width, less the scrollbar when several images can
    // overflow it, and the height left beside the labels and the two gaps between them.
    void Fit()
    {
        var width = Size.X - (_images.Count > 1 ? GetVScrollBar().GetCombinedMinimumSize().X : 0);
        var gaps = 2 * _box.GetThemeConstant("separation");
        foreach (var (caption, picture, size) in _images)
        {
            var height = Size.Y - _summary.GetCombinedMinimumSize().Y - caption.GetCombinedMinimumSize().Y - gaps;
            picture.CustomMinimumSize = (size * ImageFit.Scale(size.X, size.Y, width, height)).Floor();
        }
    }
}
