namespace OpenArmada.Formats;

/// <summary>
/// The names of the five ship systems: SystemProfile.Name and the keys of UnitCombatState.Systems. Each is the name
/// of the system's hit location, so a hit location's name is the system it damages.
/// </summary>
public static class SystemNames
{
    /// <summary>The name of the engines, whose definition keys start with <c>engines</c>.</summary>
    public const string Engines = "Engines";

    /// <summary>The name of life support, whose definition keys start with <c>lifeSupport</c>.</summary>
    public const string LifeSupport = "LifeSupport";

    /// <summary>The name of the weapons system, whose definition keys start with <c>weapons</c>.</summary>
    public const string Weapons = "Weapons";

    /// <summary>The name of the shield generator, whose definition keys start with <c>shieldGenerator</c>.</summary>
    public const string ShieldGenerator = "ShieldGenerator";

    /// <summary>The name of the sensors, whose definition keys start with <c>sensors</c>.</summary>
    public const string Sensors = "Sensors";

    /// <summary>The five names in the order of <c>HitLocation</c>, so the index of a system is its location.</summary>
    public static readonly string[] All = [Engines, LifeSupport, Weapons, ShieldGenerator, Sensors];
}
