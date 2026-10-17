namespace OpenArmada.Sandbox;

// One ship system of one unit: its profile, its maximum hit points (the definition's plus
// SYSTEM_HITPOINTS_BONUS), and the hit points left. Hit points are fractional because repair adds fractions;
// a hit takes whole points. A destroyed system stays destroyed until repair has brought it back to the maximum.
public sealed class ShipSystemState(SystemProfile profile, double bonus)
{
    public SystemProfile Profile { get; } = profile;
    public double Max { get; } = profile.HitPoints + bonus;
    public double HitPoints { get; set; } = profile.HitPoints + bonus;
    public bool Destroyed { get; set; }
}
