using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Sandbox;
using static OpenArmada.Tests.CombatFixtures;

namespace OpenArmada.Tests;

[TestClass]
public class PlayRunInputTests
{
    [TestMethod]
    public void HoverAndLeaveGoToTheInputExecutorInFileOrder()
    {
        var a = Placed("a", 1, 0);
        var play = new PlayState(new UnitNames([a]), new UnitPoses([a]).Pose, []);
        var sent = new List<PlayStep>();
        var run = new PlayRun(
            PlayScript.Parse("hover a\nleave\n", "x.play"),
            play,
            "unused",
            step =>
            {
                sent.Add(step);

                return null;
            }
        );

        run.Advance(0);

        Assert.IsTrue(run.Finished);
        Assert.AreEqual(0, run.Problems.Count);
        Assert.AreEqual(2, sent.Count);
        Assert.AreEqual("a", ((HoverStep)sent[0]).Unit);
        Assert.IsInstanceOfType<LeaveStep>(sent[1]);
    }
}
