using Godot;

namespace OpenArmada.Viewer;

// A read-only, wrapping text view that fills its pane.
public static class TextPane
{
    public static TextEdit Create(string text) => new()
    {
        Text = text,
        Editable = false,
        WrapMode = TextEdit.LineWrappingMode.Boundary,
        SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        SizeFlagsVertical = Control.SizeFlags.ExpandFill,
    };
}
