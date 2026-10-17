namespace OpenArmada.Sandbox;

/// <summary>
/// One ship system of one unit: its profile, its maximum hit points (the definition's plus
/// SYSTEM_HITPOINTS_BONUS), and the hit points left. Hit points are fractional because repair adds fractions;
/// a hit takes whole points. A destroyed system stays destroyed until repair has brought it back to the maximum.
/// </summary>
public sealed class ShipSystemState(SystemProfile profile, double bonus)
{
    /// <summary>The system's profile.</summary>
    public SystemProfile Profile { get; } = profile;

    /// <summary>The maximum hit points: the definition's plus SYSTEM_HITPOINTS_BONUS.</summary>
    public double Max { get; } = profile.HitPoints + bonus;

    /// <summary>The hit points left; starts at <see cref="Max"/>.</summary>
    public double HitPoints { get; set; } = profile.HitPoints + bonus;

    /// <summary>Whether the system is destroyed; set at zero hit points and cleared once repair reaches <see cref="Max"/>.</summary>
    public bool Destroyed { get; set; }
}
