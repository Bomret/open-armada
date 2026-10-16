using System;
using System.Linq;
using System.Numerics;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Formats;
using OpenArmada.Sandbox;

namespace OpenArmada.Tests;

// The RTS camera rig over game constants and bounds built in code (INV-0001).
[TestClass]
public class CameraRigTests
{
    // The 1.2 values of the camera constants; an override replaces the line of its name, null removes it.
    static readonly (string Name, string Value, bool Int)[] Defaults =
    {
        ("OVERVIEW_PARABOLA_FOCUS", "200.0", false), ("OVERVIEW_MAX_HEIGHT", "700.0", false),
        ("OVERVIEW_INIT_HEIGHT", "450.0", false), ("OVERVIEW_MIN_HEIGHT", "10.0", false),
        ("OVERVIEW_INIT_ROTATION", "0.0", false), ("SCROLL_BORDER_WIDTH", "2", true),
        ("SCROLL_COEFFICIENT", "90000.0", false), ("ZOOM_SCROLL_COEFFICIENT", "0.1", false),
        ("ROTATION_SCROLL_COEFFICIENT", "0.01", false), ("SCROLL_ACCELERATION", "2", true),
        ("TIME_SCALE", "1", true), ("MAX_SCROLL_SPEED", "10.0", false), ("INITIAL_SCROLL_SPEED", "0.5", false),
        ("SHIP_COMBAT_FLOOR", "10.0", false), ("SHIP_COMBAT_CEILING", "67.0", false),
    };

    static GameConstants Constants(params (string Name, string? Value)[] edits)
    {
        var text = string.Concat(Defaults.Select(d =>
        {
            var edit = edits.FirstOrDefault(e => e.Name == d.Name);
            var value = edit.Name == null ? d.Value : edit.Value;
            return value == null ? "" : $"{(d.Int ? "int" : "float")} {d.Name} = {value};\n";
        }));
        return GameConstants.Read("RTS_CFG.h", Encoding.ASCII.GetBytes(text));
    }

    static readonly MapBounds Bounds = new(0, 1000, 0, 100, 0, 600);

    static CameraRig Rig(params (string Name, string? Value)[] edits) => CameraRig.Create(Constants(edits), Bounds);

    static void Near(float expected, float actual) => Assert.AreEqual(expected, actual, 0.01f);

    [TestMethod]
    public void TheStartPoseLooksAtTheMapCenterAtTheMiddleOfTheCombatPlane()
    {
        var (eye, target) = Rig().Pose;
        Near(500, target.X);
        Near(38.5f, target.Y);
        Near(300, target.Z);
        Near(450, eye.Y);
        Near(500, eye.X);
        Near(0, eye.Z); // distance sqrt(200 * 450) = 300 behind the target, rotation 0
    }

    [TestMethod]
    public void AChangedInitHeightAndRotationChangeTheStartPose()
    {
        var (eye, target) = Rig(("OVERVIEW_INIT_HEIGHT", "200.0"), ("OVERVIEW_INIT_ROTATION", "1.5707964")).Pose;
        Near(200, eye.Y);
        var d = MathF.Sqrt(200 * 200);
        Near(500 - d, eye.X); // rotation a quarter turn: the eye lies west of the target
        Near(target.Z, eye.Z);
    }

    [TestMethod]
    public void HeightStaysWithinMinimumAndMaximum()
    {
        var rig = Rig();
        for (var i = 0; i < 50; i++) rig.ChangeHeight(0.1);
        Near(700, rig.Pose.Eye.Y);
        for (var i = 0; i < 200; i++) rig.ChangeHeight(-0.1);
        Near(10, rig.Pose.Eye.Y);
    }

    [TestMethod]
    public void RotationWrapsIntoOneTurn()
    {
        var rig = Rig();
        rig.Rotate(-0.5);
        Assert.AreEqual(2 * Math.PI - 0.5, rig.Rotation, 1e-9);
        rig.Rotate(0.5 + 2 * Math.PI * 3);
        Assert.IsTrue(rig.Rotation is >= 0 and < 2 * Math.PI);
    }

    [TestMethod]
    public void ThePanStepIsRotatedByTheRotationAndScaledByTheHeight()
    {
        var rig = Rig();
        rig.Pan(new Vector2(0, 1)); // forward at the start height: 90000 / 450 = 200
        Near(500, rig.Pose.Target.X);
        Near(500, rig.Pose.Target.Z);
        rig.Rotate(Math.PI / 2);
        rig.Pan(new Vector2(0, 1)); // forward now runs along +X
        Near(700, rig.Pose.Target.X);
    }

    [TestMethod]
    public void ThePanStepShrinksAwayFromTheInitialHeight()
    {
        var rig = Rig();
        rig.ChangeHeight(-0.5); // 225: 90000 / (225 + 450) = 133.3
        rig.Pan(new Vector2(1, 0));
        Near(500 + 90000f / 675, rig.Pose.Target.X);
    }

    [TestMethod]
    public void ThePanMovesTheEyeWithTheTarget()
    {
        var rig = Rig();
        var before = rig.Pose;
        rig.Pan(new Vector2(0.1f, 0));
        Near(before.Eye.X + 20, rig.Pose.Eye.X);
    }

    [TestMethod]
    public void TheLookAtPointNeverLeavesTheBoundsAtAnyRotation()
    {
        var rig = Rig();
        for (var turn = 0; turn < 8; turn++)
        {
            rig.Rotate(Math.PI / 4);
            for (var i = 0; i < 20; i++) rig.Pan(new Vector2(1, 1));
            var t = rig.Pose.Target;
            Assert.IsTrue(t.X >= 0 && t.X <= 1000 && t.Z >= 0 && t.Z <= 600, $"{t}");
        }
    }

