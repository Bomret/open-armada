using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Formats;

namespace OpenArmada.Tests;

// Sprite tables are built in code (INV-0001).
[TestClass]
public class SpriteTableTests
{
    static SpriteTable Read(string root, params (string Name, string Text)[] tables)
    {
        var map = tables.ToDictionary(t => t.Name, t => t.Text, System.StringComparer.OrdinalIgnoreCase);

        return SpriteTable.Read(root, n => map.TryGetValue(n, out var t) ? t : null);
    }

    const string Head = "sprite_table\n\n";

    [TestMethod]
    public void ReadsRowsWithReferenceMaterialAndComments()
    {
        var t = Read(
            "a.spr",
            ("a.spr", Head + "# c\n@reference=256\n@tmaterial=interface\nbtn.0\tfguic04\t0\t176\t256\t80\t#left\n")
        );
        var s = t.Sprites.Single();
        Assert.AreEqual("btn.0", s.Name);
        Assert.AreEqual("fguic04", s.Texture);
        Assert.AreEqual((0, 176, 256, 80), (s.U, s.V, s.W, s.H));
        Assert.AreEqual((256, 256), (s.ReferenceWidth, s.ReferenceHeight));
        Assert.AreEqual("interface", s.Material);
        Assert.AreEqual("a.spr", s.Table);
        Assert.AreEqual(6, s.Line);
    }

    [TestMethod]
    public void ReferenceWidthAndHeightSetEachAxisAndStayInForce()
    {
        var t = Read(
            "a.spr",
            ("a.spr", Head + "@referenceWidth=128\n@referenceHeight=64\nx t 0 0 32 32\ny t 0 0 32 32\n")
        );
        Assert.IsTrue(t.Sprites.All(s => (s.ReferenceWidth, s.ReferenceHeight) == (128, 64)));
    }

    [TestMethod]
    public void RowTrailingDirectivesAreRead()
    {
        var t = Read(
            "a.spr",
            ("a.spr", Head + "@skip=(0,0,0)\nc t 0 32 32 32 @anim=cursor4x1 @cursor @origin=(16,8)\n")
        );
        var s = t.Sprites.Single();
        Assert.AreEqual("cursor4x1", s.Anim);
        Assert.IsTrue(s.Cursor);
        Assert.AreEqual((16, 8), s.Origin);
        Assert.AreEqual("(0,0,0)", s.Skip);
    }

    [TestMethod]
    public void IncludeBringsItsSpritesAndAnimationsInOrder()
    {
        var t = Read(
            "m.spr",
            ("m.spr", Head + "@include b.spr\nfirst t 0 0 1 1\n"),
            ("b.spr", Head + "inner t 0 0 1 1\n")
        );
        CollectionAssert.AreEqual(new[] { "inner", "first" }, t.Sprites.Select(s => s.Name).ToArray());
        Assert.AreEqual("b.spr", t.Sprites[0].Table);
    }

    [TestMethod]
    public void IncludeCycleIsReported()
    {
        var e = Assert.ThrowsExactly<InvalidDataException>(() =>
            Read("a.spr", ("a.spr", Head + "@include b.spr\n"), ("b.spr", Head + "@include a.spr\n"))
        );
        StringAssert.Contains(e.Message, "cycle");
        StringAssert.Contains(e.Message, "b.spr:3");
    }

    [TestMethod]
    public void MissingIncludeIsReportedWithTableAndLine()
    {
        var e = Assert.ThrowsExactly<InvalidDataException>(() =>
            Read("a.spr", ("a.spr", Head + "@include gone.spr\n"))
        );
        StringAssert.Contains(e.Message, "a.spr:3");
        StringAssert.Contains(e.Message, "gone.spr");
    }

    [TestMethod]
    public void IncludeOfTheSameTableTwiceIsNotACycle()
    {
        var t = Read(
            "a.spr",
            ("a.spr", Head + "@include b.spr\n@include c.spr\n"),
            ("b.spr", Head + "@include d.spr\n"),
            ("c.spr", Head + "@include d.spr\n"),
            ("d.spr", Head + "x t 0 0 1 1\n")
        );
        Assert.AreEqual(1, t.Sprites.Count);
    }

