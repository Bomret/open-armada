using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using OpenArmada.Assets;
using OpenArmada.Sandbox;

namespace OpenArmada.Viewer;

// Move orders of the map sandbox: a right-click sends the selected ships to the point where the click's ray
// meets the middle of the combat plane. Each ship moves by its ShipMotion, fed by its physics definition, at its
// own height; stations and every other object never move. A click whose ray misses the plane is ignored.
public partial class MoveOrders : Node3D
{
    readonly UnitSelection _selection;
    readonly Dictionary<PlacedObject, Node3D> _nodes;
    readonly Dictionary<PlacedObject, (ShipMotion Motion, System.Numerics.Quaternion Initial, float Heading)> _moving = new();
    readonly MovePlan _plan;
    readonly float _planeHeight;

    public MoveOrders(GameFileSystem files, SandboxPlan plan, UnitSelection selection, Dictionary<PlacedObject, Node3D> nodes, Action<string> gap)
    {
        _selection = selection;
        _nodes = nodes;
        _plan = MovePlan.Build(plan, selection.Units, files);
        foreach (var report in _plan.Reports) gap(report.Text);
        // The combat plane's middle height is read from the game constants; when they cannot be read, the plane
        // lies at height 0 and a gap says so (INV-0002).
        // stand-in: the combat plane at height 0 when SHIP_COMBAT_FLOOR or SHIP_COMBAT_CEILING is unreadable.
        try
        {
            var constants = GameConstantsKind.Of(files);
            if (!constants.TryNumber("SHIP_COMBAT_FLOOR", out var floor, out var p1)) gap($"combat plane height unreadable ({p1}), plane at height 0 used");
            else if (!constants.TryNumber("SHIP_COMBAT_CEILING", out var ceiling, out var p2)) gap($"combat plane height unreadable ({p2}), plane at height 0 used");
            else _planeHeight = (float)((floor + ceiling) / 2);
        }
        catch (Exception e)
        {
            gap($"combat plane height unreadable ({e.Message}), plane at height 0 used");
        }
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is not InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true } click) return;
        var camera = GetViewport().GetCamera3D();
        if (camera == null) return;
        var origin = camera.ProjectRayOrigin(click.Position);
        var direction = camera.ProjectRayNormal(click.Position);
        if (MoveGeometry.PlaneTarget(new System.Numerics.Vector3(origin.X, origin.Y, origin.Z),
                new System.Numerics.Vector3(direction.X, direction.Y, direction.Z), _planeHeight) is not { } target) return;
        foreach (var unit in _selection.Selected)
        {
            if (!unit.Movable || !_plan.Physics.TryGetValue(unit.Object, out var physics) || !_nodes.TryGetValue(unit.Object, out var node)) continue;
            if (!_moving.TryGetValue(unit.Object, out var state))
            {
                var o = unit.Object;
                var initial = node.Transform.Basis.GetRotationQuaternion();
                var heading = MoveGeometry.BowHeading(new System.Numerics.Vector3(o.AxisZ.X, o.AxisZ.Y, o.AxisZ.Z));
                _moving[o] = state = (new ShipMotion(physics, new System.Numerics.Vector3(o.Position.X, o.Position.Y, o.Position.Z), heading),
                    new System.Numerics.Quaternion(initial.X, initial.Y, initial.Z, initial.W), heading);
            }
            state.Motion.SetTarget(target.X, target.Y);
        }
    }

    public override void _Process(double delta)
    {
        foreach (var (o, (motion, initial, start)) in _moving)
        {
            var pose = motion.Step(delta);
            var q = MoveGeometry.Orientation(initial, pose.Heading - start, pose.Bank);
            _nodes[o].Transform = new Transform3D(new Basis(new Quaternion(q.X, q.Y, q.Z, q.W)),new Vector3(pose.Position.X, pose.Position.Y, pose.Position.Z));
        }
    }
}
