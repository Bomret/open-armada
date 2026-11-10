using Godot;
using OpenArmada.Sandbox;

namespace OpenArmada.Viewer;

/// <summary>
/// Carries the player's special-weapon gesture from Godot input events to <see cref="SpecialWeaponControl"/>: the
/// <c>fire_special_N</c> keys and, while a key waits for its target, a right-click (taken, so the orders node gives no
/// order). The node comes after the orders node in the tree, so it sees an event first. The selection hands it the
/// left-click and its changes through <see cref="UnitSelection.ClickTaker"/>, which this node chains after the one
/// already set.
/// </summary>
public partial class SpecialWeaponInput : Node
{
    readonly SpecialWeaponControl _control;

    /// <summary>Wires the control to the selection.</summary>
    public SpecialWeaponInput(SpecialWeaponControl control, UnitSelection selection)
    {
        _control = control;
        var earlier = selection.ClickTaker;
        selection.ClickTaker = target => control.Click(target) || earlier?.Invoke(target) == true;
        selection.SelectionChanged += control.SelectionChanged;
    }

    /// <inheritdoc/>
    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true } key)
        {
            // stand-in: Godot's key names match the Input.map names for letters and F-keys (see AlertKeys)
            var chord = new KeyChord
            {
                Key = OS.GetKeycodeString(key.Keycode),
                Shift = key.ShiftPressed,
                Control = key.CtrlPressed,
                Alt = key.AltPressed,
            };
            if (_control.Pressed(chord, key.Echo))
                GetViewport().SetInputAsHandled();
        }
        else if (e is InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true } && _control.RightClick())
        {
            GetViewport().SetInputAsHandled();
        }
    }
}
