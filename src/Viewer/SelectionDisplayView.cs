using System;
using Godot;
using OpenArmada.Sandbox;

namespace OpenArmada.Viewer;

/// <summary>
/// The game's in-world selection display over the map sandbox (docs/selection-display.md): the shield bar and the
/// crew dot of each selected unit, and the shield bar of the unit under <c>hoverPoint</c> (null: none), redrawn
/// every frame from the units' current combat state.
/// </summary>
public partial class SelectionDisplayView(UnitSelection selection, CombatWorld world, Func<Vector2?> hoverPoint)
    : CanvasLayer
{
    static readonly Color Back = new(0.2f, 0.2f, 0.2f);

    readonly Control _canvas = new();

    /// <inheritdoc/>
    public override void _Ready()
    {
        _canvas.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _canvas.MouseFilter = Control.MouseFilterEnum.Ignore;
        _canvas.Draw += Paint;
        AddChild(_canvas);
    }

    /// <inheritdoc/>
    public override void _Process(double delta) => _canvas.QueueRedraw();

    // Units are looked up afresh each frame: a destroyed or deselected unit is simply no longer listed.
    void Paint()
    {
        // stand-in: the display scale of the screen the window is on stands for the game's pixel size
        var scale = DisplayServer.ScreenGetScale(DisplayServer.WindowGetCurrentScreen());
        // ponytail: the hover pick walks every placed object's meshes each frame (O(objects × meshes)); skip
        // it while neither the mouse nor the camera moved, or cache the pick, when maps grow large.
        var hovered = hoverPoint() is { } point ? selection.UnitAt(point) : null;
        // stand-in: every part draws in full-intensity colors. The game scales its colors by the opacity minus 0.5,
        // half intensity at full opacity, and whether its renderer brightens them again is unknown.
        foreach (var (unit, withDot) in SelectionDisplay.Shown(selection.Selected, hovered))
        {
            if (world.State(unit.Object) is not { } state || selection.ScreenBoxOf(unit) is not { } box)
                continue;
            if (SelectionDisplay.ShieldBarOf(box, state, scale) is { } bar)
            {
                _canvas.DrawRect(ToRect(bar.Back), Back);
                _canvas.DrawRect(ToRect(bar.Fill), new Color(bar.Color.X, bar.Color.Y, bar.Color.Z));
            }

            if (!withDot)
                continue;
            var dot = SelectionDisplay.CrewDotOf(box, state, scale);
            _canvas.DrawRect(ToRect(dot.Rect), ColorOf(dot.Status));
        }
    }

    static Color ColorOf(CrewStatus status) =>
        status switch
        {
            CrewStatus.Red => new Color(1, 0, 0),
            CrewStatus.Yellow => new Color(1, 1, 0),
            _ => new Color(0, 1, 0),
        };

    static Rect2 ToRect(ScreenRect r) => new(r.Position.X, r.Position.Y, r.Size.X, r.Size.Y);
}
