using Godot;
using OpenArmada.Assets;
using OpenArmada.Formats;

namespace OpenArmada.Viewer;

// Cursor preview: the cursor image at 8x on a gray ground with its hotspot marked by a cyan frame.
public sealed class CursorPreview : KindPreview
{
    const int Scale = 8;

    public override string KindName => CursorKind.KindName;

    public override Control Create(GameFileSystem files, AssetKind kind, string name)
    {
        var cursor = (CursorImage)kind.Load(files, name);
        var image = Image.CreateEmpty(cursor.Width * Scale, cursor.Height * Scale, false, Image.Format.Rgba8);
        image.Fill(new Color(0.5f, 0.5f, 0.5f));
        for (var y = 0; y < cursor.Height; y++)
            for (var x = 0; x < cursor.Width; x++)
            {
                var o = (y * cursor.Width + x) * 4;
                if (cursor.Rgba[o + 3] == 0) continue;
                image.FillRect(new Rect2I(x * Scale, y * Scale, Scale, Scale), Color.Color8(cursor.Rgba[o], cursor.Rgba[o + 1], cursor.Rgba[o + 2]));
            }
        var hot = new Rect2I(cursor.HotspotX * Scale, cursor.HotspotY * Scale, Scale, Scale);
        for (var i = 0; i < Scale; i++)
            foreach (var (x, y) in new[] { (hot.Position.X + i, hot.Position.Y), (hot.Position.X + i, hot.End.Y - 1), (hot.Position.X, hot.Position.Y + i), (hot.End.X - 1, hot.Position.Y + i) })
                image.SetPixel(x, y, Colors.Cyan);
        return new ImagePane($"{cursor.Width}x{cursor.Height}, hotspot at ({cursor.HotspotX}, {cursor.HotspotY})", new[] { (name, image) });
    }
}
