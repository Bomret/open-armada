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
    readonly Dictionary<PlacedObject, (ShipMotion Motion, Basis Initial, float Heading)> _moving = new();
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
            if (!constants.TryNumber("SHIP_COMBAT_FLOOR", out var floor, out var p1)) throw new InvalidOperationException(p1);
            if (!constants.TryNumber("SHIP_COMBAT_CEILING", out var ceiling, out var p2)) throw new InvalidOperationException(p2);
            _planeHeight = (float)((floor + ceiling) / 2);
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
        // the ray meets the plane ahead of the origin; parallel or pointing away it is ignored
        var along = (_planeHeight - origin.Y) / direction.Y;
        if (!float.IsFinite(along) || along <= 0) return;
        var target = origin + direction * along;
        foreach (var unit in _selection.Selected)
        {
            if (!unit.Movable || !_plan.Physics.TryGetValue(unit.Object, out var physics) || !_nodes.TryGetValue(unit.Object, out var node)) continue;
            if (!_moving.TryGetValue(unit.Object, out var state))
            {
                var o = unit.Object;
                var heading = MathF.Atan2(o.AxisZ.X, o.AxisZ.Z); // the model's +Z is its bow
                _moving[o] = state = (new ShipMotion(physics, new System.Numerics.Vector3(o.Position.X, o.Position.Y, o.Position.Z), heading),
                    node.Transform.Basis, heading);
            }
            state.Motion.SetTarget(target.X, target.Z);
        }
    }

    public override void _Process(double delta)
    {
        foreach (var (o, (motion, initial, start)) in _moving)
        {
            var pose = motion.Step(delta);
            var basis = new Basis(Vector3.Up, pose.Heading - start) * initial * new Basis(Vector3.Back, pose.Bank);
            _nodes[o].Transform = new Transform3D(basis, new Vector3(pose.Position.X, pose.Position.Y, pose.Position.Z));
        }
    }
}
