using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Sandbox;
using static OpenArmada.Tests.CombatFixtures;

namespace OpenArmada.Tests;

[TestClass]
public class PlayStateTests
{
    // Two ships that hold states (a hits b), and a rock that holds none.
    sealed class Scene
    {
        public readonly PlacedObject A = Placed("a", 1, 1.5f, 2);
        public readonly PlacedObject B = Placed("b", 2, 10);
        public readonly PlacedObject Rock = Placed("rock", 0, 50);
        public readonly UnitCombatState StateA;
        public readonly UnitCombatState StateB;
        public readonly PlayState Play;

        public Scene()
        {
            var systems = new[]
            {
                Sys(SystemNames.Sensors, 4),
                Sys(SystemNames.Engines, 6),
                Sys(SystemNames.Weapons, 5),
            };
            StateA = new UnitCombatState(A, Profile(systems: systems, crew: 40.5f), "a.odf", 0, true);
            StateB = new UnitCombatState(B, Profile(), "b.odf", 0, true);
            var names = new UnitNames([A, B, Rock]);
            var poses = new UnitPoses([A, B, Rock]);
            Play = new PlayState(names, poses.Pose, [StateA, StateB]);
        }

        public static ExpectStep Expect(string unit, string field, string op, string value) =>
            (ExpectStep)PlayScript.Parse($"expect {unit} {field} {op} {value}", "x.play").Steps.Single();
    }

    [TestMethod]
    public void TheDumpHasOneLineForEveryUnitWithFixedFieldsAndSystemOrder()
    {
        var scene = new Scene();
        scene.StateA.Order = scene.StateB;

        var lines = scene.Play.Dump().Split('\n');

        Assert.AreEqual(
            "a team=1 pos=1.50,0.00,2.00 shields=100.00 crew=40.50 Engines=6.00/6.00/intact "
                + "Weapons=5.00/5.00/intact Sensors=4.00/4.00/intact "
                + "derelict=false immobile=false coasting=false destroyed=false dying=false alert=Red order=b target=none shieldDown=false",
            lines[0]
        );
        Assert.AreEqual(4, lines.Length);
        Assert.AreEqual("", lines[3]);
    }

    [TestMethod]
    public void TheDumpListsAUnitWithoutCombatState() =>
        Assert.AreEqual("rock team=0 pos=50.00,0.00,0.00 state=none", new Scene().Play.Dump().Split('\n')[2]);

    [TestMethod]
    public void TheDumpListsADestroyedUnitWithItsDestroyedFlagSet()
    {
        var scene = new Scene();
        scene.StateB.Destroyed = true;

        StringAssert.Contains(scene.Play.Dump().Split('\n')[1], "immobile=false coasting=false destroyed=true");
    }

    [TestMethod]
    public void TheDumpShowsADyingUnitAsDyingAndNotYetDestroyed()
    {
        var scene = new Scene();
        scene.StateB.Dying = true;

        StringAssert.Contains(
            scene.Play.Dump().Split('\n')[1],
            "immobile=true coasting=true destroyed=false dying=true"
        );
    }

    [TestMethod]
    public void ADyingExpectationHoldsDuringThePhaseAndNotBefore()
    {
        var scene = new Scene();

        Assert.IsNull(scene.Play.Expect(Scene.Expect("b", "dying", "=", "false")));
        Assert.IsNotNull(scene.Play.Expect(Scene.Expect("b", "dying", "=", "true")));
        scene.StateB.Dying = true;
        Assert.IsNull(scene.Play.Expect(Scene.Expect("b", "dying", "=", "true")));
    }

    [TestMethod]
    public void TheDumpShowsADestroyedSystemAndTheImmobilityItCauses()
    {
        var scene = new Scene();
        var engines = scene.StateA.Systems[SystemNames.Engines];
        engines.HitPoints = 0;
        engines.Destroyed = true;

        var line = scene.Play.Dump().Split('\n')[0];

        StringAssert.Contains(line, "Engines=0.00/6.00/destroyed");
        StringAssert.Contains(line, "immobile=true");
        StringAssert.Contains(line, "coasting=true");
    }

    [TestMethod]
    public void TheDumpShowsTheShieldDownOverlayOfADestroyedShieldGenerator()
    {
        var generator = Sys(SystemNames.ShieldGenerator, 3);
        var unit = new UnitCombatState(Placed("c", 1, 0), Profile(systems: [generator]), "c.odf", 0, true);
        var play = new PlayState(new UnitNames([unit.Unit]), new UnitPoses([unit.Unit]).Pose, [unit]);
        unit.DestroySystem(SystemNames.ShieldGenerator);

        StringAssert.EndsWith(play.Dump(), " shieldDown=true\n");

        unit.Derelict = true;

        StringAssert.EndsWith(play.Dump(), " shieldDown=false\n");
    }

