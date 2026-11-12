using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Sandbox;
using static OpenArmada.Tests.CombatFixtures;

namespace OpenArmada.Tests;

// INV-0008: one play script over one sandbox plan writes identical state dumps on every run. The plan is built in
// code (INV-0001): two fighters whose weapons roll a hit chance and a damage variance, so the dumps depend on the
// combat random source. The run steps the combat world at the fixed tick before each Advance, as the viewer does.
[TestClass]
public class PlayRunDeterminismTests
{
    const string Script = "dump first\nfast-forward until b shields < 60 max 20\ndump middle\nwait 1\ndump last";

    // Runs the script over a fresh world of the plan, with the combat random source `random`, writing the dumps to
    // `folder`.
    static void Run(string folder, Func<double> random)
    {
        var a = Placed("a", 1, 0);
        var b = Placed("b", 2, 50);
        var beam = Beam(damage: 10, variance: 5);
        CombatUnit Unit(PlacedObject o) =>
            new()
            {
                Unit = o,
                Profile = Profile([Weapon(beam, delay: 0.5f, range: 100, hitChance: 0.5f)], maxHealth: 100),
                DefinitionName = o.Label + ".odf",
                Radius = 0,
                Mobile = false,
            };
        var poses = new UnitPoses([a, b]);
        var world = new CombatWorld(poses, [Unit(a), Unit(b)], 1, random, (_, _, pose) => pose.Position);
        world.Attack(a, b);
        world.Attack(b, a);
        var play = new PlayState(new UnitNames([a, b]), poses.Pose, [world.State(a)!, world.State(b)!]);
        var run = new PlayRun(
            PlayScript.Parse(Script, "determinism.play"),
            play,
            folder,
            skip: seconds =>
            {
                world.Step(seconds);

                return null;
            }
        );
        for (var frame = 0; frame < 1000 && !run.Finished; frame++)
        {
            world.Step(CombatWorld.Tick);
            run.Advance(CombatWorld.Tick);
        }

        Assert.IsTrue(run.Finished, "the script ends");
        Assert.AreEqual(0, run.Problems.Count, string.Join("; ", run.Problems));
    }

    // The folder's dumps in file-name order: first, last, middle.
    static string[] Dumps(string folder)
    {
        var files = Directory.GetFiles(folder).Order().ToArray();
        CollectionAssert.AreEqual(
            new[] { "first.txt", "last.txt", "middle.txt" },
            files.Select(Path.GetFileName).ToArray(),
            "every dump step writes its dump"
        );

        return files.Select(File.ReadAllText).ToArray();
    }

    [TestMethod]
    public void TwoRunsOfOneScriptOverOnePlanWriteIdenticalDumpsAndTheFightChangesTheState()
    {
        var root = Path.Combine(Path.GetTempPath(), "play-determinism-" + Guid.NewGuid().ToString("N"));
        try
        {
            string[] Once(string name)
            {
                var folder = Path.Combine(root, name);
                Run(folder, new Random(PlayScript.Seed).NextDouble);

                return Dumps(folder);
            }

            var one = Once("one");
            var two = Once("two");

            CollectionAssert.AreEqual(one, two, "the two runs write the same dumps");
            Assert.AreNotEqual(one[0], one[1], "the fight changes the state between the first and the last dump");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }
}
