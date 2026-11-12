using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using OpenArmada.Formats;

namespace OpenArmada.Sandbox;

/// <summary>Where a hardpoint is: its offset in the model and the offset's place in the world, without Godot.</summary>
public static class HardpointGeometry
{
    /// <summary>
    /// The offset of every node from the model's origin, by name (looked up ignoring case; the first node of a name is
    /// the one kept). A parent name means the first node of exactly that name, with case, as SodBuilder and
    /// Sod.InParentCycle read it. The offsets are in Godot's frame (Z mirrored, as SodBuilder does): the node's origin
    /// carried through its transform and those of its parents. The chain ends where SodBuilder hangs a node from the
    /// root: at a node with no parent, a parent the model lacks, or one in a parent cycle. The root itself (the first
    /// such node) adds nothing, since the unit's placement replaces its transform.
    /// </summary>
    /// <remarks>
    /// stand-in: the transform is the one stored in the file; the game chains the node's current matrix through its
    /// parents (0x58c930, 0x596590), so an animated or level-of-detail node is not followed.
    /// </remarks>
    public static Dictionary<string, Vector3> Offsets(Sod sod)
    {
        var byName = new Dictionary<string, Sod.Node>(StringComparer.Ordinal);
        foreach (var n in sod.Nodes)
            byName.TryAdd(n.Name, n);

        bool HangsFromRoot(Sod.Node n) => n.Parent.Length == 0 || !byName.ContainsKey(n.Parent) || sod.InParentCycle(n);

        var root = sod.Nodes.FirstOrDefault(HangsFromRoot);
        // Each node's chain to the root is its transform followed by its parent's chain, so a node reuses its parent's.
        var chains = new Dictionary<Sod.Node, Matrix4x4>();
        var pending = new Stack<Sod.Node>();
        var offsets = new Dictionary<string, Vector3>(StringComparer.OrdinalIgnoreCase);
        foreach (var start in sod.Nodes)
        {
            if (offsets.ContainsKey(start.Name))
                continue;

            for (Sod.Node? node = start; node != null && node != root && !chains.ContainsKey(node); )
            {
                pending.Push(node);
                node = HangsFromRoot(node) ? null : byName[node.Parent];
            }

            while (pending.Count > 0)
            {
                var node = pending.Pop();
                var m = node.Transform;
                // Mirror Z, as SodBuilder.ToGodot: S * M * S with S = diag(1, 1, -1). Rows are the images of the axes.
                var own = new Matrix4x4(
                    m.Right.X,
                    m.Right.Y,
                    -m.Right.Z,
                    0,
                    m.Up.X,
                    m.Up.Y,
                    -m.Up.Z,
                    0,
                    -m.Front.X,
                    -m.Front.Y,
                    m.Front.Z,
                    0,
                    m.Position.X,
                    m.Position.Y,
                    -m.Position.Z,
                    1
                );
                chains[node] =
                    HangsFromRoot(node) || byName[node.Parent] == root ? own : own * chains[byName[node.Parent]];
            }

            offsets[start.Name] = start == root ? Vector3.Zero : chains[start].Translation;
        }

        return offsets;
    }

    /// <summary>
    /// The world position of a point at <c>offset</c> in the model of <c>unit</c>, which stands in <c>pose</c>: the
    /// unit's node transform as UnitOrders sets it (the pose's orientation, <see cref="ShipPose.Orientation"/>, at the
    /// pose's position, with the placed basis's scale), applied to the offset. A mirrored placed basis keeps its mirror.
    /// </summary>
    public static Vector3 WorldPosition(PlacedObject unit, ShipPose pose, Vector3 offset)
    {
        var scale = Split(unit).Scale;

        return pose.Position + Vector3.Transform(offset * scale, pose.Orientation);
    }

    /// <summary>The offset in the model, in Godot's frame, of the world point <c>at</c>: the inverse of <see cref="WorldPosition"/>.</summary>
    public static Vector3 ModelOffset(PlacedObject unit, ShipPose pose, Vector3 at)
    {
        var scale = Split(unit).Scale;

        return Vector3.Transform(at - pose.Position, Quaternion.Inverse(pose.Orientation)) / scale;
    }

    /// <summary>
    /// The unit's placed basis split into scale and rotation: the scale is the axes' lengths, with the mirror of a
    /// negative determinant on the side axis only, so the rotation's side, up, and forward axes are (-X, Y, Z) of a
    /// mirrored basis and its forward axis is the raw one, the bow the model shows (the game's yaw reads it). The
    /// three unit axes are the rotation's columns.
    /// </summary>
    public static (Vector3 Scale, Vector3 X, Vector3 Y, Vector3 Z) Split(PlacedObject unit)
    {
        var sign = Vector3.Dot(unit.AxisX, Vector3.Cross(unit.AxisY, unit.AxisZ)) < 0 ? -1f : 1f;
        var scale = new Vector3(unit.AxisX.Length() * sign, unit.AxisY.Length(), unit.AxisZ.Length());

        return (scale, unit.AxisX / scale.X, unit.AxisY / scale.Y, unit.AxisZ / scale.Z);
    }
}
