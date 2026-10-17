using System.Collections.Generic;

namespace OpenArmada.Assets;

// A definition value that depends on the target: a default and the overrides by target definition name
// (`damageBase = 30` then `"zmama.odf" 2`). The name match is case-sensitive, as the game's is.
// Malformed is set when an override line was malformed: the overrides are then left out and the default stands.
public sealed record TargetValue
{
    public required float Default { get; init; }
    public required IReadOnlyDictionary<string, float> Overrides { get; init; }
    public bool Malformed { get; init; } = false;

    // A value with no overrides.
    public static TargetValue Of(float value) => new() { Default = value, Overrides = new Dictionary<string, float>() };

    public float For(string target) => Overrides.TryGetValue(target, out var v) ? v : Default;
}
