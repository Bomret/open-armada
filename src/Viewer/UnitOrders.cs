using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using OpenArmada.Assets;
using OpenArmada.Diagnostics;
using OpenArmada.Formats;
using OpenArmada.Sandbox;

namespace OpenArmada.Viewer;

/// <summary>
/// The orders of the map sandbox and the pose of every unit. A right-click on a unit hostile to a selected unit
/// gives that unit an attack order on it (a unit without weapons ignores the click). Any other right-click sends
/// the selected ships to where its ray meets the middle of the combat plane, clamped to the map's bounds; a ray
/// that misses the plane is ignored. A new order of either kind replaces the old one.
/// Each ship moves at its own height by its ShipMotion, fed by its physics definition, and follows the movement
/// its CombatWorld asks of it while it attacks. Stations and all other objects never move. Pose reads the
/// current pose of any unit, from the moment the map opens: the placed pose until the unit moves. Each frame
/// steps the CombatWorld and takes a destroyed unit out of the scene.
/// </summary>
public partial class UnitOrders : Node3D
{
    readonly UnitSelection _selection;
    readonly RepeatedFaults _faults = new(Area.Combat);
    readonly Dictionary<PlacedObject, Node3D> _nodes;

    // Scale is the node basis's scale (negative on all axes for a mirrored basis); the rotation is rebuilt around it.
    readonly Dictionary<
        PlacedObject,
        (ShipMotion Motion, System.Numerics.Quaternion Initial, float Heading, Vector3 Scale)
    > _moving = new();
    readonly UnitPoses _poses;
    readonly MovePlan _plan;
    readonly CombatWorld _world;
    readonly HashSet<PlacedObject> _fighting; // the units the world was built with, to tell the destroyed ones
    readonly float _planeHeight;
    readonly MapBounds _bounds;

    /// <summary>
    /// <c>planeHeight</c> is the middle of the combat plane, the camera rig's CombatPlaneHeight (the rig reports its
    /// stand-ins), so orders aim at the plane the camera looks at; <c>bounds</c> are the ones the rig clamps its look-at
    /// point to. <c>combat</c> holds every unit's combat values; the units fight in a CombatWorld that this node steps
    /// and whose state it shows through Combat. <c>seed</c> seeds the combat random source; null seeds it from the
    /// system, so every run differs.
    /// </summary>
    public UnitOrders(
        GameFileSystem files,
        SandboxPlan plan,
        UnitSelection selection,
        Dictionary<PlacedObject, Node3D> nodes,
        float planeHeight,
        MapBounds bounds,
        CombatPlan combat,
        Action<string> gap,
        Func<PlacedObject, string, ShipPose, System.Numerics.Vector3> hardpointPosition,
        int? seed = null
    )
    {
        _selection = selection;
        _nodes = nodes;
        _planeHeight = planeHeight;
        _bounds = bounds;
        _poses = new UnitPoses(selection.Units.Select(u => u.Object));
        _plan = MovePlan.Build(plan, selection.Units, files);
        foreach (var report in _plan.Reports)
        {
            gap(report.Text);
        }

        var random = seed is { } fixedSeed ? new System.Random(fixedSeed) : new System.Random();
        _world = new CombatWorld(
            _poses,
            selection
                .Units.Where(u => combat.Profiles.ContainsKey(u.Object))
                .Select(u => new CombatUnit
                {
                    Unit = u.Object,
                    Profile = combat.Profiles[u.Object],
                    DefinitionName = // inferred: per-target values name a definition with its `.odf` suffix (docs/combat-rules.md)
                        u.Object.Class + ".odf",
                    Radius = Radius(nodes[u.Object]),
                    Mobile = u.Movable && u.Unit is Ship && _plan.Physics.ContainsKey(u.Object),
                }),
            combat.ShieldDamageProtection,
            random.NextDouble,
            hardpointPosition,
            combat.SystemHitpointsBonus
        );
        _fighting = _world.Units.Select(u => u.Unit).ToHashSet();
    }

    /// <summary>The combat state of the units still in the fight.</summary>
    public CombatWorld Combat => _world;

    /// <summary>Raised with the events of each frame's step, before a destroyed unit's node leaves the scene.</summary>
    public event Action<IReadOnlyList<CombatEvent>>? Stepped;

    // Half the longest side of the box around the model's meshes, as the selection's picks take it; 0 without meshes.
    // stand-in: the unit radius is read from the mesh box; the game's collision radius is not known.
    static float Radius(Node3D node) =>
        VisibleMeshes.Extent(VisibleMeshes.Of(node), node.GlobalTransform) is { } box
            ? Math.Max(box.Size.X, Math.Max(box.Size.Y, box.Size.Z)) / 2
            : 0;

    /// <summary>The current pose of a unit of the map; throws KeyNotFoundException for an object that is no unit.</summary>
    public ShipPose Pose(PlacedObject unit) => _poses.Pose(unit);

