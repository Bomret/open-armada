using System.Collections.Generic;
using System.Linq;
using Godot;
using OpenArmada.Assets;
using OpenArmada.Formats;

namespace OpenArmada.Viewer;

// Sprite table preview: the sprites that animate, running, then each texture with the named regions
// of the table's sprites outlined on it. A texture the install lacks is named under its sprites.
public sealed class SpriteTablePreview : KindPreview
{
    public override string KindName => SpriteTableKind.KindName;

    public override Control Create(GameFileSystem files, AssetKind kind, string name)
    {
        var table = (SpriteTable)kind.Load(files, name);
        var textures = new TextureKind();
        var loaded = new Dictionary<string, (Image Image, ImageTexture Texture)?>(System.StringComparer.OrdinalIgnoreCase);
        foreach (var sprite in table.Sprites)
            if (!loaded.ContainsKey(sprite.Texture))
            {
                try
                {
                    var image = GodotDecoders.Tga(textures.LoadBase(files, sprite.Texture + ".tga").Data, sprite.Texture);
                    loaded[sprite.Texture] = (image, ImageTexture.CreateFromImage(image));
                }
                catch (System.Exception e) when (e is System.IO.IOException or System.IO.InvalidDataException or UnsupportedFormatException)
                {
                    loaded[sprite.Texture] = null;
                }
            }

        var box = new VBoxContainer();
        var animated = table.Sprites.Where(s => loaded[s.Texture] != null && table.Frames(s).Count > 1).ToList();
        box.AddChild(new Label { Text = $"{table.Sprites.Count} sprites, {animated.Count} animated, {loaded.Count} textures" });
        var flow = new HFlowContainer();
        foreach (var s in animated)
        {
            var frames = table.Frames(s);
            var cell = new VBoxContainer();
            cell.AddChild(new AnimatedSpriteView(s, frames, table.Animations[s.Anim!].FrameSeconds, loaded[s.Texture]!.Value.Texture));
            cell.AddChild(new Label { Text = $"{s.Name} ({frames.Count} frames)" });
            flow.AddChild(cell);
        }
        box.AddChild(flow);
        foreach (var group in table.Sprites.GroupBy(s => s.Texture, System.StringComparer.OrdinalIgnoreCase))
        {
            box.AddChild(new Label { Text = loaded[group.Key] == null ? $"{group.Key}: texture not found or unreadable ({string.Join(", ", group.Select(s => s.Name))})" : $"{group.Key}: {group.Count()} sprites" });
            if (loaded[group.Key] is { } t) box.AddChild(new SpriteSheetView(t.Image, group));
        }
        var scroll = new ScrollContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        scroll.AddChild(box);
        return scroll;
    }
}