    [TestMethod]
    [Timeout(10000)]
    public void ChainOfIncludeDiamondsReadsPromptly()
    {
        var tables = Enumerable
            .Range(0, 30)
            .Select(i => ($"t{i}.spr", Head + $"@include t{i + 1}.spr\n@include t{i + 1}.spr\n"))
            .Append(("t30.spr", Head + "x t 0 0 1 1\n"))
            .ToArray();
        Assert.AreEqual(1, Read("t0.spr", tables).Sprites.Count);
    }

    [TestMethod]
    public void HugeFlipbookCountIsReportedAtItsLine()
    {
        var e = Assert.ThrowsExactly<InvalidDataException>(() =>
            Read(
                "a.spr",
                ("a.spr", Head + "@animation a\noffset 2147483647 1 step\n@auto=square\ns t 0 0 8 8 @anim=a\n")
            )
        );
        StringAssert.Contains(e.Message, "a.spr:4");
    }

    [TestMethod]
    public void FlipbookLeavingTheReferenceAreaIsReportedAtTheSprite()
    {
        var e = Assert.ThrowsExactly<InvalidDataException>(() =>
            Read(
                "a.spr",
                ("a.spr", Head + "@animation a\noffset 4 1 step\n@auto=square\n@reference=32\ns t 0 0 32 32 @anim=a\n")
            )
        );
        StringAssert.Contains(e.Message, "a.spr:7");
    }

    [TestMethod]
    public void FlipbookWhoseCellsOverflowIntegersIsReported() =>
        Assert.ThrowsExactly<InvalidDataException>(() =>
            Read(
                "a.spr",
                ("a.spr", Head + "@animation a\noffset 4 1 step\n@auto=square\ns t 0 0 2000000000 8 @anim=a\n")
            )
        );

    [TestMethod]
    public void UnterminatedEmitterIsReportedAtItsLine()
    {
        var e = Assert.ThrowsExactly<InvalidDataException>(() =>
            Read("a.spr", ("a.spr", Head + "@emitter rift\ntype=spray\nx t 0 0 1 1\n"))
        );
        StringAssert.Contains(e.Message, "a.spr:3");
    }

    [TestMethod]
    public void ShortRowAfterKeyframesIsReportedNotSwallowed()
    {
        var e = Assert.ThrowsExactly<InvalidDataException>(() =>
            Read(
                "a.spr",
                ("a.spr", Head + "@animation f\ncolour 2 1 linear\n@keyframes\n0 (1,0,0)\n1 (0,0,0)\nx t 0 0 1\n")
            )
        );
        StringAssert.Contains(e.Message, "a.spr:8");
    }

    [TestMethod]
    public void KeyframeCountOtherThanTheAnimationsCountIsReported()
    {
        // The game's own font tables declare one keyframe more than they hold, so too few is a note.
        var few = Read(
            "a.spr",
            ("a.spr", Head + "@animation f\ncolour 3 1 linear\n@keyframes\n0 (1,0,0)\n1 (0,0,0)\n")
        );
        StringAssert.Contains(few.Notes.Single(), "a.spr:3");
        var many = Assert.ThrowsExactly<InvalidDataException>(() =>
            Read(
                "a.spr",
                ("a.spr", Head + "@animation f\ncolour 1 1 linear\n@keyframes\n0 (1,0,0)\n1 (0,0,0)\ns t 0 0 1 1\n")
            )
        );
        StringAssert.Contains(many.Message, "a.spr:3");
    }

    [TestMethod]
    public void KeyframeLinesOfOneOrTwoNumbersAreKept()
    {
        var t = Read(
            "a.spr",
            (
                "a.spr",
                Head
                    + "@animation w\nwidth 2 0.0 step\n@keyframes\n3\t\n5\n@animation o\noffset 2 0.0 step\n@keyframes\n0\t0\n4 0 #c\n"
            )
        );
        Assert.AreEqual(2, t.Animations["w"].Keyframes.Count);
        Assert.AreEqual(2, t.Animations["o"].Keyframes.Count);
    }

