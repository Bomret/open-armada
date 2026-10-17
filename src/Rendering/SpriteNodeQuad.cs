using System.Linq;
using Godot;
using OpenArmada.Sandbox;

namespace OpenArmada.Rendering;

/// <summary>
/// The quad a sprite node draws, a child of the model node it decorates, so it moves and turns with that node and is
/// hidden while the node is. It shows its sprite's flipbook and its node animation (drawn or not, colour) on its own
/// clock, started when it enters the scene and advanced by elapsed time, so the pace holds at any frame rate.
/// A copy of a built model (Duplicate) carries the resolved sprite node in metadata and takes its own material and
/// clock when it enters the scene, so copies of one template animate independently. Node animations loop unless
/// <see cref="PlayOnce"/> says otherwise; playing once holds the last keyframe.
/// </summary>
public partial class SpriteNodeQuad : MeshInstance3D
{
    const string Meta = "sprite_node";

    // The resolved sprite node and its texture, held by a Godot object so a Duplicate keeps the reference.
    sealed partial class Data : RefCounted
    {
        public SpriteNode Node = null!;
        public Texture2D Texture = null!;
    }

    bool _loop = true;
    Data? _data;
    StandardMaterial3D? _material;
    float _opacity;
    double _time;

    /// <summary>
    /// The resolved sprite node and its texture, for a draw that shows this quad's sprite node elsewhere. Read from
    /// the quad's metadata, so it holds on a quad that never entered the scene.
    /// </summary>
    public (SpriteNode Node, Texture2D Texture) Sprite
    {
        get
        {
            var data = GetMeta(Meta).As<Data>();

            return (data.Node, data.Texture);
        }
    }

    /// <summary>
    /// A quad of the sprite node's size showing <paramref name="texture"/> with the sprite's blend, not yet in the
    /// scene; it starts its clock when it enters one.
    /// stand-in: a sprite node that does not face the camera faces along its node, the quad's own facing.
    /// </summary>
    public static SpriteNodeQuad Create(SpriteNode node, Texture2D texture)
    {
        var material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoTexture = texture,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            BillboardMode = node.Billboard
                ? BaseMaterial3D.BillboardModeEnum.Enabled
                : BaseMaterial3D.BillboardModeEnum.Disabled,
            BillboardKeepScale = true,
        };
        SpriteMaterial.Blend(material, node.Sprite.Sprite.Material);
        var quad = new SpriteNodeQuad
        {
            Name = "sprite_node",
            Mesh = new QuadMesh { Size = new Vector2(node.Width, node.Height) },
            MaterialOverride = material,
        };
        quad.SetMeta(Meta, new Data { Node = node, Texture = texture });

        return quad;
    }

    /// <summary>
    /// Makes every sprite node under <paramref name="root"/> play its node animation once from when it enters the
    /// scene. Call it before the quads enter the scene.
    /// </summary>
    public static void PlayOnce(Node root)
    {
        foreach (var quad in root.FindChildren("*", "", true, false).OfType<SpriteNodeQuad>())
        {
            quad._loop = false;
        }
    }

    /// <summary>
    /// Sets <paramref name="material"/> to the sprite node's frame and colour <paramref name="seconds"/> after it
    /// appeared; <paramref name="opacity"/> is the blend's. Returns whether the sprite is drawn.
    /// </summary>
    public static bool Show(
        StandardMaterial3D material,
        SpriteNode node,
        Texture2D texture,
        float opacity,
        double seconds,
        bool loop
    )
    {
        var (draw, colour) = node.At(seconds, loop);
        material.AlbedoColor = new Color(colour.X, colour.Y, colour.Z, opacity);
        SpriteMaterial.ShowFrame(material, node.Sprite.Sprite, node.Sprite.FrameAt(seconds), texture);

        return draw;
    }

    /// <inheritdoc/>
    public override void _Ready()
    {
        _data = GetMeta(Meta).As<Data>();
        _material = (StandardMaterial3D)MaterialOverride.Duplicate();
        _opacity = _material.AlbedoColor.A;
        MaterialOverride = _material;
        Apply();
    }

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        _time += delta;
        Apply();
    }

    void Apply()
    {
        if (_data == null || _material == null)
            return;

        Visible = Show(_material, _data.Node, _data.Texture, _opacity, _time, _loop);
    }
}
