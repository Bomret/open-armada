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
    public void InputStepsParseToTheirRecords()
    {
        var script = PlayScript.Parse(
            string.Join(
                "\n",
                "select kbattle58",
                "attack enemy#2",
                "move 100 -250.5",
                "box 10 20 300 400",
                "box a b#2",
                "press Escape",
                "hold Left 1.5",
                "scroll up",
                "scroll down 3",
                "turn -40",
                "shot after",
                "shot close kbattle58",
                "shot closer enemy#2 3",
                "transport enemy#3"
            ),
            "x.play"
        );

        CollectionAssert.AreEqual(Array.Empty<string>(), script.Problems.ToArray());
        Assert.AreEqual("kbattle58", ((SelectStep)script.Steps[0]).Unit);
        Assert.AreEqual("enemy#2", ((AttackStep)script.Steps[1]).Unit);
        Assert.AreEqual((100.0, -250.5), (((MoveStep)script.Steps[2]).X, ((MoveStep)script.Steps[2]).Z));
        var rect = (BoxRectStep)script.Steps[3];
        Assert.AreEqual((10f, 20f, 300f, 400f), (rect.Left, rect.Top, rect.Right, rect.Bottom));
        CollectionAssert.AreEqual(new[] { "a", "b#2" }, ((BoxUnitsStep)script.Steps[4]).Units.ToArray());
        Assert.AreEqual("Escape", ((PressStep)script.Steps[5]).Key);
        var hold = (HoldStep)script.Steps[6];
        Assert.AreEqual(("Left", 1.5), (hold.Key, hold.Seconds));
        Assert.AreEqual((true, 1), (((ScrollStep)script.Steps[7]).Up, ((ScrollStep)script.Steps[7]).Count));
        Assert.AreEqual((false, 3), (((ScrollStep)script.Steps[8]).Up, ((ScrollStep)script.Steps[8]).Count));
        Assert.AreEqual(-40.0, ((TurnStep)script.Steps[9]).Pixels);
        var shot = (ShotStep)script.Steps[10];
        Assert.AreEqual(("after", (string?)null, 1), (shot.Name, shot.Unit, shot.Scale));
        var close = (ShotStep)script.Steps[11];
        Assert.AreEqual(("close", "kbattle58", 1), (close.Name, close.Unit, close.Scale));
        var closer = (ShotStep)script.Steps[12];
        Assert.AreEqual(("closer", "enemy#2", 3), (closer.Name, closer.Unit, closer.Scale));
        Assert.AreEqual("enemy#3", ((TransportStep)script.Steps[13]).Unit);
    }

    [TestMethod]
    [DataRow("select", "'select' takes 1 argument, found 0")]
    [DataRow("attack a b", "'attack' takes 1 argument, found 2")]
    [DataRow("transport", "'transport' takes 1 argument, found 0")]
    [DataRow("move 1", "'move' takes 2 arguments, found 1")]
    [DataRow("move 1 x", "'x' is not a number")]
    [DataRow("box", "'box' takes 4 numbers or one or more unit names, found 0 arguments")]
    [DataRow("box 1 2 3 x", "'x' is not a number")]
    [DataRow("box 1 2 3", "'box' takes 4 numbers or unit names, found 3 numbers")]
    [DataRow("box 1 2 3 4 5", "'box' takes 4 numbers or unit names, found 5 numbers")]
    [DataRow("press", "'press' takes 1 argument, found 0")]
    [DataRow("hold Left", "'hold' takes 2 arguments, found 1")]
    [DataRow("hold Left -1", "'-1' is not a number of seconds of zero or more")]
    [DataRow("scroll sideways", "'sideways' is not up or down")]
    [DataRow("scroll up 0", "'0' is not a whole number of one or more")]
    [DataRow("scroll", "'scroll' takes 1 or 2 arguments, found 0")]
    [DataRow("turn left", "'left' is not a number")]
    [DataRow("shot ../x", "'../x' is no file name")]
    [DataRow("shot", "'shot' takes 1 to 3 arguments, found 0")]
    [DataRow("shot x a 2 4", "'shot' takes 1 to 3 arguments, found 4")]
    [DataRow("shot x a 0", "'0' is not a whole number of one or more")]
    [DataRow("shot x a 1.5", "'1.5' is not a whole number of one or more")]
    public void AnUnreadableInputStepIsOneProblemNamingTheFileAndTheLine(string line, string reason)
    {
        var script = PlayScript.Parse($"wait 1\n{line}\n", "play/x.play");

        Assert.AreEqual(1, script.Steps.Count);
        CollectionAssert.AreEqual(new[] { $"play/x.play line 2: {reason}" }, script.Problems.ToArray());
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
    [DataRow("expect a dying < true", "'dying' takes only '=', not '<'")]
    [DataRow("expect a dying = maybe", "'maybe' is not true or false")]
    [DataRow("expect a team < 1", "'team' takes only '=', not '<'")]
    [DataRow("expect a team = 1.5", "'1.5' is not an integer")]
    [DataRow("expect a team = red", "'red' is not an integer")]
    [DataRow("expect a move < open", "'move' takes only '=', not '<'")]
    [DataRow("expect a move = 100,200", "'100,200' is not open or none")]
    [DataRow("fast-forward a move = none", "'fast-forward' takes 'until <unit> <field> <op> <value> max <seconds>'")]
    [DataRow(
        "fast-forward until a move = none for 30",
        "'fast-forward' takes 'until <unit> <field> <op> <value> max <seconds>'"
    )]
    [DataRow("fast-forward until a move = none max 0", "'0' is not a number of seconds above zero and at most 3600")]
    [DataRow(
        "fast-forward until a move = none max soon",
        "'soon' is not a number of seconds above zero and at most 3600"
    )]
    [DataRow(
        "fast-forward until a move = none max 3600.5",
        "'3600.5' is not a number of seconds above zero and at most 3600"
    )]
    [DataRow("fast-forward until a hull > 1 max 5", "unknown field 'hull'")]
    public void AnUnreadableLineIsOneProblemNamingTheFileAndTheLine(string line, string reason)
    {
        var script = PlayScript.Parse($"wait 1\n{line}\n", "play/x.play");

        Assert.AreEqual(1, script.Steps.Count);
        CollectionAssert.AreEqual(new[] { $"play/x.play line 2: {reason}" }, script.Problems.ToArray());
    }

    [TestMethod]
    public void ADyingExpectationParsesLikeADestroyedOne()
    {
        var step = (ExpectStep)PlayScript.Parse("expect a dying = true", "x.play").Steps.Single();

        Assert.AreEqual("dying", step.Field);
        Assert.AreEqual("true", step.Value);
    }

    [TestMethod]
    public void AFastForwardStepHoldsItsConditionAsAnExpectationAndItsMostSeconds()
    {
        var step = (FastForwardStep)
            PlayScript.Parse("fast-forward until Mover move = none max 30", "x.play").Steps.Single();

        Assert.AreEqual(30, step.MaxSeconds);
        Assert.AreEqual("Mover", step.Until.Unit);
        Assert.AreEqual("move", step.Until.Field);
        Assert.AreEqual("none", step.Until.Value);
        Assert.AreEqual(1, step.Until.Line);
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
