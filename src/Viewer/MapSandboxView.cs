using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using OpenArmada.Assets;
using OpenArmada.Formats;
using OpenArmada.Rendering;
using OpenArmada.Sandbox;

namespace OpenArmada.Viewer;

// The map sandbox: one map of the install as a live 3D scene, built from the scene plan.
//   godot-mono --path . -- --game <Armada dir> --map <name> [--screenshot out.png]
// Each later feature of the sandbox is one more node added in _Ready (the camera, the asteroid fields, the
// selection, the orders). A gap of the map or of the sandbox is printed and counted on screen; the map
// still opens (INV-0002).
public partial class MapSandboxView : Node3D
{
    readonly MapSandbox _sandbox;
    readonly string? _screenshot;
    readonly Dictionary<string, Sod> _sods = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<(string Model, int Slot), Node3D> _templates = new();
    readonly List<string> _gaps = new();
    readonly HashSet<string> _unreadable = new(StringComparer.OrdinalIgnoreCase);
    Node3D? _background;
    Vector3 _backgroundOffset;
    Camera3D? _camera;
    // Null while the map shows a problem instead of the scene (_Ready returns before the rig is built).
    CameraRig? _rig;
    bool _mouseInWindow = true;
    int _frames;

    public MapSandboxView(MapSandbox sandbox, string? screenshot)
    {
        _sandbox = sandbox;
        _screenshot = screenshot;
    }

