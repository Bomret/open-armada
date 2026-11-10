using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Formats;
using OpenArmada.Sandbox;
using static OpenArmada.Tests.CombatFixtures;

namespace OpenArmada.Tests;

// A play run's fast-forward step over one unit whose move order the skip callback ends after a set number of ticks.
[TestClass]
public class FastForwardTests
{
    sealed class Scene
    {
        public readonly PlacedObject A = Placed("a", 1, 0);
        public readonly UnitCombatState State;
        public readonly PlayState Play;
        public int Ticks;
        public double Seconds;

        public Scene(int arriveAfter, int failOn = -1)
        {
            State = new UnitCombatState(A, Profile(), "a.odf", 0, true) { PlayerMove = (100, -200) };
            Play = new PlayState(new UnitNames([A]), new UnitPoses([A]).Pose, [State]);
            ArriveAfter = arriveAfter;
            FailOn = failOn;
        }

        int ArriveAfter { get; }

        int FailOn { get; }

        public PlayRun Run(string text, bool canSkip = true) =>
            new(
                PlayScript.Parse(text, "x.play"),
                Play,
                "unused",
                _ => null,
                canSkip
                    ? seconds =>
                    {
                        Ticks++;
                        Seconds += seconds;
                        if (Ticks == ArriveAfter)
                        {
                            State.PlayerMove = null;
                        }

                        return Ticks == FailOn ? "combat step failed: boom" : null;
                    }
                    : null
            );
    }

    [TestMethod]
    public void TheStepRunsCombatTicksUntilTheConditionHoldsAndTheSkippedTimeIsNotOnTheClock()
    {
        var scene = new Scene(arriveAfter: 50);
        var run = scene.Run("fast-forward until a move = none max 10\nwait 1\nexpect a move = none");

        run.Advance(0);

        Assert.AreEqual(50, scene.Ticks);
        Assert.AreEqual(50 * CombatWorld.Tick, scene.Seconds, 1e-9);
        Assert.IsFalse(run.Finished, "the wait after the step counts drawn time from where the step ran");

        run.Advance(1);

        Assert.IsTrue(run.Finished);
        Assert.AreEqual(0, run.Problems.Count);
    }

    [TestMethod]
    public void AConditionThatHoldsAlreadyRunsNoTick()
    {
        var scene = new Scene(arriveAfter: 1);
        var run = scene.Run("fast-forward until a move = open max 10");

        run.Advance(0);

        Assert.AreEqual(0, scene.Ticks);
        Assert.IsTrue(run.Finished);
        Assert.AreEqual(0, run.Problems.Count);
    }

    [TestMethod]
    public void AConditionNotReachedWithinTheMostSecondsIsAProblemWithTheActualValue()
    {
        var scene = new Scene(arriveAfter: 1000);
        var run = scene.Run("fast-forward until a move = none max 1");

        run.Advance(0);

        Assert.AreEqual(50, scene.Ticks);
        CollectionAssert.AreEqual(
            new[]
            {
                "x.play line 1: fast-forward not reached after 1 s: expectation failed: a move: expected = none, actual open",
            },
            run.Problems.ToArray()
        );
    }

    [TestMethod]
    public void AConditionReachedOnTheLastTickCounts()
    {
        var scene = new Scene(arriveAfter: 50);
        var run = scene.Run("fast-forward until a move = none max 1");

        run.Advance(0);

        Assert.AreEqual(0, run.Problems.Count);
    }

    [TestMethod]
    public void AConditionThatCannotBeAskedIsAProblemAndRunsNoTick()
    {
        var scene = new Scene(arriveAfter: 1);
        var run = scene.Run("fast-forward until nobody move = none max 10");

        run.Advance(0);

        Assert.AreEqual(0, scene.Ticks);
        CollectionAssert.AreEqual(new[] { "x.play line 1: unknown unit 'nobody'" }, run.Problems.ToArray());
    }

    [TestMethod]
    public void AFailedCombatStepEndsTheStepWithItsProblem()
    {
        var scene = new Scene(arriveAfter: 1000, failOn: 3);
        var run = scene.Run("fast-forward until a move = none max 10");

        run.Advance(0);

        Assert.AreEqual(3, scene.Ticks);
        CollectionAssert.AreEqual(new[] { "x.play line 1: combat step failed: boom" }, run.Problems.ToArray());
    }

    [TestMethod]
    public void WithoutAWorldToStepTheStepIsAProblem()
    {
        var run = new Scene(arriveAfter: 1).Run("fast-forward until a move = none max 10", canSkip: false);

        run.Advance(0);

        CollectionAssert.AreEqual(
            new[] { "x.play line 1: step 'fast-forward' cannot run here" },
            run.Problems.ToArray()
        );
    }
}
