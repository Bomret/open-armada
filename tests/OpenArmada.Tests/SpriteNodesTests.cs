using System.IO;
using System.Linq;
using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Assets;
using OpenArmada.Formats;
using OpenArmada.Sandbox;

namespace OpenArmada.Tests;

// The sprite nodes the sprite tables of an install built in code (INV-0001) attach to a model, and the gaps.
[TestClass]
public class SpriteNodesTests
{
    readonly TempInstall temp = new();

    [TestCleanup]
    public void CleanUp() => temp.CleanUp();

    // Shaped like the install's explosion.spr: eight sprite nodes on critical1..critical8.
    const string ExplosionTable =
        "sprite_table\n@reference=256\n@tmaterial=additive\nbigorange tex 0 0 64 64 @anim=flip4\nsparkball1 tex 0 128 32 32\n"
        + "@sprite_node critical1 bigorange explode1.1 (40,40) (1,1,1) billboard\n"
        + "@sprite_node critical2 sparkball1 explode1.3 (32,32) (1,1,1) billboard\n"
        + "@sprite_node critical3 sparkball1 explode1.3 (32,32) (1,1,1) billboard\n"
        + "@sprite_node critical4 sparkball1 explode1.3 (21,21) (1,1,1) billboard\n"
        + "@sprite_node critical5 sparkball1 explode1.3 (18,18) (1,1,1) billboard\n"
        + "@sprite_node critical6 sparkball1 explode1.3 (15,15) (1,.5,1) billboard\n"
        + "@sprite_node critical7 sparkball1 explode1.3 (12,12) (1,1,1) billboard\n"
        + "@sprite_node critical8 sparkball1 explode1.3 (12,12) (1,1,1)\n";

    // Shaped like the install's animation.spr.
    const string AnimationTable =
        "sprite_table\n@animation explode1.1\ndraw 2 1.5 step\n@keyframes\n0.0\t1\n1.099\t0\n"
        + "@animation explode1.3\ndraw 2 1.5 step\n@keyframes\n0.0\t1\n1.299\t0\n"
        + "@animation fire\ncolour 2 1.5 linear\n@keyframes\n0.0\t(1,1,0)\n0.75\t(1,0.5,0)\n"
        + "@animation flip4\noffset 4 0.4 step\n@auto=square\n@keyframes\n";

    string root = "";

    GameFileSystem Install(params (string Path, string Content)[] more)
    {
        root = temp.MakeInstall(
            new[] { ("Sprites/explosion.spr", ExplosionTable), ("Sprites/animation.spr", AnimationTable) }
                .Concat(more)
                .ToArray()
        );
        return new GameFileSystem(root);
    }

    // A model of a root node "sprites" with the named nodes under it.
    static Sod Model(params string[] nodes)
    {
        var sod = new Sod();
        sod.Nodes.Add(new Sod.Node { Name = "sprites" });
        foreach (var n in nodes)
            sod.Nodes.Add(new Sod.Node { Name = n, Parent = "sprites" });
        return sod;
    }

    [TestMethod]
    public void ExplosionModelResolvesToEightSpriteNodes()
    {
        var files = Install();
        var model = Model(Enumerable.Range(1, 8).Select(i => $"critical{i}").ToArray());
        var resolved = SpriteNodes.For(files, model);
        Assert.AreEqual(8, resolved.Nodes.Count);
        Assert.AreEqual(0, resolved.Reports.Count);
        var first = resolved.Nodes.Single(n => n.Node.Name == "critical1");
        Assert.AreSame(model.Nodes[1], first.Node);
        Assert.AreEqual(("bigorange", "additive"), (first.Sprite.Sprite.Name, first.Sprite.Sprite.Material));
        Assert.AreEqual(4, first.Sprite.Frames.Count); // its own flipbook, as a shot sprite's
        Assert.AreEqual(0.1, first.Sprite.FrameSeconds, 1e-9);
        Assert.AreEqual(
            (40f, 40f, new Vector3(1, 1, 1), true),
            (first.Width, first.Height, first.Colour, first.Billboard)
        );
        Assert.IsTrue(first.At(1.0, loop: true).Draw);
        Assert.IsFalse(first.At(1.2, loop: true).Draw); // explode1.1 hides at 1.099
        var sixth = resolved.Nodes.Single(n => n.Node.Name == "critical6");
        Assert.AreEqual((15f, new Vector3(1, .5f, 1)), (sixth.Width, sixth.Colour));
        Assert.IsTrue(sixth.At(1.2, loop: true).Draw); // explode1.3 hides at 1.299
        Assert.IsFalse(resolved.Nodes.Single(n => n.Node.Name == "critical8").Billboard);
        Assert.AreEqual(0, SpriteNodes.For(files, Model("hull")).Nodes.Count); // a node without a sprite node carries none
    }

    [TestMethod]
    public void ColourAnimationMultipliesTheLineColour()
    {
        var files = Install(
            ("Sprites/damage.spr", "sprite_table\n@sprite_node crew1 sparkball1 fire (5,5) (.5,1,1)\n")
        );
        var crew = SpriteNodes.For(files, Model("crew1")).Nodes.Single();
        var at = crew.At(0.375, loop: true);
        Assert.IsTrue(Vector3.Distance(new Vector3(.5f, .75f, 0), at.Colour) < 1e-4f, at.Colour.ToString());
    }

