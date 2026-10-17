using System.Collections.Generic;
using Godot;
using OpenArmada.Formats;

namespace OpenArmada.Viewer;

// A texture at native size with the named regions of its sprites outlined on it.
public partial class SpriteSheetView : Control
{
    readonly ImageTexture _texture;
    readonly List<(Sprite Sprite, Rect2 Rect)> _regions = new();

    public SpriteSheetView(Image image, IEnumerable<Sprite> sprites)
    {
        _texture = ImageTexture.CreateFromImage(image);
        CustomMinimumSize = new Vector2(image.GetWidth(), image.GetHeight());
        TextureFilter = TextureFilterEnum.Nearest;
        foreach (var s in sprites)
        {
            _regions.Add(
                (
                    s,
                    Region(
                        s,
                        new SpriteFrame
                        {
                            U = s.U,
                            V = s.V,
                            W = s.W,
                            H = s.H,
                        },
                        image.GetSize()
                    )
                )
            );
        }
    }

    // A frame in reference pixels as a rectangle of the texture; without a reference it is in texture pixels.
    public static Rect2 Region(Sprite s, SpriteFrame f, Vector2I texture)
    {
        var sx = s.ReferenceWidth > 0 ? (float)texture.X / s.ReferenceWidth : 1f;
        var sy = s.ReferenceHeight > 0 ? (float)texture.Y / s.ReferenceHeight : 1f;

        return new Rect2(f.U * sx, f.V * sy, f.W * sx, f.H * sy);
    }

    public override void _Draw()
    {
        DrawTexture(_texture, Vector2.Zero);
        foreach (var (s, r) in _regions)
        {
            DrawRect(r, new Color(1, 0.2f, 0.2f), false);
            DrawString(
                ThemeDB.FallbackFont,
                r.Position + new Vector2(1, 9),
                s.Name,
                HorizontalAlignment.Left,
                -1,
                8,
                new Color(1, 1, 0)
            );
        }
    }
}
