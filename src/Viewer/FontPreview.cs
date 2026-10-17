using Godot;
using OpenArmada.Assets;
using OpenArmada.Formats;

namespace OpenArmada.Viewer;

// Font preview: the glyph sheet, 16 characters per row in code order, ink dark on light, shown at 3x.
public sealed class FontPreview : KindPreview
{
    const int Scale = 3;

    public override string KindName => FontKind.KindName;

    public override Control Create(GameFileSystem files, AssetKind kind, string name)
    {
        var font = (BitmapFont)kind.Load(files, name);
        var cellW = System.Linq.Enumerable.Max(font.Glyphs, g => g.Width);
        var cellH = font.Height;
        var image = Image.CreateEmpty(16 * cellW, 16 * cellH, false, Image.Format.Rgba8);
        image.Fill(new Color(0.92f, 0.92f, 0.85f));
        for (var code = 0; code < font.Glyphs.Count; code++)
        {
            var (ox, oy) = (code % 16 * cellW, code / 16 * cellH);
            var g = font.Glyphs[code];
            for (var y = 0; y < font.Height; y++)
            {
                for (var x = 0; x < g.Width; x++)
                {
                    if (g.Pixels[y * g.Width + x] != font.Transparent)
                    {
                        image.SetPixel(ox + x, oy + y, new Color(0.05f, 0.05f, 0.2f));
                    }
                }
            }
        }

        image.Resize(image.GetWidth() * Scale, image.GetHeight() * Scale, Image.Interpolation.Nearest);
        var drawn = System.Linq.Enumerable.Count(font.Glyphs, g => g.Width > 0);

        return new ImagePane(
            $"{font.Glyphs.Count} characters ({drawn} drawn), {font.Height} rows high, widest {cellW} columns",
            new[] { (name, image) }
        );
    }
}
