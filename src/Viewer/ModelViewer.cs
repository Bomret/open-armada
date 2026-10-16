using System;
using System.IO;
using System.Linq;
using Godot;
using OpenArmada.Assets;
using OpenArmada.Formats;
using OpenArmada.Rendering;

namespace OpenArmada.Viewer;

// Shows one SOD model from the user's Armada install.
//   godot-mono --path . -- --game <Armada dir> [--model Fente] [--screenshot out.png]
// The game dir can also come from the ARMADA_DIR environment variable.
// Drag to orbit, wheel to zoom, Left/Right to step through models.
public partial class ModelViewer : Node3D
{
    GameFileSystem _files = null!;
    SodBuilder _builder = null!;
    string[] _models = Array.Empty<string>();
    int _index;
    Node3D? _model;
    Label _label = null!;
    Camera3D _camera = null!;
    float _yaw = 0.6f, _pitch = -0.35f, _distance = 10f;
    string? _screenshot;
    int _frames;

    public override void _Ready()
    {
        var args = OS.GetCmdlineUserArgs();
        string? Arg(string name)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        var game = Arg("--game") ?? System.Environment.GetEnvironmentVariable("ARMADA_DIR");
        if (Array.IndexOf(args, "--check") >= 0)
        {
            // A Godot callback exception does not set the exit code, so the check owns it.
            int code;
            try { code = AssetCheck.Run(game, AssetKind.Discover(), Console.Out); }
            catch (Exception e)
            {
                Console.Out.WriteLine($"asset check crashed: {e}");
                code = 1;
            }
            GetTree().Quit(code);
            return;
        }
        if (game == null)
        {
            GD.PrintErr("pass --game <Armada install dir> or set ARMADA_DIR");
            GetTree().Quit(1);
            return;
        }
        try { _files = new GameFileSystem(game); }
        catch (Exception e) // no install, or the root cannot be listed (permissions, I/O)
        {
            GD.PrintErr(e.Message);
            GetTree().Quit(1);
            return;
        }
        _builder = new SodBuilder(_files);
        _screenshot = Arg("--screenshot");
        _models = new ModelKind().Enumerate(_files)
            .Select(Path.GetFileNameWithoutExtension).OfType<string>()
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();
        var model = Arg("--model") ?? "Fente";
        _index = Math.Max(0, Array.FindIndex(_models, n => n.Equals(model, StringComparison.OrdinalIgnoreCase)));

        AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = Colors.Black,
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color(0.35f, 0.35f, 0.4f),
            },
        });
        AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-40, -30, 0) });
        _camera = new Camera3D { Far = 10000 };
        AddChild(_camera);
        var ui = new CanvasLayer();
        _label = new Label { Position = new Vector2(12, 8) };
        ui.AddChild(_label);
        AddChild(ui);

        ShowModel(_index);
    }

    void ShowModel(int index)
    {
        _index = (index + _models.Length) % _models.Length;
        _model?.QueueFree();
        _model = null;
        var name = _models[_index];
        try
        {
            _model = _builder.Build(Sod.Read(_files.ReadAllBytes(name + ".sod")));
            AddChild(_model);
            var bounds = Bounds(_model);
            _model.Position = -bounds.GetCenter();
            _distance = Math.Max(bounds.Size.Length() * 0.6f, 1f);
            _label.Text = $"{name}   ({_index + 1}/{_models.Length})   drag: orbit, wheel: zoom, left/right: next model";
        }
        catch (Exception e)
        {
            _label.Text = $"{name}: {e.Message}";
        }
        UpdateCamera();
    }

    static Aabb Bounds(Node3D root)
    {
        Aabb? box = null;
        foreach (var m in root.FindChildren("*", nameof(MeshInstance3D), true, false).OfType<MeshInstance3D>())
        {
            if (!m.IsVisibleInTree()) continue;
            var b = m.GlobalTransform * m.GetAabb();
            box = box?.Merge(b) ?? b;
        }
        return box ?? new Aabb(Vector3.Zero, Vector3.One);
    }

    void UpdateCamera()
    {
        var dir = new Vector3(Mathf.Sin(_yaw) * Mathf.Cos(_pitch), -Mathf.Sin(_pitch), Mathf.Cos(_yaw) * Mathf.Cos(_pitch));
        _camera.Position = dir * _distance;
        _camera.LookAt(Vector3.Zero);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseMotion m when (m.ButtonMask & MouseButtonMask.Left) != 0:
                _yaw -= m.Relative.X * 0.01f;
                _pitch = Math.Clamp(_pitch - m.Relative.Y * 0.01f, -1.5f, 1.5f);
                UpdateCamera();
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp }:
                _distance *= 0.9f;
                UpdateCamera();
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelDown }:
                _distance *= 1.1f;
                UpdateCamera();
                break;
            case InputEventKey { Pressed: true, Keycode: Key.Right }:
                ShowModel(_index + 1);
                break;
            case InputEventKey { Pressed: true, Keycode: Key.Left }:
                ShowModel(_index - 1);
                break;
        }
    }

    public override void _Process(double delta)
    {
        // Wait a few frames so textures are uploaded before capturing.
        if (_screenshot == null || ++_frames < 5) return;
        GetViewport().GetTexture().GetImage().SavePng(_screenshot);
        GetTree().Quit();
    }
}
