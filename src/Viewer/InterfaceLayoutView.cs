using System.Collections.Generic;
using Godot;
using OpenArmada.Formats;

namespace OpenArmada.Viewer;

// The elements of an interface configuration drawn on a 640x480 screen at their positions: panels in cyan,
// other elements in green, each with its name, and the sprite region of an element whose sprite is known.
public partial class InterfaceLayoutView : Control
{
    public static readonly Vector2 Screen = new(640, 480);

    readonly List<(LayoutElement Element, Texture2D? Texture, Rect2 Region)> _items;

    public InterfaceLayoutView(List<(LayoutElement Element, Texture2D? Texture, Rect2 Region)> items)
    {
        _items = items;
        CustomMinimumSize = Screen;
        TextureFilter = TextureFilterEnum.Nearest;
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Screen), Colors.Black);
        foreach (var (e, texture, region) in _items)
        {
            var rect = new Rect2(e.X, e.Y, e.Width, e.Height);
            if (texture != null)
            {
                DrawTextureRectRegion(texture, rect, region);
            }
            DrawRect(rect, e.IsPanel ? Colors.Cyan : Colors.Green, false);
            DrawString(
                ThemeDB.FallbackFont,
                rect.Position + new Vector2(1, 8),
                e.Name,
                HorizontalAlignment.Left,
                -1,
                7,
                e.IsPanel ? Colors.Cyan : Colors.Yellow
            );
        }
    }
}
