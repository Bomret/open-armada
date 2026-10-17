using System.Collections.Generic;
using System.Linq;
using Godot;
using OpenArmada.Assets;
using OpenArmada.Diagnostics;
using OpenArmada.Formats;

namespace OpenArmada.Viewer;

/// <summary>
/// Interface configuration preview: the layout of its elements (with the sprites its race's tables name
/// for them) beside the parsed entries and notes.
/// </summary>
public sealed class InterfaceConfigPreview : KindPreview
{
    /// <inheritdoc/>
    public override string KindName => InterfaceConfigKind.KindName;

    /// <inheritdoc/>
    public override Control Create(GameFileSystem files, AssetKind kind, string name)
    {
        var cfg = (InterfaceConfig)kind.Load(files, name);
        var sprites = SpritesOf(files, cfg.Name);
        var textures = new Dictionary<string, (Image Image, ImageTexture Texture)?>(
            System.StringComparer.OrdinalIgnoreCase
        );
        var items = new List<(LayoutElement, Texture2D?, Rect2)>();
        foreach (var e in cfg.Elements)
        {
            Texture2D? texture = null;
            var region = new Rect2();
            if (e.Sprite != null && sprites.TryGetValue(e.Sprite, out var s))
            {
                if (!textures.TryGetValue(s.Texture, out var loaded))
                {
                    textures[s.Texture] = loaded = TryLoadTexture(files, s.Texture, name);
                }

                if (loaded is { } l)
                {
                    (texture, region) = (
                        l.Texture,
                        SpriteSheetView.Region(
                            s,
                            new SpriteFrame
                            {
                                U = s.U,
                                V = s.V,
                                W = s.W,
                                H = s.H,
                            },
                            l.Image.GetSize()
                        )
                    );
                }
            }

            items.Add((e, texture, region));
        }

        var entries = string.Join(
            "\n",
            cfg.Entries.Select(e =>
                $"{e.File}:{e.Line}  {e.Key} = {(e.IsText ? "\"" + e.Values[0] + "\"" : string.Join(" ", e.Values))}"
            )
        );
        var text = NotesBlock(name, cfg.Notes) + entries;
        var row = new HBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(660, 0) };
        scroll.AddChild(new InterfaceLayoutView(items));
        row.AddChild(scroll);
        row.AddChild(ReadOnlyText(text, true));
        var box = new VBoxContainer();
        box.AddChild(
            new Label
            {
                Text =
                    $"{cfg.Entries.Count} entries, {cfg.Elements.Count} positioned elements, {items.Count(i => i.Item2 != null)} with sprites, {cfg.Notes.Count} notes",
            }
        );
        var failed = textures.Where(t => t.Value == null).Select(t => t.Key).ToList();
        if (failed.Count > 0)
        {
            box.AddChild(
                new Label
                {
                    Text =
                        $"{failed.Count} textures not found or unreadable, their elements show no sprite: {string.Join(", ", failed)}",
                }
            );
        }

        box.AddChild(row);

        return box;
    }

    // The texture's image and Godot texture, or null when its file is missing, unreadable or in a format not
    // decoded; a failed texture is logged once per run, and once per later asset that uses it (ViewerLog.TextureFailed).
    static (Image Image, ImageTexture Texture)? TryLoadTexture(GameFileSystem files, string texture, string asset)
    {
        try
        {
            var image = GodotDecoders.Tga(new TextureKind().LoadBase(files, texture + ".tga").Data, texture);

            return (image, ImageTexture.CreateFromImage(image));
        }
        catch (System.Exception ex)
            when (ex is System.IO.IOException or System.IO.InvalidDataException or UnsupportedFormatException)
        {
            ViewerLog.TextureFailed(Area.Browser, texture, ex, asset);

            return null;
        }
    }

    // Sprites by name from the tables of the cfg's race (Sprites/gui_<first letters of the cfg>*.spr, so
    // gui_fed.cfg reads gui_federation.spr) and the shared gui_global.spr; a table that fails to load adds none.
    static Dictionary<string, Sprite> SpritesOf(GameFileSystem files, string cfgName)
    {
        var stem = System.IO.Path.GetFileNameWithoutExtension(cfgName);
        var kind = new SpriteTableKind();
        var result = new Dictionary<string, Sprite>(System.StringComparer.OrdinalIgnoreCase);
        foreach (var table in kind.Enumerate(files))
        {
            var file = System.IO.Path.GetFileName(table);
            if (
                !file.StartsWith(stem, System.StringComparison.OrdinalIgnoreCase)
                && !file.Equals("gui_global.spr", System.StringComparison.OrdinalIgnoreCase)
            )
                continue;
            try
            {
                foreach (var s in ((SpriteTable)kind.Load(files, table)).Sprites)
                {
                    result.TryAdd(s.Name, s);
                }
            }
            catch (System.Exception ex)
                when (ex is System.IO.IOException or System.IO.InvalidDataException or UnsupportedFormatException) { }
        }

        return result;
    }
}
