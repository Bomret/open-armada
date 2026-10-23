using System;
using System.Linq;
using Godot;
using OpenArmada.Sandbox;

namespace OpenArmada.Viewer;

/// <summary>
/// The game's in-world selection display over the map sandbox (docs/rules/selection-display.md), through the unit under
/// <c>hoverPoint</c> (null: none): the shield bars of the hovered unit and its team, and the crew dots of the selected
/// among them, redrawn every frame from the units' current combat state.
/// </summary>
public partial class SelectionDisplayView(UnitSelection selection, CombatWorld world, Func<Vector2?> hoverPoint)
    : CanvasLayer
{
    static readonly Color Back = new(0.2f, 0.2f, 0.2f);

    readonly Control _canvas = new();
    HoverState _hover = new();

    /// <inheritdoc/>
    public override void _Ready()
    {
        _canvas.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _canvas.MouseFilter = Control.MouseFilterEnum.Ignore;
        _canvas.Draw += Paint;
        AddChild(_canvas);
    }

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        // The hover steps here, once per frame: the draw callback can run zero or several times a frame.
        // ponytail: the hover pick walks every placed object's meshes each frame (O(objects × meshes)); skip
        // it while neither the mouse nor the camera moved, or cache the pick, when maps grow large.
        var pick = hoverPoint() is { } point ? selection.UnitAt(point)?.Object : null;
        _hover = _hover.Step(pick, selection.Units.Select(u => u.Object), delta);
        _canvas.QueueRedraw();
    }

    // Units are looked up afresh each frame: a destroyed unit is simply no longer listed.
    void Paint()
    {
        // stand-in: the display scale of the screen the window is on stands for the game's pixel size
        var scale = DisplayServer.ScreenGetScale(DisplayServer.WindowGetCurrentScreen());
        var units = selection.Units.ToDictionary(u => u.Object);
        var selected = selection.Selected.Select(u => u.Object);
        foreach (var shown in SelectionDisplay.Shown(_hover, units.Keys, selected))
        {
            if (world.State(shown.Unit) is not { } state || selection.ScreenBoxOf(units[shown.Unit]) is not { } box)
                continue;
            // stand-in: each part draws at alpha (opacity − 0.5) × 2 kept within 0 and 1 (SelectionDisplay.AlphaOf)
            var alpha = SelectionDisplay.AlphaOf(shown.Opacity);
            if (alpha > 0 && SelectionDisplay.ShieldBarOf(box, state, scale) is { } bar)
            {
                _canvas.DrawRect(ToRect(bar.Back), Back with { A = alpha });
                _canvas.DrawRect(ToRect(bar.Fill), new Color(bar.Color.X, bar.Color.Y, bar.Color.Z, alpha));
            }

            var dotAlpha = SelectionDisplay.AlphaOf(_hover.Fade);
            if (shown.Dot && dotAlpha > 0 && SelectionDisplay.CrewDotOf(box, state, scale) is { } dot)
            {
                _canvas.DrawRect(ToRect(dot.Rect), ColorOf(dot.Status) with { A = dotAlpha });
            }
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
