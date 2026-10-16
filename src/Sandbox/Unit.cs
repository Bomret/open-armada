using System;
using OpenArmada.Assets;

namespace OpenArmada.Sandbox;

// A map object that ships or stations are: its definition names a physics definition. Name is the
// definition's unitName.
public abstract record Unit(string Name);

public sealed record Ship(string Name) : Unit(Name);

public sealed record Station(string Name) : Unit(Name);

public static class UnitRules
{
    // The unit the definition describes, or null when it names no physicsFile. A station sets is_starbase to
    // anything but 0; a ship does not. Throws DefinitionException for an is_starbase that is no whole number.
    public static Unit? Classify(ResolvedDefinition definition)
    {
        if (!definition.Has("physicsFile"))
            return null;
        var name = definition.Has("unitName")
            ? definition.GetString("unitName")
            : definition.Name.Replace(".odf", "", StringComparison.OrdinalIgnoreCase);
        return definition.Has("is_starbase") && definition.GetInt("is_starbase") != 0
            ? new Station(name)
            : new Ship(name);
    }
}
