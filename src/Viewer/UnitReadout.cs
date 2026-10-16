using System.Linq;
using Godot;
using OpenArmada.Sandbox;

namespace OpenArmada.Viewer;

// The readout of the selected units: for each its name, shields (current and maximum), and crew, refreshed every
// frame. stand-in: a plain label at the bottom left of the window; the game's own interface comes later.
public partial class UnitReadout : CanvasLayer
{
    readonly UnitSelection _selection;
    readonly CombatWorld _world;
    readonly Label _label = new() { Position = new Vector2(8, 0) };

    public UnitReadout(UnitSelection selection, CombatWorld world)
    {
        _selection = selection;
        _world = world;
    }

    public override void _Ready()
    {
        _label.SetAnchorsPreset(Control.LayoutPreset.BottomLeft);
        _label.GrowVertical = Control.GrowDirection.Begin;
        AddChild(_label);
    }

    public override void _Process(double delta) =>
        _label.Text = string.Join(
            "\n",
            _selection
                .Selected.Select(u => (u.Unit.Name, State: _world.State(u.Object)))
                .Where(x => x.State != null)
                .Select(x => CombatReadout.Line(x.Name, x.State!))
        );
}