    [TestMethod]
    public void AnimationWithoutFieldLineIsReported()
    {
        var e = Assert.ThrowsExactly<InvalidDataException>(() => Read("a.spr", ("a.spr", Head + "@animation f\n")));
        StringAssert.Contains(e.Message, "a.spr:3");
    }

    [TestMethod]
    public void NumericLinesBeforeTheFieldLineAreReportedByTheFieldLineCheck()
    {
        var e = Assert.ThrowsExactly<InvalidDataException>(() =>
            Read("a.spr", ("a.spr", Head + "@animation f\n1 2\noffset 1 1 step\n"))
        );
        StringAssert.Contains(e.Message, "a.spr:4");
        StringAssert.Contains(e.Message, "expected '<offset|colour|draw|width>");
    }

    [TestMethod]
    public void EmitterWithoutNameIsReported()
    {
        var e = Assert.ThrowsExactly<InvalidDataException>(() =>
            Read("a.spr", ("a.spr", Head + "@emitter\n@end_emitter\n"))
        );
        StringAssert.Contains(e.Message, "a.spr:3");
        StringAssert.Contains(e.Message, "@emitter names no emitter");
    }

    [TestMethod]
    public void UnrecognizedDirectiveIsReportedWithTableAndLine()
    {
        var e = Assert.ThrowsExactly<InvalidDataException>(() => Read("a.spr", ("a.spr", Head + "@bogus=1\n")));
        StringAssert.Contains(e.Message, "a.spr:3");
        StringAssert.Contains(e.Message, "@bogus");
    }

    [TestMethod]
    public void UnrecognizedRowDirectiveIsReported()
    {
        var e = Assert.ThrowsExactly<InvalidDataException>(() =>
            Read("a.spr", ("a.spr", Head + "x t 0 0 1 1 @weird\n"))
        );
        StringAssert.Contains(e.Message, "a.spr:3");
        StringAssert.Contains(e.Message, "@weird");
    }

    [TestMethod]
    public void MalformedRowIsReported()
    {
        var e = Assert.ThrowsExactly<InvalidDataException>(() => Read("a.spr", ("a.spr", Head + "x t 0 zero 1 1\n")));
        StringAssert.Contains(e.Message, "a.spr:3");
    }

    [TestMethod]
    public void MissingHeaderIsReported() =>
        Assert.ThrowsExactly<InvalidDataException>(() => Read("a.spr", ("a.spr", "x t 0 0 1 1\n")));

    [TestMethod]
    public void SquareAnimationStepsRowMajorOverTheGrid()
    {
        var t = Read(
            "a.spr",
            (
                "a.spr",
                Head
                    + "@animation tex2x2\noffset 4 2.0 step\n@auto=square\n@keyframes\n@reference=64\ns t 0 0 32 32 @anim=tex2x2\n"
            )
        );
        var s = t.Sprites.Single();
        CollectionAssert.AreEqual(
            new[] { (0, 0), (32, 0), (0, 32), (32, 32) },
            t.Frames(s).Select(f => (f.U, f.V)).ToArray()
        );
        Assert.AreEqual(0.5, t.Animations["tex2x2"].FrameSeconds);
    }

    [TestMethod]
    public void RowAnimationStepsAcrossAndColumnDown()
    {
        var t = Read(
            "a.spr",
            (
                "a.spr",
                Head
                    + "@animation r\noffset 3 1 step\n@auto=row\n@keyframes\n@animation c\noffset 2 1 step\n@auto=column\n@keyframes\n"
                    + "a t 0 0 8 8 @anim=r\nb t 0 0 8 8 @anim=c\n"
            )
        );
        CollectionAssert.AreEqual(
            new[] { (0, 0), (8, 0), (16, 0) },
            t.Frames(t.Sprites[0]).Select(f => (f.U, f.V)).ToArray()
        );
        CollectionAssert.AreEqual(new[] { (0, 0), (0, 8) }, t.Frames(t.Sprites[1]).Select(f => (f.U, f.V)).ToArray());
    }

