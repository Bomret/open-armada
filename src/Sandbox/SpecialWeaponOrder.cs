namespace OpenArmada.Sandbox;

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

    /// <summary>
    /// The pose the Holding Beam holds its target at: recorded on the first tick the beam runs (the first hit, 0x4ddd30);
    /// null until then.
    /// </summary>
    public ShipPose? Held { get; set; }
}
