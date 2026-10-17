using Godot;

namespace OpenArmada.Viewer;

/// <summary>
/// Puts back the keys a play run holds before any other node's frame step reads them. The window releases every
/// pressed key when it loses focus, and the display server decides when that happens, so a held arrow key would
/// stop the camera at a different frame in each run (INV-0006). The lowest process priority puts this node's step
/// ahead of every other node's step, after the frame's window events.
/// </summary>
public partial class PlayKeys : Node
{
    readonly PlayInput _input;

    /// <summary>Creates the node for the executor whose held keys it puts back.</summary>
    public PlayKeys(PlayInput input)
    {
        _input = input;
        ProcessPriority = int.MinValue;
    }

    /// <inheritdoc/>
    public override void _Process(double delta) => _input.KeepHeld();
}