    /// <inheritdoc/>
    public override void _UnhandledInput(InputEvent e)
    {
        if (e is not InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true } click)
            return;
        var camera = GetViewport().GetCamera3D();
        if (camera == null)
            return;
        var origin = camera.ProjectRayOrigin(click.Position);
        var direction = camera.ProjectRayNormal(click.Position);
        var clicked = _selection.UnitAt(click.Position);
        var hit = MoveGeometry.PlaneTarget(
            new System.Numerics.Vector3(origin.X, origin.Y, origin.Z),
            new System.Numerics.Vector3(direction.X, direction.Y, direction.Z),
            _planeHeight
        );
        // stand-in: an order outside the map goes to the nearest point of its edge; how the game handles such a click
        // is not known.
        var target = hit is { } point ? MoveGeometry.ClampToMap(point, _bounds) : (System.Numerics.Vector2?)null;
        foreach (var unit in _selection.Selected)
        {
            if (clicked != null && CombatRules.Hostile(unit.Object.Team, clicked.Object.Team))
            {
                _world.Attack(unit.Object, clicked.Object); // refused for a unit without weapons: it ignores the click

                continue;
            }

            // a ship with destroyed engines, or a derelict one, takes no move order
            if (
                target is not { } to
                || !unit.Movable
                || _world.State(unit.Object) is { Immobile: true }
                || Motion(unit.Object) is not { } motion
            )
                continue;
            _world.Move(unit.Object, to.X, to.Y);
            motion.SetTarget(to.X, to.Y);
        }
    }

    // The ShipMotion of a ship that can move, created on first use; null for a unit that never moves.
    ShipMotion? Motion(PlacedObject o)
    {
        if (_moving.TryGetValue(o, out var state))
            return state.Motion;
        if (!_plan.Physics.TryGetValue(o, out var physics) || !_nodes.TryGetValue(o, out var node))
            return null;
        var initial = node.Transform.Basis.GetRotationQuaternion();
        var heading = MoveGeometry.BowHeading(new System.Numerics.Vector3(o.AxisZ.X, o.AxisZ.Y, o.AxisZ.Z));
        var motion = new ShipMotion(
            physics,
            new System.Numerics.Vector3(o.Position.X, o.Position.Y, o.Position.Z),
            heading
        );
        _moving[o] = (
            motion,
            new System.Numerics.Quaternion(initial.X, initial.Y, initial.Z, initial.W),
            heading,
            node.Transform.Basis.Scale
        );

        return motion;
    }

    /// <summary>
    /// Logs the count of repeated step faults. Godot exits the children of a node before the node, so this runs
    /// before the run-end entry of the AssetBrowserView above the sandbox.
    /// </summary>
    public override void _ExitTree() => _faults.Flush();

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        foreach (var unit in _world.Units)
        {
            if (unit.CoastOvershoot is { } overshoot && Motion(unit.Unit) is { } coaster)
            {
                coaster.Coast(overshoot);
            }
            else if (unit.Intent.Kind != MoveKind.None && Motion(unit.Unit) is { } motion)
            {
                motion.Thrust();
                switch (unit.Intent.Kind)
                {
                    case MoveKind.MoveTo:
                        motion.SetTarget(unit.Intent.X, unit.Intent.Z);

                        break;
                    case MoveKind.Face:
                        motion.Face(unit.Intent.X, unit.Intent.Z);

                        break;
                    default:
                        motion.Stop();

                        break;
                }
            }
        }

        foreach (var (o, (motion, initial, start, scale)) in _moving)
        {
            _poses.Advance(o, motion, delta);
            var pose = motion.Drawn; // a coasting ship is drawn between its tick poses; the simulated pose stays on the tick
            var q = MoveGeometry.Orientation(initial, pose.Heading - start, pose.Bank);
            _nodes[o].Transform = new Transform3D(
                new Basis(new Quaternion(q.X, q.Y, q.Z, q.W)) * Basis.FromScale(scale),
                new Vector3(pose.Position.X, pose.Position.Y, pose.Position.Z)
            );
        }

        // A failure of one frame's combat never stops the sandbox (INV-0002); the next frame goes on.
        try
        {
            Stepped?.Invoke(_world.Step(delta));
        }
        catch (Exception e)
        {
            _faults.Report($"combat step failed: {e.Message}");
        }

        // A dying unit is deselected and cannot be selected again (0x4ab5d0, 0x4ab91e), so it takes no orders.
        foreach (var unit in _world.Units.Where(u => u.Dying))
        {
            _selection.Dismiss(unit.Unit);
        }

        // The world, not the step's events, says who is gone, so a failed step still takes its destroyed units out.
        foreach (var unit in _fighting.Where(u => _world.State(u) == null).ToList())
        {
            _fighting.Remove(unit);
            _moving.Remove(unit);
            _selection.Remove(unit);
        }
    }
}
