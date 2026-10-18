using System;
using System.Collections.Generic;
using System.Linq;

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

    /// <summary>
    /// Whether <paramref name="keycode"/>, as <c>OS.FindKeycodeFromString(name)</c> returns it, holds every part of the
    /// key name: a known key and one modifier bit for each <c>+</c>-joined modifier. Godot gives code 0 for an unknown
    /// key and sets no bit for a modifier name it does not know (<c>Control</c> where it spells <c>Ctrl</c>, or a key
    /// name in front: <c>G+Shift</c> is the Shift key), so such a name does not read.
    /// </summary>
    public static bool Reads(string name, long keycode)
    {
        var modifiers = new[] { Shift, Alt, Meta, Control }.Count(bit => (keycode & bit) != 0);

        return (keycode & CodeMask) != 0 && modifiers == name.Split('+').Length - 1;
    }

    /// <summary>
    /// The held keycodes, in held order, whose bare key <paramref name="isPressed"/> reads released.
    /// <paramref name="isPressed"/> is <c>Input.IsKeyPressed</c>, which reads the bare key, so a chord is polled by
    /// its bare key and given back with its modifier bits.
    /// </summary>
    public static IEnumerable<long> ToRepress(IEnumerable<long> held, Func<long, bool> isPressed) =>
        held.Where(key => !isPressed(key & CodeMask));

    /// <summary>
    /// What is wrong with the key name <paramref name="name"/>, or null when it reads. <paramref name="find"/> is
    /// <c>OS.FindKeycodeFromString</c>. A name with an empty part (<c>+R</c>, <c>Shift++R</c>, <c>Shift+</c>) says
    /// so. A known key with a part in front that sets no modifier bit names that part; an unknown key, or a modifier
    /// given twice, is no key name.
    /// </summary>
    public static string? Problem(string name, Func<string, long> find)
    {
        var keycode = find(name);
        if (Reads(name, keycode))
            return null;
        if (name.Split('+').Contains(""))
            return $"'{name}' is no key name: it has an empty part";
        var unread = name.Split('+')[..^1]
            .FirstOrDefault(part => (find(part + "+A") & (Shift | Alt | Meta | Control)) == 0);

        return (keycode & CodeMask) == 0 || unread is null
            ? $"'{name}' is no key name"
            : $"'{name}' is no key name: '{unread}' is not a modifier Godot knows";
    }
}
