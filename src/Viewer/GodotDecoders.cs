using System.IO;
using Godot;
using OpenArmada.Assets;

namespace OpenArmada.Viewer;

// Godot's own runtime loaders for images and sounds (ADR 0002). Previews show what they return and
// the asset check runs them on every asset; a loader that rejects a file throws InvalidDataException.
public static class GodotDecoders
{
    public static Image Tga(byte[] data, string name)
    {
        var image = new Image();

        return Decode(image, image.LoadTgaFromBuffer(data), name);
    }

    public static Image Bmp(byte[] data, string name)
    {
        var image = new Image();

        return Decode(image, image.LoadBmpFromBuffer(data), name);
    }

    public static AudioStreamWav Wav(byte[] data, string name) =>
        AudioStreamWav.LoadFromBuffer(data) ?? throw new InvalidDataException($"{name}: Godot cannot decode it");

    // The decode step of the asset check: textures (base and every mip level), bitmaps, map overview images, and sounds.
    public static void Check(AssetKind kind, GameFileSystem files, string name)
    {
        switch (kind.Name)
        {
            case TextureKind.KindName:
                var texture = (TextureAsset)kind.Load(files, name);
                Tga(texture.Base.Data, texture.Base.Name);
                foreach (var mip in texture.Mips)
                {
                    Tga(mip.Data, mip.Name);
                }

                break;
            case BitmapKind.KindName:
                Bmp(files.ReadAllBytes(name), name);

                break;
            case MapKind.KindName:
                if (((MapAsset)kind.Load(files, name)).Overview is { } overview)
                {
                    Bmp(files.ReadAllBytes(overview), overview);
                }

                break;
            case SoundKind.KindName:
                Wav(files.ReadAllBytes(name), name);

                break;
        }
    }

    static Image Decode(Image image, Error error, string name) =>
        error == Error.Ok && !image.IsEmpty()
            ? image
            : throw new InvalidDataException($"{name}: Godot cannot decode it ({error})");
}