    [TestMethod]
    public void TheDumpIsFormattedTheSameUnderEveryCulture()
    {
        var scene = new Scene();
        var before = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
        try
        {
            StringAssert.Contains(scene.Play.Dump(), "pos=1.50,0.00,2.00 shields=100.00");
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = before;
        }
    }

    [TestMethod]
    [DataRow("a", "shields", "=", "100")]
    [DataRow("a", "shields", ">=", "99.5")]
    [DataRow("a", "shields", "<", "101")]
    [DataRow("a", "crew", "=", "40.5")]
    [DataRow("a", "Engines", ">", "5")]
    [DataRow("a", "destroyed", "=", "false")]
    [DataRow("a", "target", "=", "none")]
    [DataRow("b", "destroyed", "=", "false")]
    public void AnExpectationThatHoldsGivesNoProblem(string unit, string field, string op, string value) =>
        Assert.IsNull(new Scene().Play.Expect(Scene.Expect(unit, field, op, value)));

    [TestMethod]
    public void ATargetExpectationComparesTheUnitsNotTheirNames()
    {
        var scene = new Scene();
        scene.StateA.Target = scene.StateB;

        Assert.IsNull(scene.Play.Expect(Scene.Expect("a", "target", "=", "b")));
        Assert.IsNotNull(scene.Play.Expect(Scene.Expect("a", "target", "=", "none")));
    }

    [TestMethod]
    public void AFailedExpectationNamesTheExpectedAndTheActualValue()
    {
        var scene = new Scene();
        scene.StateA.Shields = 40;

        Assert.AreEqual(
            "expectation failed: a shields: expected >= 50, actual 40.00",
            scene.Play.Expect(Scene.Expect("a", "shields", ">=", "50"))
        );
    }

    [TestMethod]
    public void AFailedTargetExpectationNamesBothUnits()
    {
        var scene = new Scene();
        scene.StateA.Target = scene.StateB;

        Assert.AreEqual(
            "expectation failed: a target: expected = none, actual b",
            scene.Play.Expect(Scene.Expect("a", "target", "=", "none"))
        );
    }

    [TestMethod]
    public void ADestroyedExpectationHoldsForADestroyedAndRemovedUnit()
    {
        var scene = new Scene();
        scene.StateB.Destroyed = true;

        Assert.IsNull(scene.Play.Expect(Scene.Expect("b", "destroyed", "=", "true")));
    }

    [TestMethod]
    public void ADyingExpectationHoldsAsFalseForADestroyedAndRemovedUnit()
    {
        var scene = new Scene();
        scene.StateB.Destroyed = true;

        Assert.IsNull(scene.Play.Expect(Scene.Expect("b", "dying", "=", "false")));
    }

    [TestMethod]
    public void AnyOtherExpectationOnADestroyedAndRemovedUnitIsAProblem()
    {
        var scene = new Scene();
        scene.StateB.Destroyed = true;

        Assert.AreEqual(
            "unit 'b' was destroyed and removed",
            scene.Play.Expect(Scene.Expect("b", "shields", ">", "0"))
        );
    }

    [TestMethod]
    [DataRow("nobody", "shields", "unknown unit 'nobody'")]
    [DataRow("rock", "shields", "unit 'rock' has no combat state")]
    [DataRow("b", "Engines", "unit 'b' has no system 'Engines'")]
    public void AnExpectationThatCannotBeAskedIsAProblem(string unit, string field, string problem) =>
        Assert.AreEqual(problem, new Scene().Play.Expect(Scene.Expect(unit, field, "=", "1")));

    [TestMethod]
    public void AnUnknownTargetNameIsAProblem() =>
        Assert.AreEqual("unknown unit 'zed'", new Scene().Play.Expect(Scene.Expect("a", "target", "=", "zed")));

    [TestMethod]
    public void WritingCreatesTheFolderAndKeepsOtherFiles()
    {
        var folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "out");
        try
        {
            Assert.IsNull(PlayState.Write(folder, "one", "1\n"));
            Assert.IsNull(PlayState.Write(folder, "two", "2\n"));

            Assert.AreEqual("1\n", File.ReadAllText(Path.Combine(folder, "one.txt")));
            Assert.AreEqual(2, Directory.GetFiles(folder).Length);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(folder)!, true);
        }
    }

    [TestMethod]
    public void AFileThatCannotBeWrittenIsAProblemNotAnException()
    {
        var blocker = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.txt");
        File.WriteAllText(blocker, "a file where the folder should be");
        try
        {
            var problem = PlayState.Write(Path.Combine(blocker, "out"), "x", "1");

            StringAssert.StartsWith(problem, "cannot write '");
            StringAssert.Contains(problem, "x.txt");
        }
        finally
        {
            File.Delete(blocker);
        }
    }
}
