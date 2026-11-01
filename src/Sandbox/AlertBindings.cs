using System;
using System.Collections.Generic;
using System.Linq;
using OpenArmada.Assets;
using OpenArmada.Diagnostics;
using OpenArmada.Formats;

namespace OpenArmada.Sandbox;

/// <summary>
/// The key chords of the install's Input.map that set an alert stance: the <c>green</c>, <c>yellow</c>, and <c>red</c> blocks, and the chord of the transport order: the <c>transport</c> block. A
/// block's <c>+ keyboard X</c> line names the key; <c>+ keyboard Shift</c> (or Control, Alt) a modifier that must be held. A
/// modifier the block does not require with <c>+</c> must not be held.
/// stand-in: how the game matches a chord with extra modifiers held is not traced; the match is exact.
/// </summary>
public sealed class AlertBindings
{
    static readonly string[] Modifiers = { "Shift", "Control", "Alt" };

    readonly Dictionary<KeyChord, AlertStance> _chords = new();

    AlertBindings() { }

    /// <summary>
    /// Reads the bindings; a missing or unreadable Input.map, or a binding without a key, goes to <c>gap</c> and leaves
    /// that stance unbound (INV-0002).
    /// </summary>
    public static AlertBindings Read(GameFileSystem files, Action<string> gap)
    {
        var bindings = new AlertBindings();
        MapFile map;
        try
        {
            map = (MapFile)new KeyMapKind().Load(files, "Input.map");
        }
        catch (Exception e)
        {
            var text = $"Input.map cannot be read, so the alert and transport keys are unbound: {e.Message}";
            Log.Error(Area.Combat, text);
            gap(text);

            return bindings;
        }

        // The chord of the section's block; a block without a key goes to the gap and leaves its order unbound.
        KeyChord? Chord(string section, string what)
        {
            var lines = map
                .Entries.Where(e =>
                    e.Section == section && e.Key.Equals("keyboard", StringComparison.OrdinalIgnoreCase)
                )
                .ToList();
            var key = lines.FirstOrDefault(e =>
                e.Sign == "+" && !Modifiers.Contains(e.Value, StringComparer.OrdinalIgnoreCase)
            );
            if (key == null)
            {
                // a malformed line of the block is the likely cause, so the gap names it
                var cause = map.Notes.Where(n => n.EndsWith($"in block '{section}'", StringComparison.Ordinal));
                var text =
                    $"Input.map has no keyboard binding for '{section}' {what}, so that key is unbound"
                    + string.Concat(cause.Select(n => $" ({n})"));
                Log.Warn(Area.Combat, text);
                gap(text);

                return null;
            }

            bool Held(string modifier) =>
                lines.Any(e => e.Sign == "+" && e.Value.Equals(modifier, StringComparison.OrdinalIgnoreCase));

            return new KeyChord
            {
                Key = key.Value.ToUpperInvariant(),
                Shift = Held("Shift"),
                Control = Held("Control"),
                Alt = Held("Alt"),
            };
        }

        foreach (
            var (section, stance) in new[]
            {
                ("green", AlertStance.Green),
                ("yellow", AlertStance.Yellow),
                ("red", AlertStance.Red),
            }
        )
        {
            if (Chord(section, "alert") is { } chord)
                bindings._chords[chord] = stance;
        }

        // inferred: the transport block's key is the key of the transport order; the game's reader of Input.map is
        // not traced to the button.
        bindings.Transport = Chord("transport", "order");

        return bindings;
    }

    /// <summary>The chord of the transport order; null when Input.map or its <c>transport</c> block is missing.</summary>
    public KeyChord? Transport { get; private set; }

    /// <summary>Whether the chord is the transport chord.</summary>
    public bool IsTransport(KeyChord pressed) => Transport is { } t && t == pressed with { Key = pressed.Key.ToUpperInvariant() };

    /// <summary>The stance the chord sets; null when it is bound to none.</summary>
    public AlertStance? For(KeyChord pressed) =>
        _chords.TryGetValue(pressed with { Key = pressed.Key.ToUpperInvariant() }, out var s) ? s : null;
}
