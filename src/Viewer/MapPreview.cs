using System.Linq;
using Godot;
using OpenArmada.Assets;

namespace OpenArmada.Viewer;

// Map preview: the overview image alone beside the same image with every object (by team) and the
// start positions drawn over it, so the two can be compared; a legend lists the teams and counts.
public sealed class MapPreview : KindPreview
{
    public override string KindName => MapKind.KindName;

    public override Control Create(GameFileSystem files, AssetKind kind, string name)
    {
        var map = (MapAsset)kind.Load(files, name);
        var image = map.Overview == null ? null : GodotDecoders.Bmp(files.ReadAllBytes(map.Overview), map.Overview);
        var over = new MapView(map, image, true);
        var outside = over.Outside();

        var legend = new VBoxContainer();
        legend.AddChild(new Label
        {
            Text = $"{map.Map.Description}: {map.Map.Objects.Count} objects ({outside} outside the picture), {map.Starts.Count} start position(s), "
                + $"{map.Map.Paths.Count} path(s), overview {(image == null ? "none" : image.GetWidth() + "x" + image.GetHeight())}",
        });
        foreach (var g in map.Map.Objects.GroupBy(o => o.Team).OrderBy(g => g.Key))
        {
            var row = new HBoxContainer();
            row.AddChild(new ColorRect { Color = MapView.TeamColors[g.Key], CustomMinimumSize = new Vector2(14, 14) });
            row.AddChild(new Label { Text = $"team {g.Key}: {g.Count()} objects" });
            legend.AddChild(row);
        }

        var pictures = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        pictures.AddChild(new MapView(map, image, false));
        pictures.AddChild(over);
        var box = new VBoxContainer();
        box.AddChild(legend);
        box.AddChild(pictures);
        return box;
    }
}
