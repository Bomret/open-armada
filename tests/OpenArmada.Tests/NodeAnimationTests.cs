using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Formats;
using OpenArmada.Sandbox;

namespace OpenArmada.Tests;

// Node animations read from sprite tables built in code (INV-0001), evaluated over time.
[TestClass]
public class NodeAnimationTests
{
    static NodeAnimation Anim(string body)
    {
        var t = SpriteTable.Read("a.spr", n => n == "a.spr" ? "sprite_table\n@animation x\n" + body : null);
        return new NodeAnimation(t.Animations["x"]);
    }

    static void Near(Vector3 expected, Vector3 actual) =>
        Assert.IsTrue(Vector3.Distance(expected, actual) < 1e-4f, $"expected {expected}, got {actual}");

    [TestMethod]
    public void DrawStepHidesAndShowsAtItsKeyframes()
    {
        var a = Anim("draw 2 1.5 step\n@keyframes\n0.0\t1\n1.099\t0\n");
        Assert.IsTrue(a.At(0.5, loop: true).Draw);
        Assert.IsFalse(a.At(1.2, loop: true).Draw);
        Assert.IsFalse(a.At(1.4, loop: true).Draw); // between the last keyframe and the length
        Assert.AreEqual(new Vector3(1, 1, 1), a.At(0.5, loop: true).Colour);
    }

    [TestMethod]
    public void LoopingWrapsAtTheLengthAndPlayingOnceHoldsTheLastKeyframe()
    {
        var a = Anim("draw 2 1.5 step\n@keyframes\n0.0 1\n1.0 0\n");
        Assert.IsTrue(a.At(1.6, loop: true).Draw);
        Assert.IsFalse(a.At(1.6, loop: false).Draw);
        Assert.IsFalse(a.At(100, loop: false).Draw);
    }

    [TestMethod]
    public void ColourLinearInterpolatesAndHoldsAfterItsLastKeyframe()
    {
        var a = Anim("colour 2 1.5 linear\n@keyframes\n0.0 (1,1,0)\n0.75 (1,0.5,0)\n");
        Near(new Vector3(1, 0.75f, 0), a.At(0.375, loop: true).Colour);
        Near(new Vector3(1, 0.5f, 0), a.At(1.2, loop: true).Colour);
        Near(new Vector3(1, 1 - 0.5f * 0.1f / 0.75f, 0), a.At(1.6, loop: true).Colour);
        Assert.IsTrue(a.At(0.3, loop: true).Draw);
    }

    [TestMethod]
    public void ColourStepHoldsAKeyframeUntilTheNext()
    {
        var a = Anim("colour 2 2 step\n@keyframes\n0 (1,1,1)\n1 (0,0,0)\n");
        Near(new Vector3(1, 1, 1), a.At(0.9, loop: true).Colour);
        Near(new Vector3(0, 0, 0), a.At(1.1, loop: true).Colour);
    }

    [TestMethod]
    public void ZeroLengthAndAKeyframeShortfallStillYieldAValue()
    {
        var zero = Anim("colour 2 0 linear\n@keyframes\n0 (1,0,0)\n1 (0,0,1)\n");
        foreach (var loop in new[] { true, false })
        {
            var c = zero.At(5, loop).Colour;
            Assert.IsTrue(float.IsFinite(c.X) && float.IsFinite(c.Y) && float.IsFinite(c.Z));
        }
        Near(
            new Vector3(.5f, .5f, .5f),
            Anim("colour 3 2 linear\n@keyframes\n0 (.5,.5,.5)\n").At(1, loop: true).Colour
        );
        var none = Anim("draw 2 2 step\n@keyframes\n").At(1, loop: true);
        Assert.AreEqual((true, new Vector3(1, 1, 1)), (none.Draw, none.Colour));
    }

    [TestMethod]
    public void TrailingCommentOnAKeyframeLineIsIgnored()
    {
        var a = Anim("colour 2 2 step\n@keyframes\n0 (1,1,1)\n1 (.2,.2,.7) #fix this animation\n");
        Near(new Vector3(.2f, .2f, .7f), a.At(1.5, loop: true).Colour);
    }
}
