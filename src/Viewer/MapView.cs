using System.Linq;
using Godot;
using OpenArmada.Assets;

namespace OpenArmada.Viewer;

// The map from above, square: the overview image when the map has one, every object as a dot in its
// team's color, and the start positions as numbered rings. Positions go through MapBounds.ToImage:
// image right is +X and image top is maximum Z, so the map is not mirrored (models mirror Z for Godot,
// which puts north at -Z, the same top of the picture). Objects outside the picture's area are left out.
public partial class MapView : Control
{
    public static readonly Color[] TeamColors =
    {
        new(0.65f, 0.65f, 0.65f), new(1f, 0.2f, 0.2f), new(0.3f, 0.5f, 1f), new(0.2f, 0.9f, 0.3f), new(1f, 0.9f, 0.2f),
        new(1f, 0.3f, 1f), new(0.2f, 0.95f, 0.95f), new(1f, 0.6f, 0.1f), new(1f, 1f, 1f),
    };

    readonly MapAsset map;
    readonly ImageTexture? overview;
    readonly float size;
    readonly bool showObjects;

    // `overview` null draws a dark square instead; `showObjects` false draws the image alone.
    public MapView(MapAsset map, Image? overview, float size, bool showObjects)
    {
        this.map = map;
        this.overview = overview == null ? null : ImageTexture.CreateFromImage(overview);
        this.size = size;
        this.showObjects = showObjects;
        CustomMinimumSize = new Vector2(size, size);
        TextureFilter = TextureFilterEnum.Nearest;
    }

    public override void _Draw()
    {
        var rect = new Rect2(0, 0, size, size);
        if (overview != null) DrawTextureRect(overview, rect, false);
        else DrawRect(rect, new Color(0.08f, 0.08f, 0.1f));
        if (!showObjects) return;
        foreach (var o in map.Map.Objects)
        {
            var (col, row) = map.Map.Bounds.ToImage(o.Position.X, o.Position.Z, size);
            if (col < 0 || col > size || row < 0 || row > size) continue;
            DrawCircle(new Vector2(col, row), o.Team == 0 ? 1.5f : 3f, TeamColors[o.Team]);
        }
        var font = ThemeDB.FallbackFont;
        foreach (var s in map.Starts)
        {
            var (col, row) = map.Map.Bounds.ToImage(s.X, s.Z, size);
            DrawArc(new Vector2(col, row), 9, 0, Mathf.Tau, 24, Colors.White, 2f);
            DrawString(font, new Vector2(col + 11, row + 5), s.Number.ToString(), HorizontalAlignment.Left, -1, 14, Colors.White);
        }
    }

    // Objects left out because they lie outside the picture's area.
    public int Outside() => map.Map.Objects.Count(o =>
    {
        var (col, row) = map.Map.Bounds.ToImage(o.Position.X, o.Position.Z, size);
        return col < 0 || col > size || row < 0 || row > size;
    });
}
