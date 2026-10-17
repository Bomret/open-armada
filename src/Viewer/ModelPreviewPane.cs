using System;
using System.Linq;
using Godot;

namespace OpenArmada.Viewer;

// A model in its own 3D viewport. Drag to orbit, wheel to zoom.
public partial class ModelPreviewPane : SubViewportContainer
{
    readonly Node3D _model;
    readonly Camera3D _camera = new() { Far = 10000 };
    float _yaw = 0.6f,
        _pitch = -0.35f,
        _distance;

    public ModelPreviewPane(Node3D model)
    {
        _model = model;
        Stretch = true;
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        SizeFlagsVertical = SizeFlags.ExpandFill;
    }

    public override void _Ready()
    {
        var view = new SubViewport { OwnWorld3D = true };
        AddChild(view);
        view.AddChild(
            new WorldEnvironment
            {
                Environment = new Godot.Environment
                {
                    BackgroundMode = Godot.Environment.BGMode.Color,
                    BackgroundColor = Colors.Black,
                    AmbientLightSource = Godot.Environment.AmbientSource.Color,
                    AmbientLightColor = new Color(0.35f, 0.35f, 0.4f),
                },
            }
        );
        view.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-40, -30, 0) });
        view.AddChild(_camera);
        view.AddChild(_model);
        var bounds = Bounds(_model);
        _model.Position = -bounds.GetCenter();
        _distance = Math.Max(bounds.Size.Length() * 0.6f, 1f);
        UpdateCamera();
    }

    static Aabb Bounds(Node3D root)
    {
        Aabb? box = null;
        foreach (var m in root.FindChildren("*", nameof(MeshInstance3D), true, false).OfType<MeshInstance3D>())
        {
            if (!m.IsVisibleInTree())
                continue;
            var b = m.GlobalTransform * m.GetAabb();
            box = box?.Merge(b) ?? b;
        }

        return box ?? new Aabb(Vector3.Zero, Vector3.One);
    }

    void UpdateCamera()
    {
        var dir = new Vector3(
            Mathf.Sin(_yaw) * Mathf.Cos(_pitch),
            -Mathf.Sin(_pitch),
            Mathf.Cos(_yaw) * Mathf.Cos(_pitch)
        );
        _camera.Position = dir * _distance;
        _camera.LookAt(Vector3.Zero);
    }

    public override void _GuiInput(InputEvent e)
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
        }
    }
}
