using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using OpenArmada.Assets;
using OpenArmada.Diagnostics;
using OpenArmada.Formats;
using OpenArmada.Rendering;
using OpenArmada.Sandbox;

namespace OpenArmada.Viewer;

/// <summary>
/// The orders of the map sandbox and the pose of every unit. A right-click on a unit hostile to a selected unit
/// gives that unit an attack order on it (a unit without weapons ignores the click). Any other right-click sends
/// the selected ships to where its ray meets the middle of the combat plane, clamped to the map's bounds, as a group
/// move (CombatWorld.GroupMove: one ship goes to the point, several to the cells of the game's group formation around
/// it); a ray that misses the plane is ignored. A new order of either kind replaces the old one.
/// Each ship flies by its FlightModel, fed by its physics definition, and follows the movement
/// its CombatWorld asks of it while it attacks. Stations and all other objects never move. Pose reads the
/// current pose of any unit, from the moment the map opens: the placed pose until the unit moves. Each frame
/// steps the CombatWorld, decides each unit's showing damage nodes once (ShowingNodes), and takes a destroyed unit
/// out of the scene.
/// </summary>
public partial class UnitOrders : Node3D
{
    readonly UnitSelection _selection;
    readonly RepeatedFaults _faults = new(Area.Combat);
    readonly Dictionary<PlacedObject, Node3D> _nodes;

    // Scale is the node basis's scale (HardpointGeometry.Split: negative on the side axis for a mirrored basis); the pose's rotation is built around it.
    readonly Dictionary<PlacedObject, (FlightModel Motion, Vector3 Scale)> _moving = new();
    readonly UnitPoses _poses;
    readonly MovePlan _plan;
    readonly CombatWorld _world;
    readonly HashSet<PlacedObject> _fighting; // the units the world was built with, to tell the destroyed ones
    readonly float _planeHeight;
    readonly float _spacing; // FORMATION_SPACING, the cells of a group move
    readonly MapBounds _bounds;
    readonly Func<PlacedObject, Sod?> _model;

    // Each unit's damage group and the nodes below it that the view toggles (never sprite quads, which draw themselves).
    readonly Dictionary<PlacedObject, (Node3D Group, List<Node3D> Nodes)?> _damage = new();

    // The damage nodes that show on each unit in the world this frame, decided once per frame after the step.
    readonly Dictionary<PlacedObject, IReadOnlySet<Sod.Node>> _showing = new();
    static readonly IReadOnlySet<Sod.Node> NoNodes = new HashSet<Sod.Node>();

