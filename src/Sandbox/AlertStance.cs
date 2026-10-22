namespace OpenArmada.Sandbox;

/// <summary>How a unit picks targets and acts on its own (docs/rules/combat-rules.md, "Alert stance").</summary>
public enum AlertStance
{
    /// <summary>Picks targets by itself; an idle unit that can attack closes on its pick near its tether point, or returns to the point.</summary>
    Red,

    /// <summary>Picks targets by itself and fires at them, and never moves on its own.</summary>
    Yellow,

    /// <summary>Picks no target by itself; fires only at the target of an attack order.</summary>
    Green,
}

/// <summary>A key and the modifiers held with it, as an Input.map binding names them. Key is the key's name in upper case.</summary>
public readonly record struct KeyChord
{
    /// <summary>The key's name in upper case.</summary>
    public required string Key { get; init; }

    /// <summary>Whether Shift is held.</summary>
    public bool Shift { get; init; }

    /// <summary>Whether Control is held.</summary>
    public bool Control { get; init; }

    /// <summary>Whether Alt is held.</summary>
    public bool Alt { get; init; }
}
