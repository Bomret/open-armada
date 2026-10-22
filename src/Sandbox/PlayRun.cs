using System;
using System.Collections.Generic;

namespace OpenArmada.Sandbox;

/// <summary>
/// The run of a play script over a <see cref="PlayState"/>: the steps in file order on simulated time, which the
/// caller feeds in through <see cref="Advance"/>, after the combat world has taken the same time. A step that
/// fails or throws ends the run with a problem that names the script and the line. The run never logs and never
/// throws for a step (INV-0007); the caller logs <see cref="Problems"/> and quits with the exit code.
/// </summary>
public sealed class PlayRun(
    PlayScript script,
    PlayState state,
    string outputFolder,
    Func<PlayStep, string?>? input = null
)
{
    readonly List<string> _problems = [];
    HoldStep? _held;
    double _clock;
    double _due;
    int _next;

    /// <summary>
    /// The problem with a <c>--play</c> launch, or null when it can start: it needs Movie Maker (which forces a
    /// fixed time step), a <c>--map</c> or a <c>--layout</c>, and no <c>--screenshot</c>. Both a map and a layout
    /// are the launch arguments' own <c>--layout</c>/<c>--map</c> problem, so this rule adds none for them.
    /// </summary>
    public static string? LaunchProblem(bool movieMaker, bool hasMap, bool hasLayout, bool hasScreenshot) =>
        !movieMaker ? "--play needs Movie Maker: start with --write-movie and --fixed-fps"
        : !hasMap && !hasLayout ? "--play needs --map or --layout"
        : hasScreenshot ? "--play cannot be combined with --screenshot"
        : null;

    /// <summary>
    /// The problem with the Movie Maker output, or null: Movie Maker opens its PNG and WAV files at engine start
    /// and writes nothing, yet the run goes on, when the folder of <c>moviePath</c> (the <c>--write-movie</c> value)
    /// does not exist. A relative path is checked against the working directory, which is where Godot resolves it:
    /// <c>--path</c> makes the project folder the working directory before any script runs.
    /// </summary>
    public static string? MovieProblem(string moviePath)
    {
        var folder = System.IO.Path.GetDirectoryName(moviePath);

        return string.IsNullOrEmpty(folder) || System.IO.Directory.Exists(folder)
            ? null
            : $"movie output folder '{folder}' does not exist: create it before the run";
    }

    /// <summary>Whether the last step has run and its wait has passed, or a step failed.</summary>
    public bool Finished { get; private set; }

    /// <summary>The problems that ended the run; none for a run that finished its last step.</summary>
    public IReadOnlyList<string> Problems => _problems;

    /// <summary>
    /// Lets <c>seconds</c> of simulated time pass and runs every step that is due, in file order. A wait counts
    /// from the time its step was due, not from the frame that reached it, so frame rate causes no drift.
    /// </summary>
    public void Advance(double seconds)
    {
        if (Finished)
            return;
        _clock += seconds;
        // stand-in: a wait of whole frames can come out a rounding error short; this tolerance counts it as passed.
        const double Tolerance = 1e-9;
        while (_due <= _clock + Tolerance)
        {
            // A hold ends when its time has passed, before the step after it runs.
            PlayStep step;
            if (_held is { } held)
            {
                step = new ReleaseStep { Line = held.Line, Key = held.Key };
                _held = null;
            }
            else if (_next == script.Steps.Count)
            {
                Finished = true;

                return;
            }
            else
            {
                step = script.Steps[_next++];
            }

            string? problem;
            try
            {
                problem = Run(step);
            }
            catch (Exception e) // a step that throws is a problem naming its line, never a crash (INV-0007)
            {
                problem = $"step threw {e.GetType().Name}: {e.Message}";
            }

            if (problem != null)
            {
                _problems.Add($"{script.Path} line {step.Line}: {problem}");
                Finished = true;

                return;
            }
        }
    }

    // Runs one step; returns its problem, or null.
    string? Run(PlayStep step)
    {
        switch (step)
        {
            case WaitStep wait:
                _due += wait.Seconds;

                return null;
            case DumpStep dump:
                return PlayState.Write(outputFolder, dump.Name, state.Dump());
            case ExpectStep expect:
                return state.Expect(expect);
            case HoldStep hold:
                _held = hold;
                _due += hold.Seconds;

                return Input(step);
            default:
                return Input(step);
        }
    }

    // An input step goes to the executor, which sends it to the sandbox as input events.
    string? Input(PlayStep step) => input == null ? $"step '{step.GetType().Name}' cannot run here" : input(step);
}
