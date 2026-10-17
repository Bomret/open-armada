using Godot;
using OpenArmada.Sandbox;

namespace OpenArmada.Viewer;

// Sets the alert stance of every selected unit when the key chord of an alert binding is pressed.
public partial class AlertKeys : Node
{
    readonly AlertBindings _bindings;
    readonly UnitSelection _selection;
    readonly CombatWorld _world;

    public AlertKeys(AlertBindings bindings, UnitSelection selection, CombatWorld world)
    {
        _bindings = bindings;
        _selection = selection;
        _world = world;
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true, Echo: false } key)
            return;
        var chord = new KeyChord(OS.GetKeycodeString(key.Keycode), key.ShiftPressed, key.CtrlPressed, key.AltPressed);
        if (_bindings.For(chord) is not { } stance)
            return;
        foreach (var unit in _selection.Selected)
            if (_world.State(unit.Object) is { } state)
                state.Alert = stance;
        GetViewport().SetInputAsHandled();
    }
}
