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
/// The map sandbox: one map of the install as a live 3D scene, built from the scene plan.
///   godot-mono --path . -- --game &lt;Armada dir&gt; --map &lt;name&gt; [--screenshot out.png]
/// With a play script (<c>--play</c>, under Movie Maker) the sandbox adds a PlayDriver that runs the script's steps
/// and quits, and seeds the combat random source with PlayScript.Seed.
/// Each later feature of the sandbox is one more node added in _Ready (the camera, the asteroid fields, the
/// selection, the orders). A gap of the map or of the sandbox is printed and counted on screen; the map
/// still opens (INV-0002).
/// </summary>
public partial class MapSandboxView(MapSandbox sandbox, string? screenshot, PlayScript? play = null) : Node3D
{
    readonly Dictionary<string, Sod> _sods = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<(string Model, int Slot), Node3D> _templates = new();
    readonly List<string> _gaps = new();
    readonly HashSet<string> _unreadable = new(StringComparer.OrdinalIgnoreCase);
    Node3D? _background;
    Vector3 _backgroundOffset;
    Camera3D? _camera;

    // Null while the map shows a problem instead of the scene (_Ready returns before the rig is built).
    CameraRig? _rig;
    bool _mouseInWindow;
    int _frames;

    /// <inheritdoc/>
    public override void _Ready()
    {
        if (sandbox.Problem != null)
        {
            Show(sandbox.Problem);

            return;
        }

        var window = GetWindow();
        // The window reports only a later enter or exit, so the start state comes from the cursor's place on screen.
        // A play run never has the cursor inside: no edge scroll.
        if (play == null)
        {
            _mouseInWindow = new Rect2I(DisplayServer.WindowGetPosition(), DisplayServer.WindowGetSize()).HasPoint(
                DisplayServer.MouseGetPosition()
            );
            window.MouseEntered += () => _mouseInWindow = true;
            window.MouseExited += () => _mouseInWindow = false;
        }

        // stand-in: the game ran full screen, where the screen edge holds the cursor in the scroll border; the
        // sandbox confines the cursor to its window while the window has focus. Losing focus releases it; Esc
        // releases it until the window gains focus again. A headless or screenshot run never confines.
        if (HasMouse)
        {
            window.FocusEntered += () => Input.MouseMode = Input.MouseModeEnum.Confined;
            window.FocusExited += () => Input.MouseMode = Input.MouseModeEnum.Visible;
            if (window.HasFocus())
            {
                Input.MouseMode = Input.MouseModeEnum.Confined;
            }
        }

        var files = sandbox.Files!;
        var plan = sandbox.Plan!;
        var team = TeamColors.Load(files);
        var builder = new SodBuilder(files);
        foreach (var note in team.Notes)
        {
            Note(note);
        }

        // The plans log their own reports when complete; the label only shows them.
        _gaps.AddRange(plan.Reports.Select(r => r.Text));

        // The scene's ambient light is the map's. stand-in: the directional light (its direction and
        // energy), the ambient energy of 1 and the black clear color; the install defines none.
        AddChild(
            new WorldEnvironment
            {
                Environment = new Godot.Environment
                {
                    BackgroundMode = Godot.Environment.BGMode.Color,
                    BackgroundColor = Colors.Black,
                    AmbientLightSource = Godot.Environment.AmbientSource.Color,
                    AmbientLightColor = new Color(plan.Ambient.X, plan.Ambient.Y, plan.Ambient.Z),
                    AmbientLightEnergy = 1f,
                },
            }
        );
        AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-50, -30, 0) });

        var nodes = new Dictionary<PlacedObject, Node3D>();
        foreach (var o in plan.Objects)
        {
            var node = Instance(files, builder, team, o.Model, o.Slot);
            if (node == null)
                continue;
            node.Transform = new Transform3D(
                new Basis(ToGodot(o.AxisX), ToGodot(o.AxisY), ToGodot(o.AxisZ)),
                ToGodot(o.Position)
            );
            AddChild(node);
            nodes[o] = node;
        }

        var selection = new UnitSelection(files, plan, nodes);
        AddChild(selection);
        _gaps.AddRange(selection.Reports.Select(r => r.Text));
        // Every unit's combat inputs are read once, here; the orders node fights them.
        var combat = CombatPlan.Build(files, plan, selection.Units);
        _gaps.AddRange(combat.Reports.Select(r => r.Text));

        // The rig reads the game constants once per start; move orders aim at its combat plane.
        var bounds = plan.Bounds;
        try
        {
            _rig = new CameraRig(GameConstantsKind.Of(files), bounds);
        }
        catch (Exception e) // no readable RTS_CFG.h: the camera uses its stand-ins (INV-0002)
        {
            // CombatPlan.Build read the same file above and logged its cause (WARN missing, ERROR unreadable);
            // this line is only shown.
            var what = Log.IsAbsent(e) ? "missing" : "cannot be read";
            AddGap($"{GameConstantsKind.FileName} {what}, the camera uses its stand-ins: {e.Message}");
            _rig = new CameraRig(null, bounds);
        }

        foreach (var problem in _rig.Problems)
        {
            Note(problem);
        }

        // The art is built from the world's units, after the world; the world reads hardpoints through it from its
        // first step on.
        CombatArt? art = null;
        var orders = new UnitOrders(
            files,
            plan,
            selection,
            nodes,
            (float)_rig.CombatPlaneHeight,
            bounds,
            combat,
            gap => AddGap(gap),
            (unit, name, pose) => art!.HardpointPosition(unit, name, pose),
            o => _sods.GetValueOrDefault(o.Model),
            play == null ? null : PlayScript.Seed
        );
        AddChild(orders);
        AddChild(new UnitReadout(selection, orders.Combat));
        AddChild(new SelectionDisplayView(selection, orders.Combat, HoverPoint));
        AddChild(new AlertKeys(AlertBindings.Read(files, gap => AddGap(gap)), selection, orders.Combat));

        // The fight's sprites, sounds, and explosions come from the install, read once here; what it lacks joins
        // the gaps.
        art = CombatArt.Build(files, orders.Combat.Units.Select(u => (u.Unit, u.Profile)));
        art.AddSmallExplosions();
        var (eye, target) = _rig.Pose; // the camera's opening pose: no input has moved it yet
        var falloff = SoundFalloff.For(System.Numerics.Vector3.Distance(eye, target));
        AddChild(new CombatView(files, art, orders.Combat, nodes, builder, orders, falloff, gap => AddGap(gap)));
        _gaps.AddRange(art.Reports.Select(r => r.Text));
        AddChild(
            new DamageSprayView(
                files,
                orders.Combat,
                nodes,
                o => _sods.GetValueOrDefault(o.Model),
                gap => AddGap(gap),
                play == null ? null : PlayScript.Seed
            )
        );

        AddChild(new AsteroidFieldsView(files, plan, Note, gap => AddGap(gap), Unreadable));

        // The sandbox's reach: no camera is farther from an object than the rig's greatest eye distance from its
        // look-at point (which stays within the map) plus the map's diagonal.
        var reach = (float)_rig.MaxDistance + MathF.Sqrt(bounds.Width * bounds.Width + bounds.Depth * bounds.Depth);
        // stand-in: the camera's field of view and near plane; the far plane covers the map and the background.
        var camera = new Camera3D
        {
            Fov = 60,
            Near = 1,
            Far = reach * 4,
            Current = true,
        };
        AddChild(camera);
        _camera = camera;
        PlaceCamera();
        if (plan.Background != null)
        {
            AddBackground(files, builder, team, plan.Background, camera, reach);
        }

        if (play != null)
        {
            // The units a script can address are those at the start; a destroyed unit leaves the selection. The
            // driver comes after the orders node, so each frame's combat step runs before the script's steps.
            var names = new UnitNames(selection.Units.Select(u => u.Object));
            var state = new PlayState(
                names,
                orders.Pose,
                orders.Combat.Units.ToList(),
                o => _sods.GetValueOrDefault(o.Model)
            );
            var folder = ProjectSettings.GlobalizePath($"res://play-out/{play.Stem}");
            var input = new PlayInput(this, names, selection, nodes, (float)_rig.CombatPlaneHeight, folder);
            AddChild(new PlayKeys(input));
            AddChild(new PlayDriver(new PlayRun(play, state, folder, input.Run)));
        }

        sandbox.LogOpened(selection.Units.Count);
        Show(null);
    }

    // The model of the name with its team materials in the slot's color: the first instance is built, later
    // ones copy it (they share its meshes and materials, so animated textures run once).
    Node3D? Instance(GameFileSystem files, SodBuilder builder, TeamColors team, string model, int slot)
    {
        if (_templates.TryGetValue((model, slot), out var template))
            return (Node3D)template.Duplicate();
        if (_unreadable.Contains(model))
            return null;
        try
        {
            if (!_sods.TryGetValue(model, out var sod))
            {
                _sods[model] = sod = Sod.Read(files.ReadAllBytes(model + ".sod"));
            }

            var node = builder.Build(sod, team.Color(TeamColors.SlotNames[slot]));
            foreach (var problem in builder.Problems)
            {
                Note($"{model}: {problem}");
            }

            // The builder logged each texture that failed (ViewerLog.TextureFailed); these lines are only shown.
            foreach (var failure in builder.TextureFailures)
            {
                AddGap($"{model}: {failure}");
            }

            _templates[(model, slot)] = node;

            return node;
        }
        catch (Exception e) // a model that cannot be read is a gap, whatever the reason, and is read once (INV-0002)
        {
            Unreadable(model, e.Message);

            return null;
        }
    }

    // Shows a gap on the label once. The producers that pass this (the orders, the combat view, the model
    // builder's texture failures, the camera's RTS_CFG.h, which CombatPlan.Build logs) log their own findings,
    // and so does AlertBindings.Read for the alert keys.
    // Returns whether the gap was new.
    bool AddGap(string gap)
    {
        if (_gaps.Contains(gap))
        {
            return false;
        }

        _gaps.Add(gap);
        return true;
    }

    // A headless, screenshot, or play run has no mouse: the real mouse neither confines nor scrolls nor hovers.
    bool HasMouse => DisplayServer.GetName() != "headless" && screenshot == null && play == null;

    // The point the cursor hovers for the selection display; null outside the window or without a mouse.
    Vector2? HoverPoint()
    {
        var mouse = GetViewport().GetMousePosition();

        return
            SelectionDisplay.HoverPoint(
                new System.Numerics.Vector2(mouse.X, mouse.Y),
                _mouseInWindow,
                Input.MouseMode == Input.MouseModeEnum.Confined,
                HasMouse
            )
                is { } p
            ? new Vector2(p.X, p.Y)
            : null;
    }

    // A finding only the view gathers: shown once and logged once, as WARN.
    void Note(string gap)
    {
        if (AddGap(gap))
        {
            Log.Warn(Area.Sandbox, gap);
        }
    }

    // A model that cannot be read: an asset failed, so one ERROR per model however many producers report it;
    // shown on the label like any gap.
    void Unreadable(string model, string reason)
    {
        if (_unreadable.Add(model))
        {
            AddGap(MapSandbox.LogUnreadableModel(model, reason));
        }
    }

    // The background model sits behind every other object: it is scaled until its origin's nearest distance to
    // a face of its box passes the sandbox's reach, kept with its box center (its origin when inside) on the camera, and the far plane
    // passes its farthest corner, so no camera position or rotation clips it or puts an object behind it.
    // A model whose origin lies outside its box is centered on the box first, so the camera is inside it.
    void AddBackground(
        GameFileSystem files,
        SodBuilder builder,
        TeamColors team,
        string model,
        Camera3D camera,
        float reach
    )
    {
        var node = Instance(files, builder, team, model, 0);
        if (node == null)
            return;
        // The box in the model's own frame; a model without visible meshes gets a unit box.
        var box =
            VisibleMeshes.Extent(VisibleMeshes.Of(node), Transform3D.Identity)
            ?? new Aabb(-Vector3.One, Vector3.One * 2);
        var inside =
            box.Position.X < 0
            && box.Position.Y < 0
            && box.Position.Z < 0
            && box.End.X > 0
            && box.End.Y > 0
            && box.End.Z > 0;
        var shift = inside ? Vector3.Zero : -box.GetCenter();
        box.Position += shift;
        var low = -box.Position;
        var high = box.End;
        var nearest = MathF.Min(
            MathF.Min(MathF.Min(low.X, high.X), MathF.Min(low.Y, high.Y)),
            MathF.Min(low.Z, high.Z)
        );
        var farthest = new[] { low.X, high.X }
            .SelectMany(x =>
                new[] { low.Y, high.Y }.SelectMany(y =>
                    new[] { low.Z, high.Z }.Select(z => new Vector3(x, y, z).Length())
                )
            )
            .Max();
        // stand-in: the factor 1.5 of margin past the reach and past the farthest corner.
        var scale = reach * 1.5f / MathF.Max(nearest, 0.001f);
        node.Transform = new Transform3D(Basis.FromScale(Vector3.One * scale), shift * scale);
        AddChild(node);
        camera.Far = MathF.Max(camera.Far, farthest * scale * 1.5f);
        _background = node;
        _backgroundOffset = shift * scale;
        _camera = camera;
    }

    // Shows the gaps on a label with their count, or a problem in place of the scene. The log has each of them
    // already (MapSandbox.Open logs the problem).
    void Show(string? problem)
    {
        var text = problem ?? (_gaps.Count == 0 ? "" : $"{_gaps.Count} gaps:\n{string.Join("\n", _gaps)}");
        var layer = new CanvasLayer();
        AddChild(layer);
        layer.AddChild(
            new Label
            {
                Text = text,
                Position = new Vector2(8, 8),
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                CustomMinimumSize = new Vector2(600, 0),
            }
        );
    }

    static Vector3 ToGodot(System.Numerics.Vector3 v) => new(v.X, v.Y, v.Z);

    // The rig's pose in Godot's frame (Z mirrored).
    void PlaceCamera()
    {
        var (eye, target) = _rig!.Pose;
        _camera!.Position = new Vector3(eye.X, eye.Y, -eye.Z);
        _camera.LookAt(new Vector3(target.X, target.Y, -target.Z));
    }

    /// <summary>
    /// The wheel changes the height (stand-in: the game uses a vertical drag), a middle-button drag rotates. On a
    /// trackpad (stand-in: the game had none) a two-finger pan does both and a pinch changes the height (CameraRig
    /// names the scales). Esc releases the confined cursor.
    /// </summary>
    public override void _Input(InputEvent e)
    {
        if (_rig == null)
            return;
        if (e is InputEventKey { Pressed: true, Keycode: Key.Escape })
        {
            Input.MouseMode = Input.MouseModeEnum.Visible;
        }
        else if (
            e is InputEventMouseButton { Pressed: true } b
            && b.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown
        )
        {
            _rig.ChangeHeight(b.ButtonIndex == MouseButton.WheelUp ? -_rig.ZoomStep : _rig.ZoomStep);
        }
        else if (e is InputEventMouseMotion m && m.ButtonMask.HasFlag(MouseButtonMask.Middle))
        {
            _rig.Rotate(m.Relative.X * _rig.RotationPerPixel);
        }
        else if (e is InputEventPanGesture p)
        {
            _rig.TrackpadPan(new System.Numerics.Vector2(p.Delta.X, p.Delta.Y));
        }
        else if (e is InputEventMagnifyGesture g)
        {
            _rig.Pinch(g.Factor);
        }
    }

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        if (_rig != null)
        {
            var keys = new Vector2(Input.GetAxis("ui_left", "ui_right"), Input.GetAxis("ui_down", "ui_up"));
            var viewport = GetViewport();
            var mouse = viewport.GetMousePosition();
            var size = viewport.GetVisibleRect().Size;
            // The view tracks the window's mouse enter and exit; CameraRig.EdgeCursor decides what the rig sees.
            var cursor = CameraRig.EdgeCursor(
                new System.Numerics.Vector2(mouse.X, mouse.Y),
                _mouseInWindow,
                Input.MouseMode == Input.MouseModeEnum.Confined
            );
            _rig.Scroll(
                cursor,
                new System.Numerics.Vector2(size.X, size.Y),
                play != null || GetWindow().HasFocus(), // a play run's keys count whichever window has focus
                new System.Numerics.Vector2(keys.X, keys.Y),
                delta
            );
            PlaceCamera();
        }

        // The background keeps its centering offset from the camera and never turns with it.
        if (_background != null && _camera != null)
        {
            _background.GlobalPosition = _camera.GlobalPosition + _backgroundOffset;
        }

        // Wait a few frames so textures are uploaded before capturing.
        if (screenshot == null || ++_frames < 5)
            return;
        // A viewport can have no image (a headless run has no renderer); that and a failed save are one ERROR.
        string? saveError = null;
        var captured = false;
        try
        {
            var image = GetViewport().GetTexture().GetImage();
            if (image != null)
            {
                captured = true;
                var saved = image.SavePng(screenshot);
                saveError = saved == Error.Ok ? null : saved.ToString();
            }
        }
        catch (Exception e) // any other capture failure is a failed save too (INV-0002)
        {
            captured = true;
            saveError = e.Message;
        }

        // user:// and res:// paths are Godot's; the log names the file on disk.
        ViewerLog.Screenshot(
            Area.Sandbox,
            ProjectSettings.GlobalizePath(screenshot),
            ViewerLog.CaptureFailure(captured, saveError)
        );
        GetTree().Quit();
    }
}
