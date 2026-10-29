using System;
using System.Linq;
using Godot;
using OpenArmada.Rendering;

namespace OpenArmada.Viewer;

/// <summary>A model in its own 3D viewport. Drag or two-finger swipe to orbit, wheel or pinch to zoom.</summary>
public partial class ModelPreviewPane : SubViewportContainer
{
    readonly Node3D _model;
    readonly Node3D? _world;
    readonly Camera3D _camera = new() { Far = 10000 };
    float _yaw = 0.6f,
        _pitch = -0.35f,
        _distance,
        _framed;

    /// <summary>
    /// Keeps <c>model</c> for <see cref="_Ready"/>, which adds it to the pane's own SubViewport with a camera, a light,
    /// and an environment, and moves it so the box of its visible meshes is centered on the origin. <c>world</c>, when
    /// given, joins the viewport at its origin, for what draws in world space such as damage sprays.
    /// </summary>
    public ModelPreviewPane(Node3D model, Node3D? world = null)
    {
        _model = model;
        _world = world;
        Stretch = true;
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        SizeFlagsVertical = SizeFlags.ExpandFill;
    }

    /// <inheritdoc/>
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
        if (_world != null)
        {
            view.AddChild(_world);
        }
        var bounds = Bounds(_model);
        _model.Position = -bounds.GetCenter();
        _distance = _framed = Math.Max(bounds.Size.Length() * 0.6f, 1f);
        UpdateCamera();
    }

    // The box around the visible meshes; a model made only of sprite nodes is framed by its sprites.
    static Aabb Bounds(Node3D root)
    {
        var meshes = root.FindChildren("*", nameof(MeshInstance3D), true, false)
            .OfType<MeshInstance3D>()
            .Where(m => m.IsVisibleInTree())
            .ToList();
        var geometry = meshes.Where(m => m is not SpriteNodeQuad).ToList();
        Aabb? box = null;
        foreach (var m in geometry.Count > 0 ? geometry : meshes)
        {
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
        // Zoom stays between a tenth and ten times the distance that frames the model.
        _distance = Math.Clamp(_distance, _framed * 0.1f, _framed * 10f);
        _camera.Position = dir * _distance;
        _camera.LookAt(Vector3.Zero);
    }

    /// <inheritdoc/>
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
            case InputEventPanGesture p:
                _yaw -= p.Delta.X * 0.05f;
                _pitch = Math.Clamp(_pitch - p.Delta.Y * 0.05f, -1.5f, 1.5f);
                UpdateCamera();

                break;
            case InputEventMagnifyGesture g when g.Factor > 0:
                _distance /= g.Factor;
                UpdateCamera();

                break;
        }
    }
}