    [TestMethod]
    public void EdgeScrollSpeedRampsFromInitialToMaximum()
    {
        var rig = Rig();
        var size = new Vector2(800, 600);
        var cursor = new Vector2(799, 300); // the last pixel column: strength 1, to the right
        rig.Scroll(cursor, size, true, Vector2.Zero, 0.1);
        Near(500 + 2 * 0.5f * 0.1f * 200, rig.Pose.Target.X); // acceleration 2 * speed 0.5 * dt * 200
        Assert.AreEqual(0.6, rig.ScrollSpeed, 1e-9); // grows by the time scale times dt
        for (var i = 0; i < 200; i++) rig.Scroll(cursor, size, true, Vector2.Zero, 0.1);
        Assert.AreEqual(10, rig.ScrollSpeed, 1e-9);
        rig.Scroll(new Vector2(400, 300), size, true, Vector2.Zero, 0.1);
        Assert.AreEqual(0.5, rig.ScrollSpeed, 1e-9);
    }

    [TestMethod]
    public void TheEdgeStrengthRampsOverTheBorderWidth()
    {
        var rig = Rig(("SCROLL_BORDER_WIDTH", "4"));
        var size = new Vector2(800, 600);
        rig.Scroll(new Vector2(2, 300), size, true, Vector2.Zero, 1.0); // 2 pixels in of a 4 pixel border: 0.5 to the left
        Near(500 - 100, rig.Pose.Target.X);
    }

    [TestMethod]
    public void ArrowKeysScrollTheSameWay()
    {
        var rig = Rig();
        rig.Scroll(new Vector2(400, 300), new Vector2(800, 600), true, new Vector2(0, 1), 1.0);
        Near(500, rig.Pose.Target.X);
        Near(500, rig.Pose.Target.Z);
    }

    [TestMethod]
    public void NothingScrollsWithoutFocusAndEdgeScrollNeedsTheCursorInside()
    {
        var rig = Rig();
        var size = new Vector2(800, 600);
        rig.Scroll(new Vector2(799, 300), size, false, new Vector2(1, 0), 1.0);
        Near(500, rig.Pose.Target.X);
        rig.Scroll(new Vector2(805, 300), size, true, Vector2.Zero, 1.0);
        Near(500, rig.Pose.Target.X);
        rig.Scroll(new Vector2(-1, 300), size, true, Vector2.Zero, 1.0);
        Near(500, rig.Pose.Target.X);
        rig.Scroll(new Vector2(805, 300), size, true, new Vector2(1, 0), 1.0); // keys still act with the cursor outside
        Assert.IsTrue(rig.Pose.Target.X > 500);
    }

    [TestMethod]
    public void ALackingTextualWrongTypedOrIllegalConstantIsReportedAndUsesItsStandIn()
    {
        var rig = Rig(("OVERVIEW_INIT_HEIGHT", null), ("OVERVIEW_MAX_HEIGHT", "2 * 350"), ("OVERVIEW_MIN_HEIGHT", "20"),
            ("SCROLL_COEFFICIENT", "-1.0"), ("ZOOM_SCROLL_COEFFICIENT", "0"));
        Near(450, rig.Pose.Eye.Y);
        Assert.IsTrue(rig.Problems.Any(p => p.Contains("OVERVIEW_INIT_HEIGHT") && p.Contains("missing")));
        Assert.IsTrue(rig.Problems.Any(p => p.Contains("OVERVIEW_MAX_HEIGHT")));
        Assert.IsTrue(rig.Problems.Any(p => p.Contains("SCROLL_COEFFICIENT") && p.Contains("stand-in")));
        Assert.IsTrue(rig.Problems.Any(p => p.Contains("ZOOM_SCROLL_COEFFICIENT")));
        Assert.AreEqual(0.1, rig.ZoomStep, 1e-9);
        Assert.AreEqual(700, rig.MaxHeight, 1e-9);
    }

    [TestMethod]
    public void ATextConstantUsesItsStandIn()
    {
        var text = GameConstants.Read("RTS_CFG.h", Encoding.ASCII.GetBytes("#define OVERVIEW_INIT_HEIGHT \"tall\"\n"));
        var rig = CameraRig.Create(text, Bounds);
        Near(450, rig.Pose.Eye.Y);
        Assert.IsTrue(rig.Problems.Any(p => p.Contains("OVERVIEW_INIT_HEIGHT") && p.Contains("text")));
    }

    [TestMethod]
    public void FocusZeroAndMinimumAboveMaximumGiveAFinitePose()
    {
        var rig = Rig(("OVERVIEW_PARABOLA_FOCUS", "0"), ("OVERVIEW_MIN_HEIGHT", "800"));
        var (eye, target) = rig.Pose;
        foreach (var v in new[] { eye.X, eye.Y, eye.Z, target.X, target.Y, target.Z }) Assert.IsTrue(float.IsFinite(v));
        Assert.IsTrue(rig.Problems.Any(p => p.Contains("OVERVIEW_PARABOLA_FOCUS")));
        Assert.IsTrue(rig.Problems.Any(p => p.Contains("OVERVIEW_MIN_HEIGHT")));
        Near(300, MathF.Sqrt((eye.X - target.X) * (eye.X - target.X) + (eye.Z - target.Z) * (eye.Z - target.Z)));
    }

    [TestMethod]
    public void WithoutGameConstantsEveryValueIsAStandIn()
    {
        var rig = CameraRig.Create(null, Bounds);
        Near(450, rig.Pose.Eye.Y);
        Assert.AreEqual(1, rig.Problems.Count);
    }
}
