using System;
using System.Collections.Generic;
using System.Linq;
using OpenArmada.Assets;
using OpenArmada.Formats;

namespace OpenArmada.Sandbox;

/// <summary>
/// The key chords of the install's Input.map that set an alert stance: the <c>green</c>, <c>yellow</c>, and <c>red</c> blocks. A
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
            gap($"Input.map cannot be read, so the alert keys are unbound: {e.Message}");

            return bindings;
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
                gap(
                    $"Input.map has no keyboard binding for '{section}' alert, so that key is unbound"
                        + string.Concat(cause.Select(n => $" ({n})"))
                );

                continue;
            }

            bool Held(string modifier) =>
                lines.Any(e => e.Sign == "+" && e.Value.Equals(modifier, StringComparison.OrdinalIgnoreCase));
            bindings._chords[
                new KeyChord
                {
                    Key = key.Value.ToUpperInvariant(),
                    Shift = Held("Shift"),
                    Control = Held("Control"),
                    Alt = Held("Alt"),
                }
            ] = stance;
        }

        return bindings;
    }

    /// <summary>The stance the chord sets; null when it is bound to none.</summary>
    public AlertStance? For(KeyChord pressed) =>
        _chords.TryGetValue(pressed with { Key = pressed.Key.ToUpperInvariant() }, out var s) ? s : null;
}
