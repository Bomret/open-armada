using Godot;

namespace OpenArmada.Viewer;

/// <summary>One animated sprite: its frames in table order, looping, each frame for the animation's frame time.</summary>
public partial class AnimatedSpriteView : TextureRect
{
    readonly AtlasTexture _atlas;
    readonly Rect2[] _frames;
    readonly double _frameSeconds;
    double _time;

    /// <summary>
    /// Shows <c>regions</c> of <c>texture</c> in turn, each for <c>frameSeconds</c>, at least twice the size of the
    /// first region and with nearest filtering. A <c>frameSeconds</c> not above 0 shows the first region only. An
    /// empty <c>regions</c> throws <see cref="System.IndexOutOfRangeException"/>.
    /// </summary>
    public AnimatedSpriteView(Rect2[] regions, double frameSeconds, ImageTexture texture)
    {
        _frames = regions;
        _frameSeconds = frameSeconds;
        _atlas = new AtlasTexture { Atlas = texture, Region = _frames[0] };
        Texture = _atlas;
        CustomMinimumSize = _frames[0].Size * 2;
        ExpandMode = ExpandModeEnum.IgnoreSize;
        StretchMode = StretchModeEnum.Scale;
        TextureFilter = TextureFilterEnum.Nearest;
    }

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        _time += delta;
        if (_frameSeconds > 0)
        {
            _atlas.Region = _frames[(int)(_time / _frameSeconds) % _frames.Length];
        }
    }
}
