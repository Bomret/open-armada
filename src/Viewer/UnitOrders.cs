using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using OpenArmada.Assets;
using OpenArmada.Formats;
using OpenArmada.Sandbox;

namespace OpenArmada.Viewer;

// The orders of the map sandbox and the pose of every unit. A right-click sends the selected ships to where its ray meets the middle of the
// combat plane, clamped to the map's bounds; a ray that misses the plane is ignored. Each ship moves at its own
// height by its ShipMotion, fed by its physics definition. Stations and all other objects never move. Pose reads
// the current pose of any unit, from the moment the map opens: the placed pose until the unit moves.
public partial class UnitOrders : Node3D
{
    readonly UnitSelection _selection;
    readonly Dictionary<PlacedObject, Node3D> _nodes;

    // Scale is the node basis's scale (negative on all axes for a mirrored basis); the rotation is rebuilt around it.
    readonly Dictionary<
        PlacedObject,
        (ShipMotion Motion, System.Numerics.Quaternion Initial, float Heading, Vector3 Scale)
    > _moving = new();
    readonly Dictionary<PlacedObject, ShipPose> _poses;
    readonly MovePlan _plan;
    readonly float _planeHeight;
    readonly MapBounds _bounds;

    // `planeHeight` is the middle of the combat plane, the camera rig's CombatPlaneHeight (the rig reports its
    // stand-ins), so orders aim at the plane the camera looks at; `bounds` are the ones the rig clamps its look-at
    // point to.
    public UnitOrders(
        GameFileSystem files,
        SandboxPlan plan,
        UnitSelection selection,
        Dictionary<PlacedObject, Node3D> nodes,
        float planeHeight,
        MapBounds bounds,
        Action<string> gap
    )
    {
        _selection = selection;
        _nodes = nodes;
        _planeHeight = planeHeight;
        _bounds = bounds;
        _poses = selection.Units.ToDictionary(
            u => u.Object,
            u =>
                MoveGeometry.PlacedPose(
                    new System.Numerics.Vector3(u.Object.Position.X, u.Object.Position.Y, u.Object.Position.Z),
                    new System.Numerics.Vector3(u.Object.AxisZ.X, u.Object.AxisZ.Y, u.Object.AxisZ.Z)
                )
        );
        _plan = MovePlan.Build(plan, selection.Units, files);
        foreach (var report in _plan.Reports)
            gap(report.Text);
    }

    // The current pose of a unit of the map; throws KeyNotFoundException for an object that is no unit.
    public ShipPose Pose(PlacedObject unit) => _poses[unit];

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is not InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true } click)
            return;
        var camera = GetViewport().GetCamera3D();
        if (camera == null)
            return;
        var origin = camera.ProjectRayOrigin(click.Position);
        var direction = camera.ProjectRayNormal(click.Position);
        if (
            MoveGeometry.PlaneTarget(
                new System.Numerics.Vector3(origin.X, origin.Y, origin.Z),
                new System.Numerics.Vector3(direction.X, direction.Y, direction.Z),
                _planeHeight
            )
            is not { } hit
        )
            return;
        // stand-in: an order outside the map goes to the nearest point of its edge; how the game handles such a click
        // is not known.
        var target = MoveGeometry.ClampToMap(hit, _bounds);
        foreach (var unit in _selection.Selected)
        {
            if (
                !unit.Movable
                || !_plan.Physics.TryGetValue(unit.Object, out var physics)
                || !_nodes.TryGetValue(unit.Object, out var node)
            )
                continue;
            if (!_moving.TryGetValue(unit.Object, out var state))
            {
                var o = unit.Object;
                var initial = node.Transform.Basis.GetRotationQuaternion();
                var heading = MoveGeometry.BowHeading(new System.Numerics.Vector3(o.AxisZ.X, o.AxisZ.Y, o.AxisZ.Z));
                _moving[o] = state = (
                    new ShipMotion(
                        physics,
                        new System.Numerics.Vector3(o.Position.X, o.Position.Y, o.Position.Z),
                        heading
                    ),
                    new System.Numerics.Quaternion(initial.X, initial.Y, initial.Z, initial.W),
                    heading,
                    node.Transform.Basis.Scale
                );
            }
            state.Motion.SetTarget(target.X, target.Y);
        }
    }

    public override void _Process(double delta)
    {
        foreach (var (o, (motion, initial, start, scale)) in _moving)
        {
            var pose = _poses[o] = motion.Step(delta);
            var q = MoveGeometry.Orientation(initial, pose.Heading - start, pose.Bank);
            _nodes[o].Transform = new Transform3D(
                new Basis(new Quaternion(q.X, q.Y, q.Z, q.W)) * Basis.FromScale(scale),
                new Vector3(pose.Position.X, pose.Position.Y, pose.Position.Z)
            );
        }
    }
}
