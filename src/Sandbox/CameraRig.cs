using System;
using System.Collections.Generic;
using System.Numerics;
using OpenArmada.Formats;

namespace OpenArmada.Sandbox;

// The RTS camera of the game, in the map's frame (not mirrored; the view mirrors Z for Godot). It looks at a
// point of the combat plane over the map; its height and rotation fix its place on a parabola:
// horizontal distance sqrt(focus * height), behind the target by the rotation. Every value comes from the
// game constants (INV-0003); one that is missing, text, of the wrong type or illegal uses the stand-in equal
// to the game's 1.2 value and is listed in Problems (INV-0002), so the pose is always finite.
public sealed class CameraRig
{
    readonly MapBounds _bounds;
    readonly double _focus,
        _initHeight,
        _minHeight,
        _scrollCoefficient,
        _border,
        _keyRate,
        _acceleration,
        _timeScale,
        _maxSpeed,
        _initSpeed,
        _targetY;
    double _height,
        _x,
        _z;

    public IReadOnlyList<string> Problems { get; }
    public double MaxHeight { get; }

    // The farthest the eye gets from the look-at point: at the maximum or the minimum height (whichever lies
    // farther from the target's height), on its parabola.
    public double MaxDistance => Math.Max(DistanceAt(MaxHeight), DistanceAt(_minHeight));

    // The middle of the combat plane (SHIP_COMBAT_FLOOR and SHIP_COMBAT_CEILING, or their stand-ins): the
    // height the camera looks at and the one plane move orders aim at.
    public double CombatPlaneHeight => _targetY;

    double DistanceAt(double height) => Math.Sqrt(_focus * height + (height - _targetY) * (height - _targetY));

    // The height change per mouse wheel notch (ZOOM_SCROLL_COEFFICIENT) and the rotation per dragged pixel.
    public double ZoomStep { get; }
    public double RotationPerPixel { get; }
    public double Rotation { get; private set; }

    // The current edge/key scroll speed: INITIAL_SCROLL_SPEED while idle, ramping to MAX_SCROLL_SPEED.
    public double ScrollSpeed { get; private set; }

    // `constants` is null when the install's file could not be read: every value is then a stand-in.
    public CameraRig(GameConstants? constants, MapBounds bounds)
    {
        var problems = new List<string>();
        Problems = problems;
        if (bounds.MinX > bounds.MaxX || bounds.MinZ > bounds.MaxZ)
        {
            problems.Add(
                $"the map bounds are inverted (X {bounds.MinX} to {bounds.MaxX}, Z {bounds.MinZ} to {bounds.MaxZ}): the camera swaps each inverted pair"
            );
            bounds = bounds with
            {
                MinX = Math.Min(bounds.MinX, bounds.MaxX),
                MaxX = Math.Max(bounds.MinX, bounds.MaxX),
                MinZ = Math.Min(bounds.MinZ, bounds.MaxZ),
                MaxZ = Math.Max(bounds.MinZ, bounds.MaxZ),
            };
        }
        _bounds = bounds;
        if (constants == null)
        {
            problems.Add("the game constants are unavailable: the camera uses stand-ins for every value");
        }

        double Get(string name, double standIn, Func<double, bool> legal, string rule)
        {
            if (constants == null)
                return standIn;
            if (!constants.TryNumber(name, out var value, out var problem))
            {
                problems.Add($"{problem}: the camera uses the stand-in {standIn}");
                return standIn;
            }
            if (!double.IsFinite(value) || !legal(value))
            {
                problems.Add($"constant {name} is {value}, {rule}: the camera uses the stand-in {standIn}");
                return standIn;
            }
            return value;
        }

        // stand-in: each fallback below equals the game's 1.2 value of the constant; the legal range is the
        // engine's own (the game states none).
        _focus = Get("OVERVIEW_PARABOLA_FOCUS", 200, v => v > 0, "not above 0");
        var min = Get("OVERVIEW_MIN_HEIGHT", 10, v => v > 0, "not above 0");
        var max = Get("OVERVIEW_MAX_HEIGHT", 700, v => v > 0, "not above 0");
        if (min > max)
        {
            problems.Add(
                $"constant OVERVIEW_MIN_HEIGHT {min} is above OVERVIEW_MAX_HEIGHT {max}: the camera uses the stand-ins 10 and 700"
            );
            (min, max) = (10, 700);
        }
        _minHeight = min;
        MaxHeight = max;
        var init = Get("OVERVIEW_INIT_HEIGHT", 450, v => v > 0, "not above 0");
        _initHeight = Math.Clamp(init, min, max);
        if (_initHeight != init)
        {
            problems.Add(
                $"constant OVERVIEW_INIT_HEIGHT {init} lies outside {min} to {max}: the camera starts at {_initHeight}"
            );
        }
        Rotation = Wrap(Get("OVERVIEW_INIT_ROTATION", 0, _ => true, ""));
        _scrollCoefficient = Get("SCROLL_COEFFICIENT", 90000, v => v > 0, "not above 0");
        ZoomStep = Get("ZOOM_SCROLL_COEFFICIENT", 0.1, v => v > 0, "not above 0");
        _keyRate = Get("KEYBOARD_SCROLL_RATE", 6, v => v > 0, "not above 0");
        RotationPerPixel = Get("ROTATION_SCROLL_COEFFICIENT", 0.01, v => v > 0, "not above 0");
        _border = Get("SCROLL_BORDER_WIDTH", 2, v => v >= 0, "below 0");
        _acceleration = Get("SCROLL_ACCELERATION", 2, v => v >= 0, "below 0");
        _timeScale = Get("TIME_SCALE", 1, v => v >= 0, "below 0");
        _maxSpeed = Get("MAX_SCROLL_SPEED", 10, v => v >= 0, "below 0");
        _initSpeed = Get("INITIAL_SCROLL_SPEED", 0.5, v => v >= 0, "below 0");
        var floor = Get("SHIP_COMBAT_FLOOR", 10, _ => true, "");
        var ceiling = Get("SHIP_COMBAT_CEILING", 67, _ => true, "");
        _targetY = (floor + ceiling) / 2;

        _height = _initHeight;
        ScrollSpeed = _initSpeed;
        _x = (bounds.MinX + bounds.MaxX) / 2;
        _z = (bounds.MinZ + bounds.MaxZ) / 2;
    }

