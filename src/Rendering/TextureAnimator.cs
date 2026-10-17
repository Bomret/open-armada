using System.Collections.Generic;
using Godot;
using OpenArmada.Assets;

namespace OpenArmada.Rendering;

/// <summary>
/// Runs a model's texture flipbooks: each frame it moves the texture coordinates of the animated
/// nodes' materials to the cell its animation (and the reference's time offset) says.
/// </summary>
public partial class TextureAnimator : Node
{
    readonly List<(List<ShaderMaterial> Materials, TextureAnimation Animation)> _animated = new();
    double _time;

    /// <summary>Runs <c>animation</c> on <c>materials</c>: each frame sets their <c>uv_offset</c> shader parameter.</summary>
    public void Add(List<ShaderMaterial> materials, TextureAnimation animation) =>
        _animated.Add((materials, animation));

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        _time += delta;
        foreach (var (materials, animation) in _animated)
        {
            var o = animation.UvOffset(_time);
            foreach (var m in materials)
            {
                m.SetShaderParameter("uv_offset", new Vector2(o.X, o.Y));
            }
        }
    }
}
