namespace OpenArmada.Sandbox;

/// <summary>
/// The combat band of the map: <c>SHIP_COMBAT_FLOOR</c> and <c>SHIP_COMBAT_CEILING</c> (RTS_CFG.h, read by 0x4453f0).
/// One band feeds the camera's look-at height, the plane move orders aim at, the path points' height, and the steering
/// clamp that stops a ship climbing above the ceiling or sinking below the floor (0x42ebb0, 0x42c9d0).
/// </summary>
public readonly record struct CombatBand(float Floor, float Ceiling)
{
    /// <summary>The stock band of the install: floor 10, ceiling 67.</summary>
    public static readonly CombatBand Stock = new(10, 67);

    /// <summary>
    /// The height of the normal plane, (ceiling + floor) / 2 (0x40a792, 0x4ac746): where a click lands and where path
    /// points lie. 38.5 for the stock band.
    /// </summary>
    public float Middle => (Floor + Ceiling) * 0.5f;
}
