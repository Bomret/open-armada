using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenArmada.Sandbox;

/// <summary>
/// The player's special-weapon gesture over the combat world: a <c>fire_special_N</c> key turns off the weapon of that slot
/// on each selected unit that runs it and waits, for the others that have it, for the click on a target; the first
/// left-click on a unit gives each waiting unit the order and ends the wait. A selection change or a right-click ends it
/// too. The viewer's nodes only translate Godot input events to these calls (docs/rules/combat-rules.md, "Special
/// weapons").
/// </summary>
public sealed class SpecialWeaponControl(
    CombatWorld world,
    AlertBindings bindings,
    Func<IReadOnlyList<PlacedObject>> selected
)
{
    List<PlacedObject> _waiting = [];
    int _slot;

    /// <summary>Whether a special-weapon key waits for its click: the next left-click on a unit is the target.</summary>
    public bool Pending { get; private set; }

    /// <summary>
    /// A key press. Returns true when it was a special-weapon key some selected unit has a weapon for and was taken. A
    /// key-repeat echo does nothing, and so does a key no selected unit has a weapon for. Per such unit: a weapon that
    /// runs is turned off, with no refund, and a unit with it off waits for the click. A selection of only running units
    /// waits for none (the per-unit loop over the selection is inferred, as the transport's is).
    /// </summary>
    public bool Pressed(KeyChord chord, bool echo)
    {
        if (echo || bindings.SpecialSlot(chord) is not { } slot)
            return false;
        var units = selected()
            .Where(u => world.State(u) is { } s && s.Profile.SpecialWeapons.Any(w => w.Slot == slot))
            .ToList();
        if (units.Count == 0)
            return false;
        var running = units.Where(u => world.State(u)!.SpecialOrder != null).ToList();
        foreach (var unit in running)
        {
            world.SpecialWeapon(unit, slot, null);
        }

        _slot = slot;
        _waiting = units.Except(running).ToList();
        Pending = _waiting.Count > 0;

        return true;
    }

    /// <summary>
    /// A left-click without a drag on <c>target</c> (null for empty space). While a key waits the click is taken (true) and
    /// the selection stays: a unit ends the wait and each waiting unit gets the order, which the world refuses for a target
    /// that is no enemy ship in range; empty space changes nothing. Outside the wait the click is left to the selection
    /// (false).
    /// </summary>
    public bool Click(PlacedObject? target)
    {
        if (!Pending)
            return false;
        if (target == null)
            return true;
        Pending = false;
        foreach (var unit in _waiting)
        {
            world.SpecialWeapon(unit, _slot, target);
        }

        return true;
    }

    /// <summary>The selection changed, by a click or a box drag: the wait ends.</summary>
    public void SelectionChanged() => Pending = false;

    /// <summary>
    /// A right-click: it ends the wait and is taken (true), so it gives no move or attack order. Outside the wait it is left
    /// to the orders (false). stand-in: the wait ends on a right-click as the transport mode does; the game's was not traced.
    /// </summary>
    public bool RightClick()
    {
        if (!Pending)
            return false;
        Pending = false;

        return true;
    }
}
