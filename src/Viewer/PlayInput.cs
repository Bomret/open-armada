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
/// coordinates; key events go through the Input singleton, so polled keys see them until the release, and are flushed
/// at once, so key and mouse steps of one frame reach the handlers in file order.
/// </summary>
public sealed class PlayInput(
    Node host,
    UnitNames names,
    UnitSelection selection,
    CombatWorld world,
    IReadOnlyDictionary<PlacedObject, Node3D> nodes,
    float planeHeight,
    string outputFolder
)
{
    // stand-in: a drag around named units reaches this many pixels past the outermost unit points.
    const float BoxMargin = 12;

    readonly HashSet<Key> _held = [];

    /// <summary>Runs one input step; returns its problem, or null. The caller maps a throw to a problem naming the line.</summary>
    public string? Run(PlayStep step)
    {
        switch (step)
        {
            case SelectStep select:
                return Click(select.Unit, MouseButton.Left);
            case AttackStep attack:
                return Click(attack.Unit, MouseButton.Right, attack: true);
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
                return Shot(shot);
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

    string? Click(string unit, MouseButton button, bool attack = false)
    {
        var (point, problem) = Aim(unit, out var target);
        if (point is not { } at)
            return problem;
        if (PlayTarget.PickProblem(unit, PickedAt(at)) is { } wrongPick)
            return wrongPick;
        if (attack)
        {
            // the current teams (INV-0012); a unit the world has lost is hostile to none
            var hostile = selection
                .Selected.Select(s =>
                    world.State(s.Object) is { } shooter
                    && world.State(target!) is { } victim
                    && CombatRules.Hostile(shooter.Team, victim.Team)
                )
                .ToList();
            if (PlayTarget.AttackProblem(unit, hostile) is { } notAttack)
                return notAttack;
        }

        ClickAt(button, at);

        return null;
    }

    void ClickAt(MouseButton button, Vector2 at)
    {
        Send(Button(button, at, true));
        Send(Button(button, at, false));
    }

    string? Move(MoveStep move)
    {
        var label = FormattableString.Invariant($"move {move.X} {move.Z}");
        var (point, problem) = AimAt(label, new Vector3((float)move.X, planeHeight, (float)move.Z));
        if (point is not { } at)
            return problem;
        if (PickedAt(at) is { } picked)
            return $"the click for '{label}' would pick '{picked}'";
        ClickAt(MouseButton.Right, at);

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
        var from = new Vector2(points.Min(p => p.X), points.Min(p => p.Y)) - margin;
        var to = new Vector2(points.Max(p => p.X), points.Max(p => p.Y)) + margin;
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
        var problem = KeyMask.Problem(name, n => (long)OS.FindKeycodeFromString(n));
        if (problem is not null)
            return problem;
        var key = OS.FindKeycodeFromString(name);
        if (pressed)
            _held.Add(key);
        else
            _held.Remove(key);
        Press(key, pressed);
        // Flushed at once, as a mouse step's PushInput delivers at once, so a key step and a mouse step on one frame
        // reach the handlers in file order.
        Input.FlushBufferedEvents();

        return null;
    }

    /// <summary>
    /// Presses again each held key that the window released, so a hold lasts as long as its step says. After a focus
    /// loss that is a press in every frame until the key's release. The press is an echo: it sets the action state
    /// that polled actions read, and a handler that acts only on a fresh, non-echo press ignores it, so such a handler
    /// acts once for a hold. The alert keys are such a handler: a hold across a <c>select</c> step changes no stance
    /// at a run-dependent frame. A held chord is polled by its bare key, and only that key is pressed again: a chord
    /// presses no modifier key.
    /// </summary>
    public void KeepHeld()
    {
        foreach (var key in KeyMask.ToRepress(_held.Select(k => (long)k), code => Input.IsKeyPressed((Key)code)))
        {
            Press((Key)key, true, echo: true);
            Input.FlushBufferedEvents();
        }
    }

    // A chord's key name ("Shift+G") gives the key with modifier bits; the event carries the bare key and the
    // modifier flags, as a player's key press does. A chord carries its modifiers on the key's own event; it does not
    // press the modifier key itself, so Input.IsKeyPressed(Key.Shift) stays false while Shift+G is held.
    static void Press(Key key, bool pressed, bool echo = false)
    {
        var (code, shift, control, alt, meta) = KeyMask.Split((long)key);
        Input.ParseInputEvent(
            new InputEventKey
            {
                Keycode = (Key)code,
                PhysicalKeycode = (Key)code,
                ShiftPressed = shift,
                CtrlPressed = control,
                AltPressed = alt,
                MetaPressed = meta,
                Pressed = pressed,
                Echo = echo,
            }
        );
    }

    string? Scroll(ScrollStep scroll)
    {
        var center = Size / 2;
        var wheel = scroll.Up ? MouseButton.WheelUp : MouseButton.WheelDown;
        for (var i = 0; i < scroll.Count; i++)
        {
            ClickAt(wheel, center);
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

    // The viewport's last drawn frame, or its crop around a unit: a step that changes the view takes effect in the
    // frame after it, so a wait comes before the shot.
    string? Shot(ShotStep shot)
    {
        var file = Path.Combine(outputFolder, shot.Name + ".png");
        ScreenBox? box = null;
        if (shot.Unit is { } name)
        {
            var (unit, problem) = names.Resolve(name);
            if (unit == null)
                return problem;
            box = selection.ScreenBoxOf(unit);
            if (box == null)
                return $"'{name}' has no screen box: it shows no mesh, reaches behind the camera, or is off screen";
        }

        try
        {
            Directory.CreateDirectory(outputFolder);
            var image = View.GetTexture().GetImage();
            if (image == null)
                return $"cannot write '{file}': the viewport has no image";
            if (box is { } b)
            {
                var (crop, problem) = Crop(image, b, shot.Scale);
                if (crop == null)
                    return problem;
                image = crop;
            }

            // Opening the file first makes an unwritable target a throw here, not an engine ERROR on stderr.
            File.Open(file, FileMode.Create).Dispose();
            var saved = image.SavePng(file);

            return saved == Error.Ok ? null : $"cannot write '{file}': {saved}";
        }
        catch (Exception e) // a file that cannot be written is a problem, whatever the reason (INV-0007)
        {
            return $"cannot write '{file}': {e.Message}";
        }
    }

    // The image holds the viewport's pixels, which are as many as its viewport coordinates unless a display scale
    // applies; the box is scaled to the image first. A crop too large for an image once scaled is a problem.
    (Image? Image, string? Problem) Crop(Image image, ScreenBox box, int scale)
    {
        var size = image.GetSize();
        var (sx, sy) = (size.X / Size.X, size.Y / Size.Y);
        var crop = PlayTarget.CropOf(
            new ScreenBox(box.Left * sx, box.Top * sy, box.Right * sx, box.Bottom * sy),
            new System.Numerics.Vector2(size.X, size.Y)
        );
        if (PlayTarget.ScaleProblem(crop, scale) is { } problem)
            return (null, problem);
        var region = image.GetRegion(new Rect2I(crop.X, crop.Y, crop.Width, crop.Height));
        if (scale > 1)
            region.Resize(crop.Width * scale, crop.Height * scale, Image.Interpolation.Nearest);

        return (region, null);
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
