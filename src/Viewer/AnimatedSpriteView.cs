using Godot;

namespace OpenArmada.Viewer;

// One animated sprite: its frames in table order, looping, each frame for the animation's frame time.
public partial class AnimatedSpriteView : TextureRect
{
    readonly AtlasTexture _atlas;
    readonly Rect2[] _frames;
    readonly double _frameSeconds;
    double _time;

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

    public override void _Process(double delta)
    {
        _time += delta;
        if (_frameSeconds > 0)
            _atlas.Region = _frames[(int)(_time / _frameSeconds) % _frames.Length];
    }
}