    [TestMethod]
    public void SpriteWithoutAnimationHasItsOwnRegionAsOnlyFrame()
    {
        var t = Read("a.spr", ("a.spr", Head + "s t 1 2 3 4\n"));
        CollectionAssert.AreEqual(
            new[] { (1, 2, 3, 4) },
            t.Frames(t.Sprites[0]).Select(f => (f.U, f.V, f.W, f.H)).ToArray()
        );
    }

    [TestMethod]
    public void AnimationDefinedInAnIncludedTableServesAnEarlierSprite()
    {
        var t = Read(
            "m.spr",
            ("m.spr", Head + "@include a.spr\ns t 0 0 8 8 @anim=r\n"),
            ("a.spr", Head + "@animation r\noffset 2 1 step\n@auto=row\n@keyframes\n")
        );
        Assert.AreEqual(2, t.Frames(t.Sprites[0]).Count);
    }

    [TestMethod]
    public void KeyframesMayFollowTheFieldLineWithoutTheDirective()
    {
        var t = Read(
            "a.spr",
            ("a.spr", Head + "@animation f\ncolour 2 1.0 linear\n0.0 (1,0,0)\n1.0 (0,0,0)\ns t 0 0 8 8\n")
        );
        Assert.AreEqual(2, t.Animations["f"].Keyframes.Count);
        Assert.AreEqual(1, t.Sprites.Count);
    }

    [TestMethod]
    public void AnimationsOfAmbientTablesServeSpritesAndSilenceTheNote()
    {
        var other = Read("o.spr", ("o.spr", Head + "@animation r\noffset 2 1 step\n@auto=row\n@keyframes\n"));
        var t = SpriteTable.Read("a.spr", n => n == "a.spr" ? Head + "s t 0 0 8 8 @anim=r\n" : null, new[] { other });
        Assert.AreEqual(2, t.Frames(t.Sprites[0]).Count);
        Assert.AreEqual(0, t.Notes.Count);
    }

    [TestMethod]
    public void UndefinedAnimationIsANoteAndTheSpriteKeepsItsRegion()
    {
        var t = Read("a.spr", ("a.spr", Head + "s t 0 0 8 8 @anim=nope\n"));
        Assert.AreEqual(1, t.Frames(t.Sprites[0]).Count);
        StringAssert.Contains(t.Notes.Single(), "nope");
        StringAssert.Contains(t.Notes.Single(), "a.spr:3");
    }

    [TestMethod]
    public void ExplicitKeyframesAreKeptAndEndAtTheNextDirective()
    {
        var t = Read(
            "a.spr",
            (
                "a.spr",
                Head + "@animation f\ncolour 2 1.0 linear\n@keyframes\n0.0\t(1,0,0)\n1.0 (0,0,0)\n\ns t 0 0 8 8\n"
            )
        );
        Assert.AreEqual(2, t.Animations["f"].Keyframes.Count);
        Assert.AreEqual(1, t.Sprites.Count);
    }

    [TestMethod]
    public void UnknownAnimationFieldOrMethodIsReported()
    {
        Assert.ThrowsExactly<InvalidDataException>(() =>
            Read("a.spr", ("a.spr", Head + "@animation f\nspin 2 1.0 linear\n"))
        );
        Assert.ThrowsExactly<InvalidDataException>(() =>
            Read("a.spr", ("a.spr", Head + "@animation f\ncolour 2 1.0 wobble\n"))
        );
    }

    [TestMethod]
    public void EmitterBlocksAndSpriteNodesAreKept()
    {
        var t = Read(
            "a.spr",
            (
                "a.spr",
                Head
                    + "@emitter rift\ntype=spray\nvelocity=12\n@end_emitter\n@sprite_node n s const (1,1) (1,1,1) billboard\nx t 0 0 1 1\n"
            )
        );
        Assert.AreEqual("rift", t.Emitters.Single().Name);
        CollectionAssert.AreEqual(new[] { "type=spray", "velocity=12" }, t.Emitters.Single().Body.ToArray());
        Assert.AreEqual("n", t.SpriteNodes.Single().Name);
        CollectionAssert.AreEqual(
            new[] { "s", "const", "(1,1)", "(1,1,1)", "billboard" },
            t.SpriteNodes.Single().Arguments.ToArray()
        );
        Assert.AreEqual(1, t.Sprites.Count);
    }
}
