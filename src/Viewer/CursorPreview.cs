using System.Linq;
using Godot;
using OpenArmada.Assets;
using OpenArmada.Formats;

namespace OpenArmada.Viewer;

/// <summary>Cursor preview: the cursor image at 8x on a gray ground with its hotspot marked by a cyan frame.</summary>
public sealed class CursorPreview : KindPreview
{
    const int Scale = 8;

    /// <inheritdoc/>
    public override string KindName => CursorKind.KindName;

    /// <inheritdoc/>
    public override Control Create(GameFileSystem files, AssetKind kind, string name)
    {
        var cursor = (CursorImage)kind.Load(files, name);
        var image = Image.CreateEmpty(cursor.Width * Scale, cursor.Height * Scale, false, Image.Format.Rgba8);
        image.Fill(new Color(0.5f, 0.5f, 0.5f));
        for (var y = 0; y < cursor.Height; y++)
        {
            for (var x = 0; x < cursor.Width; x++)
            {
                var o = (y * cursor.Width + x) * 4;
                if (cursor.Rgba[o + 3] == 0)
                    continue;
                image.FillRect(
                    new Rect2I(x * Scale, y * Scale, Scale, Scale),
                    Color.Color8(cursor.Rgba[o], cursor.Rgba[o + 1], cursor.Rgba[o + 2])
                );
            }
        }

        // A hotspot outside the image (a note) is drawn at the nearest edge pixel.
        var (hx, hy) = (
            System.Math.Clamp(cursor.HotspotX, 0, cursor.Width - 1) * Scale,
            System.Math.Clamp(cursor.HotspotY, 0, cursor.Height - 1) * Scale
        );
        foreach (
            var edge in new[]
            {
                new Rect2I(hx, hy, Scale, 1),
                new Rect2I(hx, hy + Scale - 1, Scale, 1),
                new Rect2I(hx, hy, 1, Scale),
                new Rect2I(hx + Scale - 1, hy, 1, Scale),
            }
        )
        {
            image.FillRect(edge, Colors.Cyan);
        }

        return new ImagePane(
            $"{cursor.Width}x{cursor.Height}, hotspot at ({cursor.HotspotX}, {cursor.HotspotY}){string.Concat(cursor.Notes.Select(n => " - " + n))}",
            new[] { (name, image) }
        );
    }
}
