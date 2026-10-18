namespace OpenArmada.Sandbox;

/// <summary>
/// Splits a Godot keycode that carries modifier bits, as <c>OS.FindKeycodeFromString("Shift+G")</c> returns it, into the
/// bare key and the modifiers held with it. A key event carries the bare key in <c>Keycode</c> and the modifiers in its
/// own flags, so a play script's <c>press Shift+G</c> reaches the alert keys as a player's Shift+G does. The bit values
/// are Godot's <c>KeyModifierMask</c>.
/// </summary>
public static class KeyMask
{
    const long CodeMask = (1L << 23) - 1;
    const long Shift = 1L << 25;
    const long Alt = 1L << 26;
    const long Meta = 1L << 27;
    const long Control = 1L << 28;

    /// <summary>The bare keycode and whether Shift, Control, Alt, and Meta are held.</summary>
    public static (long Code, bool Shift, bool Control, bool Alt, bool Meta) Split(long keycode) =>
        (
            keycode & CodeMask,
            (keycode & Shift) != 0,
            (keycode & Control) != 0,
            (keycode & Alt) != 0,
            (keycode & Meta) != 0
        );
}
