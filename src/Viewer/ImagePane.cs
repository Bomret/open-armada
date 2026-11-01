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
    readonly List<(Label Caption, AspectRatioContainer Fit)> _images = new();
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
            };
            var fit = new AspectRatioContainer
            {
                Ratio = image.GetHeight() > 0 ? (float)image.GetWidth() / image.GetHeight() : 0,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
            };
            fit.AddChild(picture);
            _box.AddChild(label);
            _box.AddChild(fit);
            _images.Add((label, fit));
        }

        Resized += Fit;
    }

    // Gives each image the height left beside the labels and the two gaps between them; its AspectRatioContainer
    // fits the image into that height and the pane's width, which already excludes a shown scrollbar.
    void Fit()
    {
        var gaps = 2 * _box.GetThemeConstant("separation");
        foreach (var (caption, fit) in _images)
        {
            var height = Size.Y - _summary.GetCombinedMinimumSize().Y - caption.GetCombinedMinimumSize().Y - gaps;
            fit.CustomMinimumSize = new Vector2(0, Mathf.Floor(Mathf.Max(height, 0)));
        }
    }
}
