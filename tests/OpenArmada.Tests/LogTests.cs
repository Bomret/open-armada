using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Diagnostics;

namespace OpenArmada.Tests;

[TestClass]
public class LogTests
{
    const string Pattern = @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}[+-]\d{2}:\d{2} (INFO|WARN|ERROR) [a-z]+: ";

    [TestMethod]
    public void EntryHasTimeLevelAreaAndText()
    {
        using var log = new CapturedLog();
        Log.Warn(Area.Check, "NOTE x");
        Assert.IsTrue(Regex.IsMatch(log.Lines.Single(), Pattern + "NOTE x$"), log.Text);
        StringAssert.Contains(log.Lines[0], " WARN check: NOTE x");
    }

    [TestMethod]
    public void TimeIsInvariantUnderANonGregorianCulture()
    {
        var old = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("th-TH");
        try
        {
            using var log = new CapturedLog();
            Log.Info(Area.Run, "x");
            Assert.IsTrue(Regex.IsMatch(log.Lines.Single(), Pattern + "x$"), log.Text);
        }
        finally
        {
            CultureInfo.CurrentCulture = old;
        }
    }

    [TestMethod]
    [DataRow("a\nb")]
    [DataRow("a\r\nb")]
    [DataRow("a\rb")]
    [DataRow("a\nb\n")]
    [DataRow("a\r\nb\r\n")]
    public void LineBreaksGiveOnePrefixedLinePerTextLineAndOneEntry(string text)
    {
        using var log = new CapturedLog();
        Log.Error(Area.Run, text);
        Assert.AreEqual(2, log.Lines.Count);
        Assert.IsTrue(Regex.IsMatch(log.Lines[0], Pattern + "a$"));
        Assert.IsTrue(Regex.IsMatch(log.Lines[1], Pattern + "b$"));
        Assert.IsFalse(log.Text.Contains('\r'));
        Assert.AreEqual(1, Log.ErrorCount);
    }

    [TestMethod]
    public void CountsTrackWarnAndErrorEntriesNotInfo()
    {
        using var log = new CapturedLog();
        Log.Info(Area.Run, "i");
        Log.Warn(Area.Run, "w");
        Log.Warn(Area.Run, "w");
        Log.Error(Area.Run, "e");
        Assert.AreEqual((2, 1), (Log.WarnCount, Log.ErrorCount));
    }

    [TestMethod]
    public void AreasAreTheFixedLowercaseSet()
    {
        var names = Enum.GetValues<Area>().Select(Log.AreaName).ToArray();
        CollectionAssert.AreEquivalent(
            new[] { "run", "check", "browser", "sandbox", "combat", "assets", "render" },
            names
        );
    }

    [TestMethod]
    public void ThrowingSinkNeverThrowsOutOfLog()
    {
        using var _ = Log.Redirect(_ => throw new InvalidOperationException("sink broke"));
        Log.Error(Area.Run, "lost");
        Assert.AreEqual(1, Log.ErrorCount);
    }

    [TestMethod]
    public void RedirectRestoresThePreviousSink()
    {
        using var outer = new CapturedLog();
        using (new CapturedLog())
        {
            Log.Info(Area.Run, "inner");
        }

        Log.Info(Area.Run, "outer");
        Assert.AreEqual(1, outer.Lines.Count);
        StringAssert.EndsWith(outer.Lines[0], "outer");
    }
}