    [TestMethod]
    public void MissingSpriteOrAnimationIsAGapAndConstIsNoAnimation()
    {
        var files = Install(
            (
                "Sprites/lights.spr",
                "sprite_table\n@sprite_node a nosuch const (1,1) (1,1,1)\n@sprite_node b sparkball1 nosuchanim (1,1) (1,1,1)\n"
                    + "@sprite_node c sparkball1 const (1,1) (1,1,1)\n"
            )
        );
        var resolved = SpriteNodes.For(files, Model("a", "b", "c"));
        CollectionAssert.AreEquivalent(new[] { "b", "c" }, resolved.Nodes.Select(n => n.Node.Name).ToArray());
        Assert.IsTrue(resolved.Nodes.All(n => n.Animation == null));
        Assert.AreEqual(2, resolved.Reports.Count);
        CollectionAssert.AreEqual(new[] { "nosuch" }, resolved.Reports[0].Names.ToArray());
        CollectionAssert.AreEqual(new[] { "nosuchanim" }, resolved.Reports[1].Names.ToArray());
        Assert.AreEqual(1, resolved.Reports[1].Count);
    }

    [TestMethod]
    public void NodeNamesMatchIgnoringCaseAndThroughNodeNameAndTheFirstTableWins()
    {
        var files = Install(
            ("Sprites/a.spr", "sprite_table\n@sprite_node dup sparkball1 const (1,1) (1,1,1)\n"),
            (
                "Sprites/B.spr",
                "sprite_table\n@sprite_node DUP sparkball1 const (2,2) (1,1,1)\n@sprite_node FX_A sparkball1 const (3,3) (1,1,1)\n"
            )
        );
        var resolved = SpriteNodes.For(files, Model("CRITICAL1", "fx.a", "Dup"));
        Assert.AreEqual(40f, resolved.Nodes.Single(n => n.Node.Name == "CRITICAL1").Width);
        Assert.AreEqual(3f, resolved.Nodes.Single(n => n.Node.Name == "fx.a").Width);
        Assert.AreEqual(1f, resolved.Nodes.Single(n => n.Node.Name == "Dup").Width);
    }

    // Ship models carry several instances of one light as "greenconst", "greenconst_2", "greenconst_3".
    [TestMethod]
    public void NumberedInstanceOfANodeTakesItsSpriteNode()
    {
        var files = Install(
            ("Sprites/lights.spr", "sprite_table\n@sprite_node critical1_2 sparkball1 const (5,5) (1,1,1)\n")
        );
        var resolved = SpriteNodes.For(files, Model("critical1_3", "critical1_2", "critical1_x"));
        Assert.AreEqual(40f, resolved.Nodes.Single(n => n.Node.Name == "critical1_3").Width);
        Assert.AreEqual(5f, resolved.Nodes.Single(n => n.Node.Name == "critical1_2").Width); // an exact name wins
        Assert.IsFalse(resolved.Nodes.Any(n => n.Node.Name == "critical1_x"));
    }

    // An unreadable line is reported for a model that has the node it names; one naming no node of the model is silent.
    [TestMethod]
    public void UnreadableSpriteNodeLineIsReportedAndCostsTheTableNothing()
    {
        var files = Install(
            (
                "Sprites/lights.spr",
                "sprite_table\n@sprite_node a sparkball1 const (1,x) (1,1,1)\n@sprite_node b sparkball1 const (1,1) (1,1,1)\n"
                    + "@sprite_node zzz sparkball1 const (1,x) (1,1,1)\n@sprite_node\n"
            )
        );
        var resolved = SpriteNodes.For(files, Model("a", "b"));
        Assert.AreEqual("b", resolved.Nodes.Single().Node.Name);
        StringAssert.Contains(resolved.Reports.Single().Names.Single(), "lights.spr:2");
    }

    // An unreadable line reaches a numbered instance of its node as a readable line does.
    [TestMethod]
    public void UnreadableLineIsReportedForANumberedInstanceOfItsNode()
    {
        var files = Install(("Sprites/lights.spr", "sprite_table\n@sprite_node x sparkball1 const (1,x) (1,1,1)\n"));
        var resolved = SpriteNodes.For(files, Model("x_2"));
        StringAssert.Contains(resolved.Reports.Single().Names.Single(), "lights.spr:2");
    }

    // A node a readable line draws is not reported for an unreadable line of its name in another table.
    [TestMethod]
    public void UnreadableLineIsSilentForANodeAReadableLineDraws()
    {
        var files = Install(
            ("Sprites/a.spr", "sprite_table\n@sprite_node y sparkball1 const (1,1) (1,1,1)\n"),
            ("Sprites/b.spr", "sprite_table\n@sprite_node y sparkball1 const (1,x) (1,1,1)\n")
        );
        var resolved = SpriteNodes.For(files, Model("y"));
        Assert.AreEqual("y", resolved.Nodes.Single().Node.Name);
        Assert.AreEqual(0, resolved.Reports.Count);
    }

    [TestMethod]
    public void SpriteFlipbookFrameFollowsElapsedTime()
    {
        var nodes = SpriteNodes.For(Install(), Model("critical1", "critical2")).Nodes;
        var flip = nodes.Single(n => n.Node.Name == "critical1").Sprite; // four frames of 0.1 s
        Assert.AreEqual(flip.Frames[0], flip.FrameAt(0.05));
        Assert.AreEqual(flip.Frames[2], flip.FrameAt(0.25));
        Assert.AreEqual(flip.Frames[1], flip.FrameAt(0.55)); // the flipbook loops
        var still = nodes.Single(n => n.Node.Name == "critical2").Sprite;
        Assert.AreEqual(still.Frames[0], still.FrameAt(7.3));
    }

    [TestMethod]
    public void ResolvingSeveralModelsReadsEachSpriteTableOnce()
    {
        var files = Install();
        Assert.AreEqual(40f, SpriteNodes.For(files, Model("critical1")).Nodes.Single().Width);
        File.WriteAllText(Path.Combine(root, "Sprites", "explosion.spr"), "sprite_table\n");
        Assert.AreEqual(40f, SpriteNodes.For(files, Model("critical1")).Nodes.Single().Width);
    }
}
