using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Formats;
using OpenArmada.Sandbox;
using static OpenArmada.Tests.CombatFixtures;

namespace OpenArmada.Tests;

[TestClass]
public class PlayRunTests
{
    const double Frame = 1.0 / 30;

    // Two hostile ships that fight with rolled damage and rolled hits, so the dumps depend on the random source.
    sealed class Fight
    {
        readonly CombatWorld _world;
        readonly PlayRun _run;
        public readonly PlayState State;

        public Fight(PlayScript script, string folder, bool lethal = false, Func<PlayStep, string?>? input = null)
        {
            var a = Placed("a", 1, 0);
            var b = Placed("b", 2, 10);
            var beam = Weapon(Beam(damage: lethal ? 60 : 6, variance: 5), delay: 0.5f, hitChance: 0.5f);
            var units = new[] { a, b }
                .Select(o => new CombatUnit
                {
                    Unit = o,
                    Profile = Profile(
                        [beam],
                        maxHealth: 30,
                        hull: lethal ? 0 : 100,
                        systems:
                        [
                            Sys(SystemNames.Engines, 5, percent: lethal ? 0 : 10),
                            Sys(SystemNames.Weapons, 5, percent: lethal ? 0 : 10),
                        ]
                    ),
                    DefinitionName = o.Label + ".odf",
                    Radius = 0,
                    Mobile = false,
                })
                .ToList();
            var poses = new UnitPoses([a, b]);
            var random = new Random(PlayScript.Seed);
            _world = new CombatWorld(poses, units, 1, random.NextDouble, (_, _, pose) => pose.Position);
            _world.Attack(a, b);
            _world.Attack(b, a);
            State = new PlayState(new UnitNames([a, b]), poses.Pose, _world.Units);
            _run = new PlayRun(script, State, folder, input);
        }

        public PlayRun Run => _run;

        // Frames until the run finishes, each stepping the world first, as the sandbox does.
        public int Play(int limit = 100000)
        {
            var frames = 0;
            while (!_run.Finished && frames < limit)
            {
                _world.Step(Frame);
                _run.Advance(Frame);
                frames++;
            }

            return frames;
        }
    }

    static string Temp() => Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    [TestMethod]
    public void TwoRunsOfOneScriptWriteByteIdenticalDumps()
    {
        var script = PlayScript.Parse("dump start\nwait 1\ndump mid\nwait 2.5\ndump end\n", "x.play");
        var one = Temp();
        var two = Temp();
        try
        {
            new Fight(script, one).Play();
            new Fight(script, two).Play();

            foreach (var name in new[] { "start", "mid", "end" })
            {
                CollectionAssert.AreEqual(
                    File.ReadAllBytes(Path.Combine(one, name + ".txt")),
                    File.ReadAllBytes(Path.Combine(two, name + ".txt"))
                );
            }

            Assert.AreNotEqual(
                File.ReadAllText(Path.Combine(one, "start.txt")),
                File.ReadAllText(Path.Combine(one, "end.txt"))
            );
        }
        finally
        {
            Directory.Delete(one, true);
            Directory.Delete(two, true);
        }
    }