    public override void _Ready()
    {
        if (_sandbox.Problem != null)
        {
            Show(_sandbox.Problem);
            return;
        }
        var window = GetWindow();
        window.MouseEntered += () => _mouseInWindow = true;
        window.MouseExited += () => _mouseInWindow = false;
        var files = _sandbox.Files!;
        var plan = _sandbox.Plan!;
        var team = TeamColors.Load(files);
        var builder = new SodBuilder(files);
        _gaps.AddRange(team.Notes);
        _gaps.AddRange(plan.Reports.Select(r => r.Text));

        // The scene's ambient light is the map's. stand-in: the directional light (its direction and
        // energy), the ambient energy of 1 and the black clear color; the install defines none.
        AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = Colors.Black,
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color(plan.Ambient.X, plan.Ambient.Y, plan.Ambient.Z),
                AmbientLightEnergy = 1f,
            },
        });
        AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-50, -30, 0) });

        var nodes = new Dictionary<PlacedObject, Node3D>();
        foreach (var o in plan.Objects)
        {
            var node = Instance(files, builder, team, o.Model, o.Slot);
            if (node == null) continue;
            node.Transform = new Transform3D(new Basis(ToGodot(o.AxisX), ToGodot(o.AxisY), ToGodot(o.AxisZ)), ToGodot(o.Position));
            AddChild(node);
            nodes[o] = node;
        }
        var selection = new UnitSelection(files, plan, nodes);
        AddChild(selection);
        _gaps.AddRange(selection.Reports.Select(r => r.Text));

        // The rig reads the game constants once per start; move orders aim at its combat plane.
        var bounds = plan.Bounds;
        try { _rig = new CameraRig(GameConstantsKind.Of(files), bounds); }
        catch (Exception e) // no readable RTS_CFG.h: the camera uses its stand-ins (INV-0002)
        {
            AddGap($"{GameConstantsKind.FileName} cannot be read: {e.Message}");
            _rig = new CameraRig(null, bounds);
        }
        foreach (var problem in _rig.Problems) AddGap(problem);
        AddChild(new MoveOrders(files, plan, selection, nodes, (float)_rig.CombatPlaneHeight, AddGap));

        AddChild(new AsteroidFieldsView(files, plan, AddGap));

        // The sandbox's reach: no camera is farther from an object than the rig's greatest eye distance from its
        // look-at point (which stays within the map) plus the map's diagonal.
        var reach = (float)_rig.MaxDistance + MathF.Sqrt(bounds.Width * bounds.Width + bounds.Depth * bounds.Depth);
        // stand-in: the camera's field of view and near plane; the far plane covers the map and the background.
        var camera = new Camera3D { Fov = 60, Near = 1, Far = reach * 4, Current = true };
        AddChild(camera);
        _camera = camera;
        PlaceCamera();
        if (plan.Background != null) AddBackground(files, builder, team, plan.Background, camera, reach);
        Show(null);
    }

    // The model of the name with its team materials in the slot's color: the first instance is built, later
    // ones copy it (they share its meshes and materials, so animated textures run once).
    Node3D? Instance(GameFileSystem files, SodBuilder builder, TeamColors team, string model, int slot)
    {
        if (_templates.TryGetValue((model, slot), out var template)) return (Node3D)template.Duplicate();
        if (_unreadable.Contains(model)) return null;
        try
        {
            if (!_sods.TryGetValue(model, out var sod)) _sods[model] = sod = Sod.Read(files.ReadAllBytes(model + ".sod"));
            var node = builder.Build(sod, team.Color(TeamColors.SlotNames[slot]));
            foreach (var problem in builder.Problems) AddGap($"{model}: {problem}");
            _templates[(model, slot)] = node;
            return node;
        }
        catch (Exception e) // a model that cannot be read is a gap, whatever the reason, and is read once (INV-0002)
        {
            _unreadable.Add(model);
            AddGap($"model '{model}' cannot be read: {e.Message}");
            return null;
        }
    }

    void AddGap(string gap)
    {
        if (!_gaps.Contains(gap)) _gaps.Add(gap);
    }

    // The background model sits behind every other object: it is scaled until its origin's nearest distance to
    // a face of its box passes the sandbox's reach, kept with its box center (its origin when inside) on the camera, and the far plane
    // passes its farthest corner, so no camera position or rotation clips it or puts an object behind it.
    // A model whose origin lies outside its box is centered on the box first, so the camera is inside it.
    void AddBackground(GameFileSystem files, SodBuilder builder, TeamColors team, string model, Camera3D camera, float reach)
    {
        var node = Instance(files, builder, team, model, 0);
        if (node == null) return;
        var box = Extent(node);
        var inside = box.Position.X < 0 && box.Position.Y < 0 && box.Position.Z < 0 && box.End.X > 0 && box.End.Y > 0 && box.End.Z > 0;
        var shift = inside ? Vector3.Zero : -box.GetCenter();
        box.Position += shift;
        var low = -box.Position;
        var high = box.End;
        var nearest = MathF.Min(MathF.Min(MathF.Min(low.X, high.X), MathF.Min(low.Y, high.Y)), MathF.Min(low.Z, high.Z));
        var farthest = new[] { low.X, high.X }.SelectMany(x => new[] { low.Y, high.Y }.SelectMany(y => new[] { low.Z, high.Z }.Select(z => new Vector3(x, y, z).Length()))).Max();
        // stand-in: the factor 1.5 of margin past the reach and past the farthest corner.
        var scale = reach * 1.5f / MathF.Max(nearest, 0.001f);
        node.Transform = new Transform3D(Basis.FromScale(Vector3.One * scale), shift * scale);
        AddChild(node);
        camera.Far = MathF.Max(camera.Far, farthest * scale * 1.5f);
        _background = node;
        _backgroundOffset = shift * scale;
        _camera = camera;
    }

    // The box around the visible meshes of the model, in the root's own frame (the root's transform left out).
    static Aabb Extent(Node3D root)
    {
        Aabb? box = null;
        void Walk(Node3D n, Transform3D t)
        {
            if (!n.Visible) return;
            if (n != root) t *= n.Transform;
            if (n is MeshInstance3D m) box = box?.Merge(t * m.GetAabb()) ?? t * m.GetAabb();
            foreach (var child in n.GetChildren().OfType<Node3D>()) Walk(child, t);
        }
        Walk(root, Transform3D.Identity);
        return box ?? new Aabb(-Vector3.One, Vector3.One * 2);
    }

    // Prints the gaps and shows them on a label with their count, or a problem in place of the scene.
    void Show(string? problem)
    {
        var text = problem ?? (_gaps.Count == 0 ? "" : $"{_gaps.Count} gaps:\n{string.Join("\n", _gaps)}");
        if (text.Length > 0) GD.PrintErr(text);
        var layer = new CanvasLayer();
        AddChild(layer);
        layer.AddChild(new Label { Text = text, Position = new Vector2(8, 8), AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(600, 0) });
    }

    static Vector3 ToGodot(System.Numerics.Vector3 v) => new(v.X, v.Y, v.Z);

    // The rig's pose in Godot's frame (Z mirrored).
    void PlaceCamera()
    {
        var (eye, target) = _rig!.Pose;
        _camera!.Position = new Vector3(eye.X, eye.Y, -eye.Z);
        _camera.LookAt(new Vector3(target.X, target.Y, -target.Z));
    }

    // The wheel changes the height (stand-in: the game uses a vertical drag), a middle-button drag rotates.
    public override void _Input(InputEvent e)
    {
        if (_rig == null) return;
        if (e is InputEventMouseButton { Pressed: true } b && b.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
            _rig.ChangeHeight(b.ButtonIndex == MouseButton.WheelUp ? -_rig.ZoomStep : _rig.ZoomStep);
        else if (e is InputEventMouseMotion m && m.ButtonMask.HasFlag(MouseButtonMask.Middle))
            _rig.Rotate(m.Relative.X * _rig.RotationPerPixel);
    }

    public override void _Process(double delta)
    {
        if (_rig != null)
        {
            var keys = new Vector2(Input.GetAxis("ui_left", "ui_right"), Input.GetAxis("ui_down", "ui_up"));
            var viewport = GetViewport();
            var mouse = viewport.GetMousePosition();
            var size = viewport.GetVisibleRect().Size;
            // The view tracks the window's mouse enter and exit and treats the cursor as outside while the window
            // is exited, passing the rig a negative position (the rig counts that as outside).
            var cursor = _mouseInWindow ? new System.Numerics.Vector2(mouse.X, mouse.Y) : new System.Numerics.Vector2(-1, -1);
            _rig.Scroll(cursor, new System.Numerics.Vector2(size.X, size.Y),
                GetWindow().HasFocus(), new System.Numerics.Vector2(keys.X, keys.Y), delta);
            PlaceCamera();
        }
        // The background keeps its centering offset from the camera and never turns with it.
        if (_background != null && _camera != null) _background.GlobalPosition = _camera.GlobalPosition + _backgroundOffset;
        // Wait a few frames so textures are uploaded before capturing.
        if (_screenshot == null || ++_frames < 5) return;
        GetViewport().GetTexture().GetImage().SavePng(_screenshot);
        GetTree().Quit();
    }
}
