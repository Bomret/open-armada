using Godot;
using OpenArmada.Assets;
using OpenArmada.Formats;
using OpenArmada.Viewer;

namespace OpenArmada.Rendering;

// How a sprite is drawn, for shot sprites and sprite nodes alike.
public static class SpriteMaterial
{
    // The sprite's draw state from its render type, as the models' are (ModelRules).
    // stand-in: a render type the engine does not know is drawn alpha blended.
    public static void Blend(StandardMaterial3D material, string? renderType)
    {
        var blend = (renderType == null ? null : ModelRules.Blend(renderType)) ?? new BlendState(BlendMode.Alpha, 1f);
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

    // Shows one frame of the sprite: the material's texture coordinates cover that frame's region of the texture.
    public static void ShowFrame(StandardMaterial3D material, Sprite sprite, SpriteFrame frame, Texture2D texture)
    {
        var size = new Vector2I(texture.GetWidth(), texture.GetHeight());
        var region = SpriteSheetView.Region(sprite, frame, size);
        material.Uv1Scale = new Vector3(region.Size.X / size.X, region.Size.Y / size.Y, 1);
        material.Uv1Offset = new Vector3(region.Position.X / size.X, region.Position.Y / size.Y, 0);
    }
}
