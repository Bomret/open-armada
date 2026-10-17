using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Sandbox;

namespace OpenArmada.Tests;

[TestClass]
public class PlayScriptTests
{
    [TestMethod]
    public void StepsKeepFileOrderAndLineNumbersAcrossBlankLinesAndComments()
    {
        var script = PlayScript.Parse(
            "# start\n\nwait 1.5\n  dump first  \n\r\nexpect a shields >= 10\n",
            "play/x.play"
        );

        CollectionAssert.AreEqual(Array.Empty<string>(), script.Problems.ToArray());
        CollectionAssert.AreEqual(new[] { 3, 4, 6 }, script.Steps.Select(s => s.Line).ToArray());
        Assert.AreEqual(1.5, ((WaitStep)script.Steps[0]).Seconds);
        Assert.AreEqual("first", ((DumpStep)script.Steps[1]).Name);
        var expect = (ExpectStep)script.Steps[2];
        Assert.AreEqual(("a", "shields", ">=", 10.0), (expect.Unit, expect.Field, expect.Op, expect.Number!.Value));
    }

    [TestMethod]
    public void TheStemIsTheFileNameWithoutExtension() =>
        Assert.AreEqual("duel", PlayScript.Parse("", "play/duel.play").Stem);

    [TestMethod]
    [DataRow("jump 3", "unknown step 'jump'")]
    [DataRow("wait", "'wait' takes 1 argument, found 0")]
    [DataRow("dump a b", "'dump' takes 1 argument, found 2")]
    [DataRow("expect a shields >", "'expect' takes 4 arguments, found 3")]
    [DataRow("wait soon", "'soon' is not a number of seconds of zero or more")]
    [DataRow("wait -1", "'-1' is not a number of seconds of zero or more")]
    [DataRow("wait NaN", "'NaN' is not a number of seconds of zero or more")]
    [DataRow("dump ../x", "'../x' is no file name")]
    [DataRow("expect a hull > 1", "unknown field 'hull'")]
    [DataRow("expect a shields ~ 1", "unknown operator '~', use one of < <= = >= >")]
    [DataRow("expect a shields > many", "'many' is not a number")]
    [DataRow("expect a destroyed < true", "'destroyed' takes only '=', not '<'")]
    [DataRow("expect a destroyed = maybe", "'maybe' is not true or false")]
    public void AnUnreadableLineIsOneProblemNamingTheFileAndTheLine(string line, string reason)
    {
        var script = PlayScript.Parse($"wait 1\n{line}\n", "play/x.play");

        Assert.AreEqual(1, script.Steps.Count);
        CollectionAssert.AreEqual(new[] { $"play/x.play line 2: {reason}" }, script.Problems.ToArray());
    }

    [TestMethod]
    [DataRow("expect a Engines <= 3", "Engines")]
    [DataRow("expect a lifesupport = 0", "LifeSupport")]
    public void ASystemNameIsAFieldWhateverItsCase(string line, string field)
    {
        var script = PlayScript.Parse(line, "x.play");

        Assert.AreEqual(field, ((ExpectStep)script.Steps.Single()).Field);
    }

    [TestMethod]
    public void AMissingScriptIsOneProblemNamingTheFile()
    {
        var script = PlayScript.Read(Path.Combine(Path.GetTempPath(), "no-such-dir", "gone.play"));

        Assert.AreEqual(0, script.Steps.Count);
        Assert.AreEqual(1, script.Problems.Count);
        StringAssert.Contains(script.Problems[0], "gone.play: script cannot be read");
    }

    [TestMethod]
    public void AScriptFileIsReadFromItsPath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.play");
        File.WriteAllText(path, "wait 2\n");
        try
        {
            Assert.AreEqual(2.0, ((WaitStep)PlayScript.Read(path).Steps.Single()).Seconds);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
