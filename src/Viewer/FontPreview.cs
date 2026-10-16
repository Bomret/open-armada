using Godot;
using OpenArmada.Assets;
using OpenArmada.Formats;

namespace OpenArmada.Viewer;

// Font preview: the glyph sheet, 16 characters per row in code order, ink dark on light, shown at 3x with the
// cell grid lines.
public sealed class FontPreview : KindPreview
{
    const int Scale = 3;

    public override string KindName => FontKind.KindName;

    public override Control Create(GameFileSystem files, AssetKind kind, string name)
    {
        var font = (BitmapFont)kind.Load(files, name);
        var cellW = 1 + System.Linq.Enumerable.Max(font.Glyphs, g => g.Width);
        var cellH = font.Height + 1;
        var image = Image.CreateEmpty(16 * cellW, 16 * cellH, false, Image.Format.Rgba8);
        image.Fill(new Color(0.92f, 0.92f, 0.85f));
        for (var code = 0; code < font.Glyphs.Count; code++)
        {
            var (ox, oy) = (code % 16 * cellW, code / 16 * cellH);
            for (var x = 0; x < cellW; x++) image.SetPixel(ox + x, oy + cellH - 1, new Color(0.7f, 0.8f, 0.9f));
            for (var y = 0; y < cellH; y++) image.SetPixel(ox + cellW - 1, oy + y, new Color(0.7f, 0.8f, 0.9f));
            var g = font.Glyphs[code];
            for (var y = 0; y < font.Height; y++)
                for (var x = 0; x < g.Width; x++)
                    if (g.Pixels[y * g.Width + x] != font.Transparent) image.SetPixel(ox + x, oy + y, new Color(0.05f, 0.05f, 0.2f));
        }
        image.Resize(image.GetWidth() * Scale, image.GetHeight() * Scale, Image.Interpolation.Nearest);
        var drawn = System.Linq.Enumerable.Count(font.Glyphs, g => g.Width > 0);
        return new ImagePane($"{font.Glyphs.Count} characters ({drawn} drawn), {font.Height} rows high, widest {cellW - 1} columns", new[] { (name, image) });
    }
}
