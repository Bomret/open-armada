using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using OpenArmada.Sandbox;

namespace OpenArmada.Viewer;

/// <summary>
/// Runs the input steps of a play script by sending Godot input events through the sandbox's own handlers
/// (ADR 0006). A step aimed at a unit or a point sends its events at the projected screen point, in viewport
/// coordinates, so the input handlers see what a player's click gives at any display scale. Every check runs
/// before the first event: a step that fails sends none. Mouse events enter the viewport directly in viewport
/// coordinates; key events go through the Input singleton, so polled keys see them until the release.
/// </summary>
public sealed class PlayInput(
    Node host,
    UnitNames names,
    UnitSelection selection,
    IReadOnlyDictionary<PlacedObject, Node3D> nodes,
    float planeHeight,
    string outputFolder
)
{
    // stand-in: a drag around named units reaches this many pixels past the outermost unit points.
    const float BoxMargin = 12;

    readonly HashSet<Key> _held = [];

    /// <summary>Runs one input step; returns its problem, or null. The caller maps a throw to a problem too.</summary>
    public string? Run(PlayStep step)
    {
        switch (step)
        {
            case SelectStep select:
                return Click(select.Unit, MouseButton.Left);
            case AttackStep attack:
                return Click(attack.Unit, MouseButton.Right);
            case MoveStep move:
                return Move(move);
            case BoxRectStep box:
                return Drag(new Vector2(box.Left, box.Top), new Vector2(box.Right, box.Bottom));
            case BoxUnitsStep box:
                return BoxAround(box.Units);
            case PressStep press:
                return SendKey(press.Key, true) ?? SendKey(press.Key, false);
            case HoldStep hold:
                return SendKey(hold.Key, true);
            case ReleaseStep release:
                return SendKey(release.Key, false);
            case ScrollStep scroll:
                return Scroll(scroll);
            case TurnStep turn:
                return Turn(turn.Pixels);
            case ShotStep shot:
                return Shot(shot.Name);
            default:
                return $"step '{step.GetType().Name}' is no input step";
        }
    }

    Viewport View => host.GetViewport();

    Vector2 Size => View.GetVisibleRect().Size;

    // The viewport coordinates a step aimed at the unit sends its events to, or the problem.
    (Vector2? Point, string? Problem) Aim(string name, out PlacedObject? unit)
    {
        string? problem;
        (unit, problem) = names.Resolve(name);
        if (unit == null)
            return (null, problem);
        if (!nodes.TryGetValue(unit, out var node))
            return (null, $"unit '{name}' was destroyed and removed");

        return AimAt(name, node.GlobalPosition);
    }

    (Vector2? Point, string? Problem) AimAt(string label, Vector3 world)
    {
        if (View.GetCamera3D() is not { } camera)
            return (null, "the scene has no camera");
        var (point, problem) = PlayTarget.Of(
            label,
            ToNumerics(camera.UnprojectPosition(world)),
            camera.IsPositionBehind(world),
            ToNumerics(Size)
        );

        return (point is { } p ? new Vector2(p.X, p.Y) : null, problem);
    }

    // The name of the unit a click at the point would pick; null for none.
    string? PickedAt(Vector2 point) => selection.UnitAt(point) is { } unit ? names.Name(unit.Object) : null;

    string? Click(string unit, MouseButton button)
    {
        var (point, problem) = Aim(unit, out _);
        if (point is not { } at)
            return problem;
        if (PlayTarget.PickProblem(unit, PickedAt(at)) is { } wrongPick)
            return wrongPick;
        Send(Button(button, at, true));
        Send(Button(button, at, false));

        return null;
    }

    string? Move(MoveStep move)
    {
        var label =
            $"move {move.X.ToString(System.Globalization.CultureInfo.InvariantCulture)} {move.Z.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
        var (point, problem) = AimAt(label, new Vector3((float)move.X, planeHeight, (float)move.Z));
        if (point is not { } at)
            return problem;
        if (PickedAt(at) is { } picked)
            return $"the click for '{label}' would pick '{picked}'";
        Send(Button(MouseButton.Right, at, true));
        Send(Button(MouseButton.Right, at, false));

        return null;
    }

    string? BoxAround(IReadOnlyList<string> units)
    {
        var points = new List<Vector2>();
        var named = new HashSet<PlacedObject>();
        foreach (var name in units)
        {
            var (point, problem) = Aim(name, out var unit);
            if (point is not { } at)
                return problem;
            points.Add(at);
            named.Add(unit!);
        }

        var margin = new Vector2(BoxMargin, BoxMargin);
        var from =
            points.Aggregate(Vector2.Inf, (a, b) => new Vector2(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y))) - margin;
        var to = points.Aggregate(-Vector2.Inf, (a, b) => new Vector2(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y))) + margin;
        var max = Size - Vector2.One;
        from = from.Clamp(Vector2.Zero, max);
        to = to.Clamp(Vector2.Zero, max);
        var camera = View.GetCamera3D()!;
        var inside = BoxSelect
            .Inside(
                selection.Units.Select(u =>
                {
                    var position = nodes[u.Object].GlobalPosition;

                    return (
                        u.Object,
                        ToNumerics(camera.UnprojectPosition(position)),
                        !camera.IsPositionBehind(position)
                    );
                }),
                ToNumerics(from),
                ToNumerics(to)
            )
            .ToHashSet();
        if (!inside.SetEquals(named))
        {
            var others = inside.Except(named).Select(names.Name).ToList();

            return others.Count > 0
                ? $"a drag around the named units would also select {string.Join(", ", others.Select(o => $"'{o}'"))}"
                : "a drag around the named units would not select them all";
        }

        return Drag(from, to);
    }

    string? Drag(Vector2 from, Vector2 to)
    {
        Send(Button(MouseButton.Left, from, true));
        Send(Motion(from.Lerp(to, 0.5f), (to - from) / 2, MouseButtonMask.Left));
        Send(Motion(to, (to - from) / 2, MouseButtonMask.Left));
        Send(Button(MouseButton.Left, to, false));

        return null;
    }

    string? SendKey(string name, bool pressed)
    {
        var key = OS.FindKeycodeFromString(name);
        if (key == Key.None)
            return $"'{name}' is no key name";
        if (pressed)
            _held.Add(key);
        else
            _held.Remove(key);
        Press(key, pressed);

        return null;
    }

    /// <summary>Presses again each held key that the window released, so a hold lasts as long as its step says.</summary>
    public void KeepHeld()
    {
        foreach (var key in _held.Where(k => !Input.IsKeyPressed(k)))
        {
            Press(key, true);
            Input.FlushBufferedEvents();
        }
    }

    static void Press(Key key, bool pressed) =>
        Input.ParseInputEvent(
            new InputEventKey
            {
                Keycode = key,
                PhysicalKeycode = key,
                Pressed = pressed,
            }
        );

    string? Scroll(ScrollStep scroll)
    {
        var center = Size / 2;
        var wheel = scroll.Up ? MouseButton.WheelUp : MouseButton.WheelDown;
        for (var i = 0; i < scroll.Count; i++)
        {
            Send(Button(wheel, center, true));
            Send(Button(wheel, center, false));
        }

        return null;
    }

    string? Turn(double pixels)
    {
        var center = Size / 2;
        var by = new Vector2((float)pixels, 0);
        Send(Button(MouseButton.Middle, center, true));
        Send(Motion(center + by, by, MouseButtonMask.Middle));
        Send(Button(MouseButton.Middle, center + by, false));

        return null;
    }

    // The viewport's last drawn frame: a step that changes the view takes effect in the frame after it, so a wait
    // comes before the shot.
    string? Shot(string name)
    {
        var file = Path.Combine(outputFolder, name + ".png");
        try
        {
            Directory.CreateDirectory(outputFolder);
            var image = View.GetTexture().GetImage();
            if (image == null)
                return $"cannot write '{file}': the viewport has no image";
            var saved = image.SavePng(file);

            return saved == Error.Ok ? null : $"cannot write '{file}': {saved}";
        }
        catch (Exception e) // a file that cannot be written is a problem, whatever the reason (INV-0007)
        {
            return $"cannot write '{file}': {e.Message}";
        }
    }

    void Send(InputEvent e) => View.PushInput(e, true);

    static InputEventMouseButton Button(MouseButton button, Vector2 at, bool pressed) =>
        new()
        {
            ButtonIndex = button,
            Pressed = pressed,
            Position = at,
            GlobalPosition = at,
            ButtonMask = pressed ? Mask(button) : 0,
        };

    static InputEventMouseMotion Motion(Vector2 at, Vector2 relative, MouseButtonMask mask) =>
        new()
        {
            Position = at,
            GlobalPosition = at,
            Relative = relative,
            ButtonMask = mask,
        };

    static MouseButtonMask Mask(MouseButton button) =>
        button switch
        {
            MouseButton.Left => MouseButtonMask.Left,
            MouseButton.Right => MouseButtonMask.Right,
            MouseButton.Middle => MouseButtonMask.Middle,
            _ => 0,
        };

    static System.Numerics.Vector2 ToNumerics(Vector2 v) => new(v.X, v.Y);
}
