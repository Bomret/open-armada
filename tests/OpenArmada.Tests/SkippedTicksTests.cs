using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Sandbox;
using static OpenArmada.Tests.CombatFixtures;

namespace OpenArmada.Tests;

[TestClass]
public class SkippedTicksTests
{
    static readonly CombatEvent First = new TransportStarted
    {
        Source = Placed("a", 1, 0),
        Target = Placed("b", 2, 10),
    };
    static readonly CombatEvent Second = new TransportStopped
    {
        Source = Placed("a", 1, 0),
        Target = Placed("b", 2, 10),
    };

    [TestMethod]
    public void TheEventsOfEverySkippedStepAreTakenOnceInTickOrder()
    {
        var skipped = new SkippedTicks();

        Assert.IsNull(skipped.Run(() => [First]));
        Assert.IsNull(skipped.Run(() => [Second]));

        CollectionAssert.AreEqual(new[] { First, Second }, (System.Collections.ICollection)skipped.Take());
        Assert.AreEqual(0, skipped.Take().Count);
    }

    [TestMethod]
    public void AStepThatThrowsIsItsProblemAndTheEarlierEventsAreKept()
    {
        var skipped = new SkippedTicks();
        skipped.Run(() => [First]);

        var problem = skipped.Run(() => throw new InvalidOperationException("boom"));

        Assert.AreEqual("combat step failed: boom", problem);
        CollectionAssert.AreEqual(new[] { First }, (System.Collections.ICollection)skipped.Take());
    }
}
