using System;
using OpenArmada.Assets;

namespace OpenArmada.Sandbox;

/// <summary>
/// A map object that ships or stations are: its definition names a physics definition. Name is the
/// definition's unitName.
/// </summary>
public abstract record Unit
{
    /// <summary>The definition's unitName; the class name when the definition has none or it cannot be read.</summary>
    public required string Name { get; init; }
}

/// <summary>A unit whose definition sets no is_starbase, sets it to 0, or sets one that cannot be read.</summary>
public sealed record Ship : Unit;

/// <summary>A unit whose definition sets is_starbase to anything but 0.</summary>
public sealed record Station : Unit;

/// <summary>The rules that tell from a definition whether it describes a unit, and which kind.</summary>
public static class UnitRules
{
    /// <summary>
    /// The unit the definition describes, or null when it names no physicsFile. A station sets is_starbase to
    /// anything but 0; a ship does not. Throws DefinitionException for an is_starbase that is no whole number.
    /// </summary>
    public static Unit? Classify(ResolvedDefinition definition)
    {
        if (!definition.Has("physicsFile"))
            return null;
        var name = definition.Has("unitName")
            ? definition.GetString("unitName")
            : definition.Name.Replace(".odf", "", StringComparison.OrdinalIgnoreCase);

        return definition.Has("is_starbase") && definition.GetInt("is_starbase") != 0
            ? new Station { Name = name }
            : new Ship { Name = name };
    }
}
