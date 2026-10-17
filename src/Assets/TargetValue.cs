using System.Collections.Generic;

namespace OpenArmada.Assets;

/// <summary>
/// A definition value that depends on the target: a default and the overrides by target definition name
/// (<c>damageBase = 30</c> then <c>"zmama.odf" 2</c>). The name match is case-sensitive, as the game's is.
/// Malformed is set when an override line was malformed: the overrides are then left out and the default stands.
/// </summary>
public sealed record TargetValue
{
    /// <summary>The value for a target that no override names.</summary>
    public required float Default { get; init; }

    /// <summary>The override values by target definition name (<c>zmama.odf</c>); empty when <see cref="Malformed"/> is set.</summary>
    public required IReadOnlyDictionary<string, float> Overrides { get; init; }

    /// <summary>Whether an override line was malformed, so the overrides are left out and only the default stands.</summary>
    public bool Malformed { get; init; } = false;

    /// <summary>A value with no overrides.</summary>
    public static TargetValue Of(float value) => new() { Default = value, Overrides = new Dictionary<string, float>() };

    /// <summary>The override for the target's definition name, matched case-sensitively; else the default.</summary>
    public float For(string target) => Overrides.TryGetValue(target, out var v) ? v : Default;
}
