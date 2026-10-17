using System;
using System.Globalization;
using System.Linq;
using System.Numerics;
using OpenArmada.Formats;

namespace OpenArmada.Sandbox;

// A sprite node's animation over time: whether its sprite is drawn (`draw`) and the colour that multiplies the
// line's colour (`colour`). Another field changes neither. `step` holds a keyframe until the next, `linear`
// interpolates. The period is the animation's length: between the last keyframe and the length the last
// keyframe holds; looping wraps at the length, playing once holds the last keyframe for good.
// stand-in: the install says only what the keyframes are; this reading of them is the engine's.
public sealed class NodeAnimation
{
    readonly SpriteAnimation _animation;
    readonly (double Time, Vector3 Value)[] _keys;

    public NodeAnimation(SpriteAnimation animation)
    {
        _animation = animation;
        // A keyframe that cannot be read is left out, so a shortfall of any size still yields a value.
        _keys = animation
            .Keyframes.Select(k => (Time: Number(k[0]), Value: Value(animation.Field, k[1..])))
            .Where(k => k.Time != null && k.Value != null)
            .Select(k => (k.Time!.Value, k.Value!.Value))
            .ToArray();
    }

    public (bool Draw, Vector3 Colour) At(double seconds, bool loop)
    {
        var value = Sample(Local(seconds, loop));
        return _animation.Field switch
        {
            "draw" => (value.X != 0, Vector3.One),
            "colour" => (true, value),
            _ => (true, Vector3.One),
        };
    }

    // The time within the period; a length of 0 or less is always past the end.
    double Local(double seconds, bool loop)
    {
        var length = _animation.Length;
        if (!(length > 0))
            return double.PositiveInfinity;
        if (!loop)
            return Math.Min(seconds, length);
        var t = seconds % length;
        return t < 0 ? t + length : t;
    }

    Vector3 Sample(double t)
    {
        if (_keys.Length == 0)
            return Vector3.One;
        var next = Array.FindIndex(_keys, k => k.Time > t);
        if (next == 0)
            return _keys[0].Value;
        if (next < 0)
            return _keys[^1].Value;
        var (t0, v0) = _keys[next - 1];
        var (t1, v1) = _keys[next];
        if (_animation.Method != "linear" || t1 <= t0)
            return v0;
        return Vector3.Lerp(v0, v1, (float)((t - t0) / (t1 - t0)));
    }

    // `draw` keyframes hold 0 or 1 (in X); `colour` keyframes hold "(r,g,b)".
    static Vector3? Value(string field, string[] tokens)
    {
        var parts = string.Concat(tokens).Trim('(', ')').Split(',').Select(Number).ToArray();
        if (parts.Any(p => p == null))
            return null;
        if (field == "colour")
        {
            if (parts.Length != 3)
                return null;
            return new Vector3((float)parts[0]!.Value, (float)parts[1]!.Value, (float)parts[2]!.Value);
        }
        if (parts.Length != 1)
            return null;
        return new Vector3((float)parts[0]!.Value, 0, 0);
    }

    static double? Number(string s) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && double.IsFinite(v)
            ? v
            : null;
}
