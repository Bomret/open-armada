using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using OpenArmada.Diagnostics;
using OpenArmada.Sandbox;

namespace OpenArmada.Viewer;

/// <summary>
/// Turns each black hole's model about the vertical axis at the hole's <c>Rotation</c> radians per second of world time
/// (0x450950 turns it by its rotation times the step in each simulate step). The turn is a pure function of the world's
/// time (<see cref="BlackHole.Turn"/>) applied to the model's placed orientation, so it never depends on the frame
/// step. stand-in: the direction of the turn is positive about Godot's +Y axis; the game's sign is not recovered.
/// </summary>
public partial class BlackHolesView : Node3D
{
    readonly CombatWorld _world;
    readonly List<(BlackHole Hole, Node3D Node, Basis Placed)> _holes = new();
    readonly RepeatedFaults _faults = new(Area.Combat);

    /// <summary><c>nodes</c> are the scene's object nodes; a hole whose model is not in the scene is not turned.</summary>
    public BlackHolesView(
        CombatWorld world,
        IEnumerable<BlackHole> holes,
        IReadOnlyDictionary<PlacedObject, Node3D> nodes
    )
    {
        _world = world;
        foreach (var hole in holes)
        {
            if (nodes.TryGetValue(hole.Object, out var node))
            {
                _holes.Add((hole, node, node.Transform.Basis));
            }
        }
    }

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        var time = _world.Time + _world.Pending;
        foreach (var (hole, node, placed) in _holes)
        {
            try
            {
                if (!IsInstanceValid(node))
                    continue;
                var q = hole.Turn(time);
                node.Transform = new Transform3D(
                    new Basis(new Quaternion(q.X, q.Y, q.Z, q.W)) * placed,
                    node.Transform.Origin
                );
            }
            catch (Exception e) // a failure of one hole's turn never stops the sandbox (INV-0002)
            {
                _faults.Report($"black hole turn failed: {e.Message}");
            }
        }
    }

    /// <summary>Logs the count of repeated faults when the view leaves the scene.</summary>
    public override void _ExitTree() => _faults.Flush();
}
