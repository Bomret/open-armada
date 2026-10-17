using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
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

        public Fight(PlayScript script, string folder)
        {
            var a = Placed("a", 1, 0);
            var b = Placed("b", 2, 10);
            var beam = Weapon(Beam(damage: 6, variance: 5), delay: 0.5f, hitChance: 0.5f);
            var units = new[] { a, b }
                .Select(o => new CombatUnit
                {
                    Unit = o,
                    Profile = Profile(
                        [beam],
                        maxHealth: 30,
                        systems: [Sys(SystemNames.Engines, 5), Sys(SystemNames.Weapons, 5)]
                    ),
                    DefinitionName = o.Label + ".odf",
                    Radius = 0,
                    Mobile = false,
                })
                .ToList();
            var poses = new UnitPoses([a, b]);
            var random = new Random(PlayScript.Seed);
            _world = new CombatWorld(poses, units, 1, random.NextDouble);
            _world.Attack(a, b);
            _world.Attack(b, a);
            State = new PlayState(new UnitNames([a, b]), poses.Pose, _world.Units);
            _run = new PlayRun(script, State, folder);
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
    [DataRow(false, true, false, "--play needs Movie Maker")]
    [DataRow(true, false, false, "--play needs --map")]
    [DataRow(true, true, true, "--play cannot be combined with --screenshot")]
    public void ALaunchThatCannotPlayIsAProblem(bool movie, bool map, bool screenshot, string problem) =>
        StringAssert.StartsWith(PlayRun.LaunchProblem(movie, map, screenshot), problem);

    [TestMethod]
    public void ALaunchUnderMovieMakerWithAMapAndNoScreenshotHasNoProblem() =>
        Assert.IsNull(PlayRun.LaunchProblem(true, true, false));
}
