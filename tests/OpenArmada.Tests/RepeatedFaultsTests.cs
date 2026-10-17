using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Diagnostics;

namespace OpenArmada.Tests;

[TestClass]
public class RepeatedFaultsTests
{
    [TestMethod]
    public void AFaultEveryFrameLogsOnceThenOneCountEntryOnFlush()
    {
        using var log = new CapturedLog();
        var faults = new RepeatedFaults(Area.Combat);
        for (var frame = 0; frame < 100; frame++)
        {
            faults.Report("combat step failed: boom");
        }

        StringAssert.Contains(log.Lines.Single(), " ERROR combat: combat step failed: boom");
        faults.Flush();
        Assert.AreEqual(2, log.Lines.Count, log.Text);
        StringAssert.Contains(log.Lines[1], " ERROR combat: combat step failed: boom (99 more times)");
    }

    [TestMethod]
    public void TwoDifferentFaultsEachGetTheirOwnFirstEntry()
    {
        using var log = new CapturedLog();
        var faults = new RepeatedFaults(Area.Combat);
        faults.Report("combat step failed: a");
        faults.Report("combat step failed: b");

        Assert.AreEqual(2, log.Lines.Count, log.Text);
        StringAssert.Contains(log.Lines[0], "failed: a");
        StringAssert.Contains(log.Lines[1], "failed: b");
    }

    [TestMethod]
    public void FlushOfFaultsSeenOnceLogsNothingAndFlushTwiceLogsTheCountOnce()
    {
        using var log = new CapturedLog();
        var faults = new RepeatedFaults(Area.Combat);
        faults.Report("x");
        faults.Flush();
        Assert.AreEqual(1, log.Lines.Count, log.Text);
        faults.Report("x");
        faults.Flush();
        faults.Flush();
        Assert.AreEqual(2, log.Lines.Count, log.Text);
        StringAssert.Contains(log.Lines[1], "x (1 more time)");
    }
}
