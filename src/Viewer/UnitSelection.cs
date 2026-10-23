using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using OpenArmada.Assets;
using OpenArmada.Sandbox;

namespace OpenArmada.Viewer;

/// <summary>
/// Selection of the map sandbox's units with the mouse: a left click selects the unit under the cursor, a
/// drag selects every unit inside the box. Selected units show a ring and their unitName. The picks go
/// through the viewport's active camera, in viewport coordinates, so they hold at any window size and
/// display scale.
/// </summary>
public partial class UnitSelection : Node3D
{
    readonly Dictionary<PlacedObject, Node3D> _nodes;
    readonly Dictionary<PlacedObject, SelectableUnit> _units;
    readonly Dictionary<SelectableUnit, (MeshInstance3D Ring, Label Name)> _marks = new();
    readonly Dictionary<PlacedObject, Aabb> _lastBoxes = new();
    readonly SelectionBox _box = new() { MouseFilter = Control.MouseFilterEnum.Ignore };
    readonly CanvasLayer _layer = new();
    Vector2? _from;

    /// <summary>
    /// Tells the units of <c>plan</c> apart among the objects in <c>nodes</c> (the shown node of each placed object);
    /// an object without a node is no unit. Adds no child nodes until <see cref="_Ready"/>.
    /// </summary>
    public UnitSelection(GameFileSystem files, SandboxPlan plan, Dictionary<PlacedObject, Node3D> nodes)
    {
        var unitPlan = UnitPlan.Build(files, plan, nodes.ContainsKey); // an object whose model is unreadable has no node and is no unit
        Reports = unitPlan.Reports;
        _nodes = nodes;
        _units = unitPlan.Units.ToDictionary(u => u.Object);
    }

    /// <summary>Every unit of the scene that is not destroyed, selected or not.</summary>
    public IReadOnlyList<SelectableUnit> Units => _units.Values.ToList();

    /// <summary>The gaps found while telling units apart.</summary>
    public IReadOnlyList<ReportEntry> Reports { get; }

    /// <summary>The selected units; a ship whose Movable is false has no physics definition and takes no order.</summary>
    public IReadOnlyList<SelectableUnit> Selected => _marks.Keys.ToList();

    /// <inheritdoc/>
    public override void _Ready()
    {
        _box.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _layer.AddChild(_box);
        AddChild(_layer);
    }

    /// <inheritdoc/>
    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left } button)
        {
            if (button.Pressed)
            {
                _from = button.Position;
            }
            else if (_from is { } from)
            {
                _from = null;
                _box.Box = null;
                if (BoxSelect.IsClick(ToNumerics(from), ToNumerics(button.Position)))
                {
                    Pick(button.Position);
                }
                else
                {
                    Box(from, button.Position);
                }
            }
        }
        else if (e is InputEventMouseMotion motion && _from is { } start)
        {
            _box.Box = new Rect2(start, Vector2.Zero).Expand(motion.Position);
        }
    }

    // The nearest object under the cursor; a unit is selected, anything else clears the selection.
    void Pick(Vector2 at)
    {
        if (GetViewport().GetCamera3D() == null)
            return;
        var unit = UnitAt(at);
        Select(unit != null ? new[] { unit } : Array.Empty<SelectableUnit>());
    }

    /// <summary>
    /// Takes a dying unit out of the selection: it is deselected and no click or box picks it again. Its node stays in
    /// the scene, and a click on it picks no unit. Its world box, while its node shows a mesh, is kept as the box it
    /// has once destroyed. A unit that is not a unit of the scene changes nothing.
    /// </summary>
    public void Dismiss(PlacedObject unit)
    {
        if (_units.Remove(unit, out var selectable) && _marks.Remove(selectable, out var mark))
        {
            mark.Ring.QueueFree();
            mark.Name.QueueFree();
        }

        if (_nodes.TryGetValue(unit, out var node) && Extent(node) is { } box)
            _lastBoxes[unit] = box;
    }

    /// <summary>
    /// Takes a destroyed unit out of the scene: its node is freed and it is no longer picked or selected. Its world box
    /// of the last frame its node showed a mesh in stays for <see cref="ScreenBoxOf"/>.
    /// </summary>
    public void Remove(PlacedObject unit)
    {
        Dismiss(unit);
        if (_nodes.Remove(unit, out var node))
            node.QueueFree();
    }

    /// <summary>
    /// The unit that is the nearest object under the cursor; null when the nearest object is no unit or there is
    /// none.
    /// </summary>
    public SelectableUnit? UnitAt(Vector2 at)
    {
        var camera = GetViewport().GetCamera3D();
        if (camera == null)
            return null;
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

        return hit != null && _units.TryGetValue(hit, out var unit) ? unit : null;
    }

    /// <summary>
    /// The unit's screen box in viewport pixels (docs/rules/selection-display.md); null when the unit has no visible
    /// mesh, has a corner behind the camera, or lies wholly off screen. A dying unit keeps its node and so its box, and a
    /// dying unit that shows no mesh, or a destroyed unit, has the world box of the last frame it showed a mesh in, seen
    /// through the current camera.
    /// </summary>
    public ScreenBox? ScreenBoxOf(PlacedObject unit)
    {
        var camera = GetViewport().GetCamera3D();
        // stand-in: the box around the model's meshes in world axes; whether the game's box turns with the unit is unknown
        var extent =
            (_nodes.TryGetValue(unit, out var node) ? Extent(node) : null)
            ?? (_lastBoxes.TryGetValue(unit, out var last) ? last : null);
        if (camera == null || extent is not { } box)
            return null;
        var corners = Enumerable
            .Range(0, 8)
            .Select(i => box.GetEndpoint(i))
            .Select(c =>
                camera.IsPositionBehind(c) ? (System.Numerics.Vector2?)null : ToNumerics(camera.UnprojectPosition(c))
            );

        return SelectionDisplay.BoxOf(corners, ToNumerics(GetViewport().GetVisibleRect().Size));
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

    /// <inheritdoc/>
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

/// <summary>The rubber band of a drag.</summary>
public partial class SelectionBox : Control
{
    Rect2? _box;

    /// <summary>The drag's rectangle in viewport coordinates; null draws nothing. Setting it queues a redraw.</summary>
    public Rect2? Box
    {
        get => _box;
        set
        {
            _box = value;
            QueueRedraw();
        }
    }

    /// <inheritdoc/>
    public override void _Draw()
    {
        if (_box is not { } box)
            return;
        DrawRect(box, new Color(1, 1, 0, 0.15f));
        DrawRect(box, Colors.Yellow, false);
    }
}
