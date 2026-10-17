using Godot;
using OpenArmada.Sandbox;

namespace OpenArmada.Viewer;

// Sets the alert stance of every selected unit when the key chord of an alert binding is pressed.
public partial class AlertKeys(AlertBindings bindings, UnitSelection selection, CombatWorld world) : Node
{
    public override void _UnhandledInput(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true, Echo: false } key)
            return;
        // stand-in: Godot's key names match the Input.map names for letters and F-keys (the alert keys of the
        // install are letters); digits ("One"), the numpad and arrows ("GreyUpArrow"), "Minus", and "Plus" differ, so
        // a rebound alert key of those kinds is not matched.
        var chord = new KeyChord
        {
            Key = OS.GetKeycodeString(key.Keycode),
            Shift = key.ShiftPressed,
            Control = key.CtrlPressed,
            Alt = key.AltPressed,
        };
        if (bindings.For(chord) is not { } stance)
            return;
        foreach (var unit in selection.Selected)
        {
            if (world.State(unit.Object) is { } state)
            {
                state.Alert = stance;
            }
        }

        GetViewport().SetInputAsHandled();
    }
}