    /// <summary>
    /// <c>band</c> is the camera rig's combat band (the rig reports its stand-ins): its middle is the plane orders aim
    /// at, the plane the camera looks at, and its limits clamp the steering's climb and sink; <c>bounds</c> are the ones the rig clamps its look-at
    /// point to. <c>move</c> holds the physics definitions of the ships that can move. <c>combat</c> holds every unit's combat values; the units fight in a CombatWorld that this node steps
    /// and whose state it shows through Combat. <c>model</c> reads a unit's model for its damage nodes. <c>seed</c> seeds the combat random source; null seeds it from the
    /// system, so every run differs.
    /// </summary>
    public UnitOrders(
        GameFileSystem files,
        SandboxPlan plan,
        UnitSelection selection,
        Dictionary<PlacedObject, Node3D> nodes,
        CombatBand band,
        MapBounds bounds,
        CombatPlan combat,
        MovePlan move,
        Action<string> gap,
        Func<PlacedObject, string, ShipPose, System.Numerics.Vector3> hardpointPosition,
        Func<PlacedObject, Sod?> model,
        int? seed = null
    )
    {
        _model = model;
        _selection = selection;
        _nodes = nodes;
        _planeHeight = band.Middle;
        _spacing = combat.FormationSpacing;
        _bounds = bounds;
        _poses = new UnitPoses(selection.Units.Select(u => u.Object));
        _plan = move;

        var spriteNodes = ShieldEllipsoid.SpriteNodeLines(files);
        ShieldEllipsoid? Shield(PlacedObject o, CombatProfile profile)
        {
            if (model(o) is not { } sod)
                return null;
            var shield = ShieldEllipsoid.Of(sod, spriteNodes, profile.ShieldPad);
            if (shield == null)
            {
                gap(
                    $"{o.Model}: model has no shield ellipsoid (its box is flat on an axis), it shows no shield hit effect"
                );
            }

            return shield;
        }

        // The class load leaves out each target hardpoint the model lacks (0x460f30); the gap names them once per model.
        var reported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CombatProfile Sparking(PlacedObject o)
        {
            var (profile, missing) = TargetHardpoints.Keep(combat.Profiles[o], model(o));
            if (missing.Count > 0 && reported.Add(o.Model))
            {
                var text =
                    $"{o.Model}: target hardpoints missing from the model, no repair spark shows there: {string.Join(", ", missing)}";

                Log.Warn(Area.Combat, text);

                gap(text);
            }

            return profile;
        }

        // obstacle avoidance reads each class's avoidMe, avoidanceClass, and collisionRadius; a bad value is logged (by
        // ByClass) and shown as a gap once per class
        var traitsOf = AvoidanceTraits.ByClass(plan.Resolved, gap);

        var random = seed is { } fixedSeed ? new System.Random(fixedSeed) : new System.Random();
        var stations = selection
            .Units.Where(u => u.Unit is Station && model(u.Object) != null)
            .Select(u => PathPlanning.Footprint(u.Object, model(u.Object)!, spriteNodes))
            .OfType<PathStation>();
        _world = new CombatWorld(
            _poses,
            selection
                .Units.Where(u => combat.Profiles.ContainsKey(u.Object))
                .Select(u => new CombatUnit
                {
                    Unit = u.Object,
                    Profile = Sparking(u.Object),
                    ModelRadius = model(u.Object) is { } sod ? ShieldEllipsoid.BoundingRadius(sod, spriteNodes) : 0,
                    DefinitionName = // inferred: per-target values name a definition with its `.odf` suffix (docs/rules/combat-rules.md)
                        u.Object.Class + ".odf",
                    Radius = Radius(nodes[u.Object]),
                    Avoidance = traitsOf(u.Object.Class),
                    Starbase = u.Unit is Station,
                    Mobile = u.Movable && u.Unit is Ship && _plan.Physics.ContainsKey(u.Object),
                    Motion = u.Movable && u.Unit is Ship ? Motion(u.Object) : null,
                    Shield = Shield(u.Object, combat.Profiles[u.Object]),
                    Model = model(u.Object),
                }),
            combat.ShieldDamageProtection,
            random.NextDouble,
            hardpointPosition,
            combat.SystemHitpointsBonus,
            combat.Debris
        )
        {
            MaxPreventionRotation = combat.MaxPreventionRotation,
            Band = band,
            Avoidance = combat.Avoidance,
            // a planet's radius is its model's bounding radius (+0x60), the same value a ship's radius is
            Obstacles = plan.Obstacles(o => model(o) is { } sod ? ShieldEllipsoid.BoundingRadius(sod, spriteNodes) : 0),
            BlackHoles = plan.BlackHoles,
            Nebulae = plan.NebulaGrid,
            Planner = PathPlanning.Create(files, plan, combat.FormationSpacing, stations, gap),
        };
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

    /// <summary>
    /// The nodes of the unit's model that show this frame, as <see cref="DamageNodes.ShowingNodes"/> decides them once
    /// per frame after the step; empty for a unit without a model or out of the world. A node later in the scene reads
    /// the current frame's nodes.
    /// </summary>
    public IReadOnlySet<Sod.Node> ShowingNodes(PlacedObject unit) => _showing.GetValueOrDefault(unit) ?? NoNodes;

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
        // stand-in: a dying unit is unselectable, so UnitAt never returns it and a right-click on it gives the selected
        // ships a move order to the plane point under it; the game's order code on a dying unit is not recovered.
        var clicked = _selection.UnitAt(click.Position);
        var hit = MoveGeometry.PlaneTarget(
            new System.Numerics.Vector3(origin.X, origin.Y, origin.Z),
            new System.Numerics.Vector3(direction.X, direction.Y, direction.Z),
            _planeHeight
        );
        // stand-in: an order outside the map goes to the nearest point of its edge; how the game handles such a click
        // is not known.
        var target = hit is { } point ? MoveGeometry.ClampToMap(point, _bounds) : (System.Numerics.Vector2?)null;
        // The selection in the order the selection marks it: a box takes the units in the scene's unit order, which the
        // layout's object order fixes, so one run's order is every run's.
        var moving = new List<PlacedObject>();
        foreach (var unit in _selection.Selected)
        {
            if (clicked != null && HostileUnits(unit.Object, clicked.Object))
            {
                _world.Attack(unit.Object, clicked.Object); // refused for a unit without weapons: it ignores the click

                continue;
            }

            moving.Add(unit.Object);
        }

        // The ships that take a move order go to the group formation around the click (the world drops the ones that
        // cannot: a station, a ship with destroyed engines, a derelict one, or a dying one).
        if (target is { } to)
            _world.GroupMove(moving, new System.Numerics.Vector2(to.X, to.Y), _spacing, _planeHeight, _bounds);
    }

