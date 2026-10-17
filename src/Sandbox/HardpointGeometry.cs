using System;
using System.Collections.Generic;
using System.Numerics;
using OpenArmada.Formats;

namespace OpenArmada.Sandbox;

/// <summary>Where a hardpoint is: its offset in the model and the offset's place in the world, without Godot.</summary>
public static class HardpointGeometry
{
    /// <summary>
    /// The offset of the node a hardpoint name matches (ignoring case) from the model's origin, in Godot's frame (Z
    /// mirrored, as SodBuilder does): the node's origin carried through its transform and those of its parents. The
    /// first node of a name is the one matched, and the one a parent name means. A parent the model lacks, or one
    /// already in the chain (a cycle), ends the chain. Null when the model holds no such node.
    /// </summary>
    /// <remarks>
    /// stand-in: the transform is the one stored in the file; the game chains the node's current matrix through its
    /// parents (0x58c930, 0x596590), so an animated or level-of-detail node is not followed.
    /// </remarks>
    public static Vector3? ModelOffset(Sod sod, string name)
    {
        var byName = new Dictionary<string, Sod.Node>();
        Sod.Node? node = null;
        foreach (var n in sod.Nodes)
        {
            byName.TryAdd(n.Name, n);
            if (node == null && string.Equals(n.Name, name, StringComparison.OrdinalIgnoreCase))
                node = n;
        }

        if (node == null)
            return null;
        var at = Vector3.Zero;
        var seen = new HashSet<Sod.Node>(ReferenceEqualityComparer.Instance);
        while (node != null && seen.Add(node))
        {
            var m = node.Transform;
            // Mirror Z, as SodBuilder.ToGodot: S * M * S with S = diag(1, 1, -1).
            at =
                new Vector3(m.Right.X, m.Right.Y, -m.Right.Z) * at.X
                + new Vector3(m.Up.X, m.Up.Y, -m.Up.Z) * at.Y
                + new Vector3(-m.Front.X, -m.Front.Y, m.Front.Z) * at.Z
                + new Vector3(m.Position.X, m.Position.Y, -m.Position.Z);
            node = node.Parent.Length > 0 ? byName.GetValueOrDefault(node.Parent) : null;
        }

        return at;
    }

    /// <summary>
    /// The world position of a point at <c>offset</c> in the model of <c>unit</c>, which stands in <c>pose</c>: the
    /// unit's node transform as UnitOrders sets it (its placed basis, turned by the heading change and the bank, at the
    /// pose's position), applied to the offset. A mirrored placed basis keeps its mirror.
    /// </summary>
    public static Vector3 WorldPosition(PlacedObject unit, ShipPose pose, Vector3 offset)
    {
        // The basis splits into scale and rotation as Godot's Basis does: a negative determinant negates the scale.
        var sign = Vector3.Dot(unit.AxisX, Vector3.Cross(unit.AxisY, unit.AxisZ)) < 0 ? -1f : 1f;
        var scale = new Vector3(unit.AxisX.Length(), unit.AxisY.Length(), unit.AxisZ.Length()) * sign;
        var x = unit.AxisX / scale.X;
        var y = unit.AxisY / scale.Y;
        var z = unit.AxisZ / scale.Z;
        var initial = Quaternion.CreateFromRotationMatrix(
            new Matrix4x4(x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0, z.X, z.Y, z.Z, 0, 0, 0, 0, 1)
        );
        var turned = MoveGeometry.Orientation(initial, pose.Heading - MoveGeometry.BowHeading(unit.AxisZ), pose.Bank);

        return pose.Position + Vector3.Transform(offset * scale, turned);
    }
}
