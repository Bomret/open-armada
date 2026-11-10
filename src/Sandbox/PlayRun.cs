using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenArmada.Sandbox;

/// <summary>
/// The run of a play script over a <see cref="PlayState"/>: the steps in file order on simulated time, which the
/// caller feeds in through <see cref="Advance"/>, after the combat world has taken the same time. A step that
/// fails or throws ends the run with a problem that names the script and the line. The run never logs and never
/// throws for a step (INV-0007); the caller logs <see cref="Problems"/> and quits with the exit code.
/// <c>skip</c> steps the combat world by the given seconds without drawing, for <see cref="FastForwardStep"/>; the
/// skipped time is not on the run's clock, so a <c>wait</c> after it counts drawn time only.
/// </summary>
public sealed class PlayRun(
    PlayScript script,
    PlayState state,
    string outputFolder,
    Func<PlayStep, string?>? input = null,
    Action<double>? skip = null
)
{
    readonly List<string> _problems = [];
    HoldStep? _held;
    double _clock;
    double _due;
    int _next;

    /// <summary>
    /// The exit code of a play run whose window was closed before its last step. It is neither 0 nor 1, the codes
    /// of a run that ended, so the play-run wrapper never stores it as a result.
    /// </summary>
    public const int ClosedExitCode = 4;

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
    /// The problems that stop a <c>--play</c> launch, each one ERROR entry (INV-0005); none means the run starts.
    /// The launch-argument problems (a flag without a value, such as a bare <c>--game</c>, or <c>--map</c> with
    /// <c>--layout</c>) are each returned, and then <c>launchProblem</c> (of <see cref="LaunchProblem"/> or
    /// <see cref="MovieProblem"/>) is left out, so one cause is never two entries. Without them,
    /// <c>launchProblem</c> is the one problem when there is one. Every entry reads
    /// <c>play run cannot start: &lt;problem&gt;</c>: its cause is the play run that failed to open, so it is never
    /// the text of the WARN entry another mode logs for the same launch-argument problem.
    /// </summary>
    public static IReadOnlyList<string> LaunchErrors(string? launchProblem, IReadOnlyList<string> argumentProblems)
    {
        IReadOnlyList<string> problems =
            argumentProblems.Count > 0 ? argumentProblems
            : launchProblem != null ? [launchProblem]
            : [];

        return problems.Select(problem => $"play run cannot start: {problem}").ToArray();
    }

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
            case FastForwardStep fastForward:
                return FastForward(fastForward);
            case HoldStep hold:
                _held = hold;
                _due += hold.Seconds;

                return Input(step);
            default:
                return Input(step);
        }
    }

    // Steps the world one combat tick at a time until the condition holds; none when it holds already. A condition
    // that cannot be asked, or that does not hold after the step's most seconds, is the problem.
    string? FastForward(FastForwardStep step)
    {
        if (skip == null)
            return "step 'fast-forward' cannot run here";
        // A tolerance like a wait's, so a whole number of ticks is not counted one too many by rounding.
        var most = (int)Math.Ceiling(step.MaxSeconds / CombatWorld.Tick - 1e-9);
        for (var ticks = 0; ; ticks++)
        {
            var (asked, problem) = state.Check(step.Until);
            if (!asked)
                return problem;
            if (problem == null)
                return null;
            if (ticks == most)
                return $"fast-forward not reached after {step.MaxSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)} s: {problem}";
            skip(CombatWorld.Tick);
        }
    }

    // An input step goes to the executor, which sends it to the sandbox as input events.
    string? Input(PlayStep step) => input == null ? $"step '{step.GetType().Name}' cannot run here" : input(step);
}