    // The FlightModel of a ship that can move, which the combat world steps; made once. Null for a unit that never moves.
    // Whether the units are hostile by their current teams (INV-0012); a unit the world has lost is hostile to none.
    bool HostileUnits(PlacedObject a, PlacedObject b) =>
        _world.State(a) is { } x && _world.State(b) is { } y && CombatRules.Hostile(x.Team, y.Team);

    FlightModel? Motion(PlacedObject o)
    {
        if (_moving.TryGetValue(o, out var state))
            return state.Motion;
        if (!_plan.Physics.TryGetValue(o, out var physics) || !_nodes.TryGetValue(o, out var node))
            return null;
        var motion = new FlightModel(physics, MoveGeometry.PlacedPose(o));
        var scale = HardpointGeometry.Split(o).Scale;
        _moving[o] = (motion, new Vector3(scale.X, scale.Y, scale.Z));

        return motion;
    }

    /// <summary>
    /// Logs the count of repeated step faults. Godot exits the children of a node before the node, so this runs
    /// before the run-end entry of the AssetBrowserView above the sandbox.
    /// </summary>
    public override void _ExitTree() => _faults.Flush();

    // Shows exactly the damage nodes the sandbox decides for this frame (ShowingNodes) on the unit's model and hides the
    // rest of the damage group. A model without a damage group shows nothing and is not reported: it is not broken.
    void ShowDamage(UnitCombatState unit)
    {
        if (!_nodes.TryGetValue(unit.Unit, out var root) || !IsInstanceValid(root))
            return;
        if (!_damage.TryGetValue(unit.Unit, out var found))
        {
            found = root.FindChildren("*", "", true, false).OfType<Node3D>().FirstOrDefault(n => IsDamageGroup(n))
                is { } group
                ? (
                    group,
                    group
                        .FindChildren("*", "", true, false)
                        .OfType<Node3D>()
                        .Where(n => n is not SpriteNodeQuad)
                        .ToList()
                )
                : null;
            _damage[unit.Unit] = found;
        }

        if (found is not { } groupAndNodes)
            return;
        var (damage, nodes) = groupAndNodes;
        var model = _model(unit.Unit);
        var showing = ShowingNodes(unit.Unit);
        damage.Visible = showing.Count > 0;
        foreach (var node in nodes)
        {
            // The builder stamps each node with its index in the model, so equal names under different parents differ.
            node.Visible =
                model != null
                && node.HasMeta(SodBuilder.SodIndexMeta)
                && (int)node.GetMeta(SodBuilder.SodIndexMeta) is var index
                && index >= 0
                && index < model.Nodes.Count
                && showing.Contains(model.Nodes[index]);
        }
    }

    static bool IsDamageGroup(Node3D node) => ((string)node.Name).Equals("damage", StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        // A failure of one frame's combat never stops the sandbox (INV-0002); the next frame goes on.
        IReadOnlyList<CombatEvent> events = [];
        try
        {
            events = _world.Step(delta);
        }
        catch (Exception e)
        {
            _faults.Report($"combat step failed: {e.Message}");
        }

        // The world moves the ships on its ticks; the view draws each between its tick poses (the simulated pose
        // stays on the tick), before the step's events reach the nodes that read the drawn transforms.
        foreach (var (o, (motion, scale)) in _moving)
        {
            var pose = motion.Drawn(_world.Pending);
            var q = pose.Orientation;
            _nodes[o].Transform = new Transform3D(
                new Basis(new Quaternion(q.X, q.Y, q.Z, q.W)) * Basis.FromScale(scale),
                new Vector3(pose.Position.X, pose.Position.Y, pose.Position.Z)
            );
        }

        try
        {
            Stepped?.Invoke(events);
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

        _showing.Clear();
        foreach (var unit in _world.Units)
        {
            try
            {
                _showing[unit.Unit] = DamageNodes.ShowingNodes(_model(unit.Unit), unit).ToHashSet();
                ShowDamage(unit);
            }
            catch (Exception e) // a failure of one unit's damage nodes never stops the sandbox (INV-0002)
            {
                _faults.Report($"damage nodes failed: {e.Message}");
            }
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
