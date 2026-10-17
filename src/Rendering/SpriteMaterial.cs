using Godot;
using OpenArmada.Assets;
using OpenArmada.Formats;
using OpenArmada.Viewer;

namespace OpenArmada.Rendering;

/// <summary>How a sprite is drawn, for shot sprites and sprite nodes alike.</summary>
public static class SpriteMaterial
{
    /// <summary>
    /// Sets the draw state of <paramref name="material"/> from the sprite's render type, as the models' are
    /// (ModelRules): transparency, blend mode, opacity in the albedo alpha (the albedo color turns white), and
    /// texture filter.
    /// stand-in: a render type the engine does not know, or none, is drawn alpha blended.
    /// </summary>
    public static void Blend(StandardMaterial3D material, string? renderType)
    {
        var blend =
            (renderType == null ? null : ModelRules.Blend(renderType))
            ?? new BlendState { Mode = BlendMode.Alpha, Opacity = 1f };
        material.Transparency = blend.Mode switch
        {
            BlendMode.Opaque => BaseMaterial3D.TransparencyEnum.Disabled,
            BlendMode.Cutout => BaseMaterial3D.TransparencyEnum.AlphaScissor,
            _ => BaseMaterial3D.TransparencyEnum.Alpha,
        };
        material.AlphaScissorThreshold = 128f / 255f;
        material.BlendMode =
            blend.Mode == BlendMode.Add ? BaseMaterial3D.BlendModeEnum.Add : BaseMaterial3D.BlendModeEnum.Mix;
        material.AlbedoColor = new Color(1, 1, 1, blend.Opacity);
        material.TextureFilter = blend.Nearest
            ? BaseMaterial3D.TextureFilterEnum.Nearest
            : BaseMaterial3D.TextureFilterEnum.Linear;
    }

    /// <summary>
    /// Shows one frame of the sprite: sets the texture scale and offset of <paramref name="material"/> to cover that
    /// frame's region of <paramref name="texture"/>.
    /// </summary>
    public static void ShowFrame(StandardMaterial3D material, Sprite sprite, SpriteFrame frame, Texture2D texture)
    {
        var size = new Vector2I(texture.GetWidth(), texture.GetHeight());
        var region = SpriteSheetView.Region(sprite, frame, size);
        material.Uv1Scale = new Vector3(region.Size.X / size.X, region.Size.Y / size.Y, 1);
        material.Uv1Offset = new Vector3(region.Position.X / size.X, region.Position.Y / size.Y, 0);
    }
}
