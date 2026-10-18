using System;
using Godot;
using OpenArmada.Diagnostics;
using OpenArmada.Sandbox;

namespace OpenArmada.Viewer;

/// <summary>
/// Feeds each frame's time to a <see cref="PlayRun"/> and quits the tree when it ends: exit code 0 for a run that
/// finished its last step, 1 after logging each problem otherwise. Add it after the node that steps the combat
/// world, so a frame's step has run before the script's steps of that frame. Under Movie Maker every frame's delta
/// is the fixed time step, so the run is on simulated time. Nothing throws out of it (INV-0007).
/// </summary>
public partial class PlayDriver(PlayRun run) : Node
{
    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        try
        {
            run.Advance(delta);
        }
        catch (Exception e) // a step that throws is a problem too; the window never stays open (INV-0007)
        {
            Log.Error(Area.Sandbox, $"play run crashed: {e}");
            GetTree().Quit(1);
            SetProcess(false);

            return;
        }

        if (!run.Finished)
            return;
        foreach (var problem in run.Problems)
        {
            Log.Error(Area.Sandbox, problem);
        }

        GetTree().Quit(run.Problems.Count == 0 ? 0 : 1);
        SetProcess(false);
    }
}
