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

        foreach (var o in plan.Objects)
        {
            var node = Instance(files, builder, team, o.Model, o.Slot);
            if (node == null) continue;
            node.Transform = new Transform3D(new Basis(ToGodot(o.AxisX), ToGodot(o.AxisY), ToGodot(o.AxisZ)), ToGodot(o.Position));
            AddChild(node);
        }

        var bounds = plan.Bounds;
        // stand-in: the sandbox's reach, no camera farther from an object than twice the map's diagonal
        // (the install defines no camera limit).
        var reach = 2 * MathF.Sqrt(bounds.Width * bounds.Width + bounds.Depth * bounds.Depth);
        var center = new Vector3((bounds.MinX + bounds.MaxX) / 2, (bounds.MinY + bounds.MaxY) / 2, -(bounds.MinZ + bounds.MaxZ) / 2);
        // stand-in: the fixed camera (slice of the RTS camera replaces it), its field of view and clip planes.
        var size = MathF.Max(bounds.Width, bounds.Depth);
        var camera = new Camera3D { Fov = 60, Near = 1, Far = reach * 4, Current = true };
        AddChild(camera);
        camera.Position = center + new Vector3(0, size * 0.8f, size * 0.6f);
        camera.LookAt(center);
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

    public override void _Process(double delta)
    {
        // The background keeps its centering offset from the camera and never turns with it.
        if (_background != null && _camera != null) _background.GlobalPosition = _camera.GlobalPosition + _backgroundOffset;
        // Wait a few frames so textures are uploaded before capturing.
        if (_screenshot == null || ++_frames < 5) return;
        GetViewport().GetTexture().GetImage().SavePng(_screenshot);
        GetTree().Quit();
    }
}
