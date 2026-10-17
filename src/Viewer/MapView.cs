using System.Linq;
using Godot;
using OpenArmada.Assets;
using OpenArmada.Formats;

namespace OpenArmada.Viewer;

// The map from above, square: the overview image when the map has one, every object as a dot in its
// team's color, and the start positions as numbered rings. Positions go through MapBounds.ToImage:
// image right is +X and image top is maximum Z, so the map is not mirrored (models mirror Z for Godot,
// which puts north at -Z, the same top of the picture). Objects outside the picture's area are left out.
public partial class MapView : Control
{
    public static readonly Color[] TeamColors =
    {
        new(0.65f, 0.65f, 0.65f),
        new(1f, 0.2f, 0.2f),
        new(0.3f, 0.5f, 1f),
        new(0.2f, 0.9f, 0.3f),
        new(1f, 0.9f, 0.2f),
        new(1f, 0.3f, 1f),
        new(0.2f, 0.95f, 0.95f),
        new(1f, 0.6f, 0.1f),
        new(1f, 1f, 1f),
    };

    readonly MapAsset map;
    readonly ImageTexture? overview;
    readonly bool showObjects;

    // The square fills the largest square of the control's area, so the picture shrinks with its pane.
    // `overview` null draws a dark square instead; `showObjects` false draws the image alone.
    public MapView(MapAsset map, Image? overview, bool showObjects)
    {
        this.map = map;
        this.overview = overview == null ? null : ImageTexture.CreateFromImage(overview);
        this.showObjects = showObjects;
        CustomMinimumSize = new Vector2(160, 160);
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        SizeFlagsVertical = SizeFlags.ExpandFill;
        TextureFilter = TextureFilterEnum.Nearest;
    }

    public override void _Draw()
    {
        var size = Mathf.Min(Size.X, Size.Y);
        var rect = new Rect2(0, 0, size, size);
        if (overview != null)
        {
            DrawTextureRect(overview, rect, false);
        }
        else
        {
            DrawRect(rect, new Color(0.08f, 0.08f, 0.1f));
        }
        if (!showObjects)
            return;
        foreach (var o in map.Map.Objects)
        {
            if (TryPlace(o, size, out var p))
            {
                DrawCircle(p, o.Team == 0 ? 1.5f : 3f, TeamColors[o.Team]);
            }
        }
        var font = ThemeDB.FallbackFont;
        foreach (var s in map.Starts)
        {
            var (x, z) = map.Map.Bounds.FromStartCell(s.Cell);
            var (col, row) = map.Map.Bounds.ToImage(x, z, size);
            // The ring (radius 9, line 2 wide, so 10 px out) is moved in to stay whole inside the square.
            col = Mathf.Clamp(col, 10, size - 10);
            row = Mathf.Clamp(row, 10, size - 10);
            DrawArc(new Vector2(col, row), 9, 0, Mathf.Tau, 24, Colors.White, 2f);
            var number = s.Number.ToString();
            var width = font.GetStringSize(number, HorizontalAlignment.Left, -1, 14).X;
            // The number sits right of the ring, or left of it when that would cross the square's edge.
            var labelX = col + 11 + width > size ? col - 11 - width : col + 11;
            // Likewise the baseline is clamped so the digits stay inside the square at the top and bottom edges.
            var baseline = Mathf.Clamp(row + 5, font.GetAscent(14), size - font.GetDescent(14));
            DrawString(font, new Vector2(labelX, baseline), number, HorizontalAlignment.Left, -1, 14, Colors.White);
        }
    }

    // Objects left out because they lie outside the picture's area (the same at every scale).
    public int Outside() => map.Map.Objects.Count(o => !TryPlace(o, 1, out _));

    // The picture point of an object in a square of `size` pixels; false when it lies outside the square.
    bool TryPlace(MapObject o, float size, out Vector2 p)
    {
        var (col, row) = map.Map.Bounds.ToImage(o.Position.X, o.Position.Z, size);
        p = new Vector2(col, row);
        return col >= 0 && col <= size && row >= 0 && row <= size;
    }
}
