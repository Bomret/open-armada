using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using OpenArmada.Assets;
using OpenArmada.Sandbox;

namespace OpenArmada.Viewer;

// Selection of the map sandbox's units with the mouse: a left click selects the unit under the cursor, a
// drag selects every unit inside the box. Selected units show a ring and their unitName. The picks go
// through the viewport's active camera, in viewport coordinates, so they hold at any window size and
// display scale.
public partial class UnitSelection : Node3D
{
    readonly Dictionary<PlacedObject, Node3D> _nodes;
    readonly Dictionary<PlacedObject, SelectableUnit> _units;
    readonly Dictionary<SelectableUnit, (MeshInstance3D Ring, Label Name)> _marks = new();
    readonly SelectionBox _box = new() { MouseFilter = Control.MouseFilterEnum.Ignore };
    readonly CanvasLayer _layer = new();
    Vector2? _from;

    public UnitSelection(GameFileSystem files, SandboxPlan plan, Dictionary<PlacedObject, Node3D> nodes)
    {
        var unitPlan = UnitPlan.Build(files, plan, nodes.ContainsKey); // an object whose model is unreadable has no node and is no unit
        Reports = unitPlan.Reports;
        _nodes = nodes;
        _units = unitPlan.Units.ToDictionary(u => u.Object);
        Units = unitPlan.Units;
    }

    // Every unit of the scene, selected or not.
    public IReadOnlyList<SelectableUnit> Units { get; }

    // The gaps found while telling units apart.
    public IReadOnlyList<ReportEntry> Reports { get; }

    // The selected units; a ship whose Movable is false has no physics definition and takes no order.
    public IReadOnlyList<SelectableUnit> Selected => _marks.Keys.ToList();

    public override void _Ready()
    {
        _box.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _layer.AddChild(_box);
        AddChild(_layer);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left } button)
        {
            if (button.Pressed)
                _from = button.Position;
            else if (_from is { } from)
            {
                _from = null;
                _box.Box = null;
                if (BoxSelect.IsClick(ToNumerics(from), ToNumerics(button.Position)))
                    Pick(button.Position);
                else
                    Box(from, button.Position);
            }
        }
        else if (e is InputEventMouseMotion motion && _from is { } start)
            _box.Box = new Rect2(start, Vector2.Zero).Expand(motion.Position);
    }

    // The nearest object under the cursor; a unit is selected, anything else clears the selection.
    void Pick(Vector2 at)
    {
        var camera = GetViewport().GetCamera3D();
        if (camera == null)
            return;
        var origin = camera.ProjectRayOrigin(at);
        var direction = camera.ProjectRayNormal(at);
        // ponytail: the box around the model's meshes is the hit shape (a non-unit object holding the camera
        // in its box uses the entry of its bounding sphere, and blocks nothing when the camera is inside the
        // sphere); a mesh-exact pick if overlaps matter
        var hit = BoxSelect.Nearest(
            _nodes.Select(pair =>
            {
                if (Extent(pair.Value) is not { } box)
                    return (pair.Key, (float?)null, false, false, (float?)null);
                var radius = Math.Max(box.Size.X, Math.Max(box.Size.Y, box.Size.Z)) / 2;
                return (
                    pair.Key,
                    Enters(box, origin, direction),
                    _units.ContainsKey(pair.Key),
                    box.HasPoint(origin),
                    BoxSelect.SphereEntry(
                        ToNumerics(box.GetCenter()),
                        radius,
                        ToNumerics(origin),
                        ToNumerics(direction)
                    )
                );
            })
        );
        Select(hit != null && _units.TryGetValue(hit, out var unit) ? new[] { unit } : Array.Empty<SelectableUnit>());
    }

    void Box(Vector2 from, Vector2 to)
    {
        var camera = GetViewport().GetCamera3D();
        if (camera == null)
            return;
        var entries = _units.Values.Select(u =>
        {
            var position = _nodes[u.Object].GlobalPosition;
            return (u, ToNumerics(camera.UnprojectPosition(position)), !camera.IsPositionBehind(position));
        });
        Select(BoxSelect.Inside(entries, ToNumerics(from), ToNumerics(to)));
    }

    void Select(IReadOnlyList<SelectableUnit> units)
    {
        foreach (var (ring, name) in _marks.Values)
        {
            ring.QueueFree();
            name.QueueFree();
        }
        _marks.Clear();
        foreach (var unit in units)
        {
            var node = _nodes[unit.Object];
            var size = Extent(node)?.Size ?? Vector3.One;
            var radius = Math.Max(size.X, size.Z) * 0.6f;
            var ring = new MeshInstance3D
            {
                Mesh = new TorusMesh { InnerRadius = radius, OuterRadius = radius * 1.04f },
                MaterialOverride = new StandardMaterial3D
                {
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                    AlbedoColor = Colors.Yellow,
                },
            };
            node.AddChild(ring);
            ring.TopLevel = true; // follows the ship's position only (see _Process), so the ring stays level when the ship banks
            ring.GlobalPosition = node.GlobalPosition;
            var name = new Label { Text = unit.Unit.Name };
            _layer.AddChild(name);
            _marks[unit] = (ring, name);
        }
    }

    public override void _Process(double delta)
    {
        var camera = GetViewport().GetCamera3D();
        if (camera == null)
            return;
        foreach (var (unit, (ring, name)) in _marks)
        {
            var position = _nodes[unit.Object].GlobalPosition;
            ring.GlobalPosition = position;
            name.Visible = !camera.IsPositionBehind(position);
            name.Position = camera.UnprojectPosition(position) + new Vector2(8, 8);
        }
    }

    // The distance along the ray at which it enters the box; null when it misses the box or the box lies
    // behind the origin. The slab test: the ray is inside each axis' slab from `near` to `far`.
    static float? Enters(Aabb box, Vector3 origin, Vector3 direction)
    {
        float near = 0,
            far = float.MaxValue;
        for (var axis = 0; axis < 3; axis++)
        {
            var from = (box.Position[axis] - origin[axis]) / direction[axis];
            var to = (box.End[axis] - origin[axis]) / direction[axis];
            if (float.IsNaN(from) || float.IsNaN(to))
            {
                if (origin[axis] < box.Position[axis] || origin[axis] > box.End[axis])
                    return null;
                continue;
            }
            near = Math.Max(near, Math.Min(from, to));
            far = Math.Min(far, Math.Max(from, to));
        }
        return near <= far ? near : null;
    }

    // The box around the object's visible meshes in world space; null when it has none.
    static Aabb? Extent(Node3D node) => // the selection ring is no part of the model
        VisibleMeshes.Extent(VisibleMeshes.Of(node).Where(m => m.Mesh is not TorusMesh), node.GlobalTransform);

    static System.Numerics.Vector2 ToNumerics(Vector2 v) => new(v.X, v.Y);

    static System.Numerics.Vector3 ToNumerics(Vector3 v) => new(v.X, v.Y, v.Z);
}

// The rubber band of a drag.
public partial class SelectionBox : Control
{
    Rect2? _box;

    public Rect2? Box
    {
        get => _box;
        set
        {
            _box = value;
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        if (_box is not { } box)
            return;
        DrawRect(box, new Color(1, 1, 0, 0.15f));
        DrawRect(box, Colors.Yellow, false);
    }
}