    public (Vector3 Eye, Vector3 Target) Pose
    {
        get
        {
            var d = Math.Sqrt(_focus * _height);
            return (
                new Vector3((float)(_x - d * Math.Sin(Rotation)), (float)_height, (float)(_z - d * Math.Cos(Rotation))),
                new Vector3((float)_x, (float)_targetY, (float)_z)
            );
        }
    }

    // `dz` multiplies the height by (1 + dz), within the minimum and maximum.
    public void ChangeHeight(double dz) => _height = Math.Clamp(_height * (1 + dz), _minHeight, MaxHeight);

    public void Rotate(double radians) => Rotation = Wrap(Rotation + radians);

    // stand-in: the trackpad scales; the game had no trackpad. Godot reports a two-finger pan in trackpad points
    // times 0.03, about the wheel's line units: one unit counts as one wheel notch of height and as 1 / 0.03
    // dragged pixels of rotation.
    const double PanNotchesPerUnit = 1,
        PanPixelsPerUnit = 1 / 0.03;

    // A two-finger trackpad pan, `delta` as Godot reports it (negative Y where the wheel turns up, negative X
    // where the fingers move right under macOS natural scrolling): the vertical part changes the height as the
    // wheel does, the horizontal part rotates as a middle-button drag to the same side.
    public void TrackpadPan(Vector2 delta)
    {
        ChangeHeight(delta.Y * PanNotchesPerUnit * ZoomStep);
        Rotate(-delta.X * PanPixelsPerUnit * RotationPerPixel);
    }

    // A pinch of `factor` (above 1 spreading the fingers) divides the height by it. stand-in: the pinch-to-height
    // ratio is the engine's own.
    public void Pinch(double factor)
    {
        if (factor > 0)
        {
            ChangeHeight(1 / factor - 1);
        }
    }

    // `input` is (right, forward) on the screen; it turns with the rotation and shrinks with the distance of
    // the height from the initial height. The look-at point stays within the map's bounds.
    public void Pan(Vector2 input)
    {
        var scale = _scrollCoefficient / (Math.Abs(_initHeight - _height) + _initHeight);
        var (sin, cos) = (Math.Sin(Rotation), Math.Cos(Rotation));
        _x = Math.Clamp(_x + (input.X * cos + input.Y * sin) * scale, _bounds.MinX, _bounds.MaxX);
        _z = Math.Clamp(_z + (input.Y * cos - input.X * sin) * scale, _bounds.MinZ, _bounds.MaxZ);
    }

    // One frame of scrolling. `cursor` and `size` are in pixels (origin top left); `keys` is (right, forward) of
    // the arrow keys. Nothing scrolls without focus; the edges count only with the cursor inside the window.
    // The speed starts at the initial speed, grows by the time scale per second to the maximum, and falls back
    // when the scroll stops.
    public void Scroll(Vector2 cursor, Vector2 size, bool focused, Vector2 keys, double dt)
    {
        var direction = Vector2.Zero;
        if (focused)
        {
            // The arrow keys count with KEYBOARD_SCROLL_RATE as their strength, the edges with 0..1.
            direction = keys.Length() > 1 ? Vector2.Normalize(keys) : keys;
            direction *= (float)_keyRate;
            // The window's width and height count as inside: a cursor confined to the window reaches them.
            if (cursor.X >= 0 && cursor.Y >= 0 && cursor.X <= size.X && cursor.Y <= size.Y)
            {
                var edge = new Vector2(
                    Edge(size.X - 1 - cursor.X) - Edge(cursor.X),
                    Edge(cursor.Y) - Edge(size.Y - 1 - cursor.Y)
                );
                direction += edge.Length() > 1 ? Vector2.Normalize(edge) : edge;
            }
        }
        if (direction == Vector2.Zero)
        {
            ScrollSpeed = _initSpeed;
            return;
        }
        Pan(direction * (float)(_acceleration * ScrollSpeed * dt));
        ScrollSpeed = Math.Min(_maxSpeed, ScrollSpeed + _timeScale * dt);
    }

    // The cursor the view passes to Scroll: the mouse position while the window holds the mouse or confines the
    // cursor, else a negative position (outside). A confined cursor is inside even when the window reports the
    // mouse exited: on macOS the right and bottom boundaries it reaches lie outside the view's tracking area.
    public static Vector2 EdgeCursor(Vector2 mouse, bool mouseInWindow, bool confined) =>
        mouseInWindow || confined ? mouse : new Vector2(-1, -1);

    // Strength 0..1 of a cursor `distance` pixels from an edge: 1 on the edge pixel, 0 at the border width.
    float Edge(float distance) => _border > 0 ? (float)Math.Clamp((_border - distance) / _border, 0, 1) : 0;

    static double Wrap(double radians)
    {
        var r = radians % (2 * Math.PI);
        if (r < 0)
        {
            r += 2 * Math.PI;
        }
        return r >= 2 * Math.PI ? 0 : r;
    }
}
