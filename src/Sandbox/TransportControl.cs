using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace OpenArmada.Sandbox;

/// <summary>The cursor over a unit while the transport mode is pending: the game's <c>c_transport</c> or <c>c_notransport</c>.</summary>
public enum TransportCursor
{
    /// <summary>The cursor is unchanged: the mode is not pending, or no unit is under it.</summary>
    Default,

    /// <summary><c>c_transport</c>: a selected unit can transport to the unit under the cursor.</summary>
    Transport,

    /// <summary><c>c_notransport</c>: no selected unit can.</summary>
    NoTransport,
}

/// <summary>
/// The player's transport gesture over the combat world: the transport chord cancels the transport of each
/// selected unit that has one and enters the mode for the others; the first left-click on a unit gives every waiting unit
/// that takes it the order and ends the mode. A selection change or a right-click ends it too. The viewer's nodes only translate Godot input events to
/// these calls (docs/rules/combat-rules.md, "Transport and boarding").
/// </summary>
public sealed class TransportControl(
    CombatWorld world,
    AlertBindings bindings,
    Func<IReadOnlyList<PlacedObject>> selected,
    Func<PlacedObject, Vector3> positionOf
)
{
    List<PlacedObject> _waiting = [];

    /// <summary>Whether the transport mode is pending: the next left-click on a unit is the target.</summary>
    public bool Pending { get; private set; }

    /// <summary>
    /// A key press. Returns true when it was the transport chord and was taken. A key-repeat echo does nothing, and so
    /// does the chord with nothing selected. Per selected unit (as 0x463593 does): a unit transporting has its transport
    /// cancelled, and a unit not transporting waits for the click in the mode. A selection of only transporting units
    /// enters no mode.
    /// </summary>
    public bool Pressed(KeyChord chord, bool echo)
    {
        if (echo || !bindings.IsTransport(chord))
            return false;
        var units = selected();
        if (units.Count == 0)
            return false;
        var transporting = units.Where(u => world.State(u)?.Transporting == true).ToList();
        foreach (var unit in transporting)
        {
            world.CancelTransport(unit);
        }

        // the units that were idle wait for the click (per unit; the loop over the selection is inferred)
        _waiting = units.Except(transporting).ToList();
        Pending = _waiting.Count > 0;

        return true;
    }

    /// <summary>
    /// A left-click without a drag on <c>target</c> (null for empty space). While the mode is pending the click is taken
    /// (true) and the selection stays: a unit ends the mode and every unit that was waiting (not transporting at the chord)
    /// and takes the order gets it; empty
    /// space changes nothing. Outside the mode the click is left to the selection (false).
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
            world.Transport(unit, target);
        }

        return true;
    }

    /// <summary>The selection changed, by a click or a box drag: the mode ends.</summary>
    public void SelectionChanged() => Pending = false;

    /// <summary>
    /// A right-click: it ends the mode and is taken (true), so it gives no move or attack order. Outside the mode it is
    /// left to the orders (false). stand-in: the game deselects on a right-click in the mode (inferred); the selection is
    /// kept here.
    /// </summary>
    public bool RightClick()
    {
        if (!Pending)
            return false;
        Pending = false;

        return true;
    }

    /// <summary>
    /// The cursor over <c>hovered</c> (null for no unit): <see cref="TransportCursor.Transport"/> while the mode is
    /// pending and <see cref="CombatRules.CanTransportTo"/> accepts the unit for a waiting unit, which is the game's merge
    /// of the per-unit answers (0x4318a0); <see cref="TransportCursor.NoTransport"/> over any other unit.
    /// </summary>
    public TransportCursor Cursor(PlacedObject? hovered)
    {
        if (!Pending || hovered == null)
            return TransportCursor.Default;
        if (world.State(hovered) is not { } target)
            return TransportCursor.NoTransport;
        var range = world.Transporter.Range;
        var ok = _waiting
            .Any(u =>
                world.State(u) is { } source
                && CombatRules.CanTransportTo(
                    source,
                    target,
                    Vector3.Distance(positionOf(u), positionOf(hovered)),
                    range
                )
            );

        return ok ? TransportCursor.Transport : TransportCursor.NoTransport;
    }
}
