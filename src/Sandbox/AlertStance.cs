namespace OpenArmada.Sandbox;

// How a unit picks targets and acts on its own (docs/combat-rules.md, "Alert stance").
public enum AlertStance
{
    Red,
    Yellow,
    Green,
}

// A key and the modifiers held with it, as an Input.map binding names them. Key is the key's name in upper case.
public readonly record struct KeyChord(string Key, bool Shift = false, bool Control = false, bool Alt = false);