    [TestMethod]
    public void AUnitThatCombatDestroysIsDumpedWithItsDestroyedFlagSet()
    {
        var folder = Temp();
        try
        {
            var fight = new Fight(PlayScript.Parse("wait 120\ndump end\n", "x.play"), folder, lethal: true);
            fight.Play();

            StringAssert.Contains(File.ReadAllText(Path.Combine(folder, "end.txt")), "destroyed=true");
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [TestMethod]
    public void TheRunFinishesWhenTheLastWaitHasPassedOnSimulatedTime()
    {
        var fight = new Fight(PlayScript.Parse("wait 1\nwait 0.5\n", "x.play"), Temp());

        Assert.AreEqual(45, fight.Play());
        Assert.AreEqual(0, fight.Run.Problems.Count);
    }

    [TestMethod]
    public void AScriptWithoutStepsFinishesAtOnce()
    {
        var fight = new Fight(PlayScript.Parse("", "x.play"), Temp());

        Assert.AreEqual(1, fight.Play());
    }

    [TestMethod]
    public void AFailedExpectationEndsTheRunWithAProblemNamingScriptAndLine()
    {
        var script = PlayScript.Parse("wait 1\n\nexpect a shields > 1000\nwait 5\n", "play/x.play");
        var fight = new Fight(script, Temp());

        var frames = fight.Play();

        Assert.AreEqual(30, frames);
        Assert.AreEqual(1, fight.Run.Problems.Count);
        StringAssert.StartsWith(
            fight.Run.Problems[0],
            "play/x.play line 3: expectation failed: a shields: expected > 1000, actual "
        );
    }

    [TestMethod]
    public void AnUnknownUnitInAStepIsAProblemNamingTheLine()
    {
        var fight = new Fight(PlayScript.Parse("expect ghost shields > 1\n", "x.play"), Temp());

        fight.Play();

        CollectionAssert.AreEqual(new[] { "x.play line 1: unknown unit 'ghost'" }, fight.Run.Problems.ToArray());
    }

    [TestMethod]
    public void ADumpThatCannotBeWrittenEndsTheRunWithAProblemNamingTheLine()
    {
        var blocker = Temp();
        File.WriteAllText(blocker, "not a folder");
        try
        {
            var fight = new Fight(PlayScript.Parse("wait 0\ndump d\n", "x.play"), Path.Combine(blocker, "out"));

            fight.Play();

            Assert.AreEqual(1, fight.Run.Problems.Count);
            StringAssert.StartsWith(fight.Run.Problems[0], "x.play line 2: cannot write '");
        }
        finally
        {
            File.Delete(blocker);
        }
    }

    [TestMethod]
    public void AHeldKeyIsPressedWhenItsStepIsDueAndReleasedWhenItsTimeHasPassed()
    {
        var log = new System.Collections.Generic.List<string>();
        var script = PlayScript.Parse("select a\nhold Left 1\npress Escape\n", "x.play");
        var fight = new Fight(
            script,
            Temp(),
            input: step =>
            {
                log.Add(
                    step switch
                    {
                        SelectStep s => $"select {s.Unit}",
                        HoldStep h => $"down {h.Key}",
                        ReleaseStep r => $"up {r.Key}",
                        PressStep p => $"press {p.Key}",
                        _ => "?",
                    }
                );

                return null;
            }
        );

        var frames = fight.Play();

        CollectionAssert.AreEqual(new[] { "select a", "down Left", "up Left", "press Escape" }, log.ToArray());
        Assert.AreEqual(30, frames);
    }

    [TestMethod]
    public void AnInputStepThatFailsEndsTheRunWithItsProblemNamingTheLine()
    {
        var script = PlayScript.Parse("wait 0\nselect a\nwait 5\n", "x.play");
        var fight = new Fight(script, Temp(), input: _ => "unit 'a' is off screen");

        fight.Play();

        CollectionAssert.AreEqual(new[] { "x.play line 2: unit 'a' is off screen" }, fight.Run.Problems.ToArray());
    }

    [TestMethod]
    public void AnInputStepThatThrowsIsAProblemNamingTheLine()
    {
        var script = PlayScript.Parse("wait 0\nselect a\nwait 5\n", "x.play");
        var fight = new Fight(script, Temp(), input: _ => throw new InvalidOperationException("boom"));

        fight.Play();

        CollectionAssert.AreEqual(
            new[] { "x.play line 2: step threw InvalidOperationException: boom" },
            fight.Run.Problems.ToArray()
        );
        Assert.IsTrue(fight.Run.Finished);
    }

    [TestMethod]
    public void AnInputStepWithoutAnExecutorIsAProblemNamingTheLine()
    {
        var fight = new Fight(PlayScript.Parse("select a\n", "x.play"), Temp());

        fight.Play();

        Assert.AreEqual(1, fight.Run.Problems.Count);
        StringAssert.StartsWith(fight.Run.Problems[0], "x.play line 1: ");
    }

    [TestMethod]
    [DataRow(false, true, false, false, "--play needs Movie Maker")]
    [DataRow(false, false, true, false, "--play needs Movie Maker")]
    [DataRow(true, false, false, false, "--play needs --map or --layout")]
    [DataRow(true, true, false, true, "--play cannot be combined with --screenshot")]
    [DataRow(true, false, true, true, "--play cannot be combined with --screenshot")]
    public void ALaunchThatCannotPlayIsAProblem(bool movie, bool map, bool layout, bool screenshot, string problem) =>
        StringAssert.StartsWith(PlayRun.LaunchProblem(movie, map, layout, screenshot), problem);

    [TestMethod]
    public void AMovieFileInAMissingFolderIsAProblemNamingTheFolder()
    {
        var folder = Path.Combine(Temp(), "duel");

        StringAssert.Contains(PlayRun.MovieProblem(Path.Combine(folder, "frame.png")), $"'{folder}' does not exist");
    }

    [TestMethod]
    public void AMovieFileInAnExistingFolderHasNoProblem()
    {
        var folder = Temp();
        Directory.CreateDirectory(folder);
        try
        {
            Assert.IsNull(PlayRun.MovieProblem(Path.Combine(folder, "frame.png")));
        }
        finally
        {
            Directory.Delete(folder);
        }
    }

    [TestMethod]
    [DataRow(true, false)]
    [DataRow(false, true)]
    public void ALaunchUnderMovieMakerWithAMapOrALayoutAndNoScreenshotHasNoProblem(bool map, bool layout) =>
        Assert.IsNull(PlayRun.LaunchProblem(true, map, layout, false));

    [TestMethod]
    public void ALaunchWithAMapAndALayoutLeavesTheProblemToTheLaunchArguments() =>
        // The --layout/--map exclusion is the run's one entry; the launch rule adds no second one.
        Assert.IsNull(PlayRun.LaunchProblem(true, true, true, false));
}
