using System.Collections.Generic;

namespace OpenArmada.Assets;

// A definition value that depends on the target: a default and the overrides by target definition name
// (`damageBase = 30` then `"zmama.odf" 2`). The name match is case-sensitive, as the game's is.
// Problem is set when an override line was malformed: the overrides are then left out and the default stands.
public sealed record TargetValue(float Default, IReadOnlyDictionary<string, float> Overrides, string? Problem = null)
{
    public float For(string target) => Overrides.TryGetValue(target, out var v) ? v : Default;
}
