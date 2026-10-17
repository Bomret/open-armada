namespace OpenArmada.Sandbox;

// How a unit picks targets and acts on its own (docs/combat-rules.md, "Alert stance").
public enum AlertStance
{
    Red,
    Yellow,
    Green,
}

// A key and the modifiers held with it, as an Input.map binding names them. Key is the key's name in upper case.
public readonly record struct KeyChord
{
    public required string Key { get; init; }
    public bool Shift { get; init; }
    public bool Control { get; init; }
    public bool Alt { get; init; }
}
