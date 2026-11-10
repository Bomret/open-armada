namespace OpenArmada.Sandbox;

/// <summary>
/// A special weapon started: its first tick ran. The view plays the weapon's fire sound once and draws its beam for as
/// long as the unit's <see cref="UnitCombatState.SpecialOrder"/> holds this weapon.
/// </summary>
public sealed record SpecialWeaponStarted : CombatEvent
{
    /// <summary>The unit that runs the weapon.</summary>
    public required PlacedObject Unit { get; init; }

    /// <summary>The weapon that runs.</summary>
    public required SpecialWeaponProfile Weapon { get; init; }
}

/// <summary>
/// A special weapon a unit has running on a target: the weapon and the unit it is on. A toggle weapon runs from the
/// player's order until an end condition or a second order for its slot (docs/rules/combat-rules.md, "Special weapons").
/// </summary>
/// <param name="Weapon">The weapon that runs.</param>
/// <param name="Target">The unit the weapon is on.</param>
public sealed class SpecialWeaponOrder(SpecialWeaponProfile weapon, UnitCombatState target)
{
    /// <summary>The weapon that runs.</summary>
    public SpecialWeaponProfile Weapon { get; } = weapon;

    /// <summary>The unit the weapon is on.</summary>
    public UnitCombatState Target { get; } = target;

    /// <summary>Whether the world announced the weapon's start (<see cref="SpecialWeaponStarted"/>); set on its first tick.</summary>
    public bool Started { get; set; }

    /// <summary>
    /// The pose the Holding Beam holds its target at: recorded on the first tick the beam runs (the first hit, 0x4ddd30);
    /// null until then.
    /// </summary>
    public ShipPose? Held { get; set; }

    /// <summary>
    /// The seconds until the beam's next boarding party: set to the weapon's <c>boardingDelay</c> on the first tick the
    /// beam runs, counted down by each tick, and reset to <c>boardingInterval</c> after a party is sent (0x4ddd30);
    /// null until the first tick.
    /// </summary>
    public double? BoardingTimer { get; set; }
}
