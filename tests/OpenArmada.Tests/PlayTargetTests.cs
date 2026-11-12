using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Sandbox;

namespace OpenArmada.Tests;

[TestClass]
public class PlayTargetTests
{
    static readonly Vector2 Screen = new(1280, 720);

    [TestMethod]
    public void AProjectedPointInsideTheViewportIsTheTarget()
    {
        var (point, problem) = PlayTarget.Of("a", new Vector2(640, 360), false, Screen);

        Assert.AreEqual(new Vector2(640, 360), point);
        Assert.IsNull(problem);
    }

    [TestMethod]
    [DataRow(-1f, 100f)]
    [DataRow(1280f, 100f)]
    [DataRow(100f, -0.5f)]
    [DataRow(100f, 720f)]
    public void AnOffScreenPointIsAProblemNamingTheUnit(float x, float y)
    {
        var (point, problem) = PlayTarget.Of("a", new Vector2(x, y), false, Screen);

        Assert.IsNull(point);
        Assert.AreEqual("'a' is off screen", problem);
    }

    [TestMethod]
    public void APointBehindTheCameraIsAProblemWhereverItProjects()
    {
        var (point, problem) = PlayTarget.Of("a", new Vector2(640, 360), true, Screen);

        Assert.IsNull(point);
        Assert.AreEqual("'a' is behind the camera", problem);
    }

    [TestMethod]
    [DataRow(0f, 100f)]
    [DataRow(0.5f, 100f)]
    [DataRow(1279f, 100f)]
    [DataRow(100f, 0f)]
    [DataRow(100f, 719f)]
    public void AHoverOnAViewportEdgePixelIsAProblemNamingTheUnit(float x, float y)
    {
        Assert.AreEqual("'a' lies on the viewport edge", PlayTarget.HoverProblem("a", new Vector2(x, y), Screen));
    }

    [TestMethod]
    public void AHoverOffTheViewportEdgesIsNoProblem()
    {
        Assert.IsNull(PlayTarget.HoverProblem("a", new Vector2(1, 718), Screen));
    }

    [TestMethod]
    public void APickOfAnotherObjectIsAProblemNamingBoth()
    {
        Assert.IsNull(PlayTarget.PickProblem("a", "a"));
        Assert.AreEqual("the click on 'a' would pick 'b'", PlayTarget.PickProblem("a", "b"));
        Assert.AreEqual("the click on 'a' would pick no unit", PlayTarget.PickProblem("a", null));
    }

    [TestMethod]
    public void AnAttackNeedsASelectionAndAUnitHostileToEveryUnitOfIt()
    {
        Assert.IsNull(PlayTarget.AttackProblem("a", [true, true]));
        Assert.AreEqual("no unit is selected to attack 'a'", PlayTarget.AttackProblem("a", []));
        Assert.AreEqual("'a' is not hostile to every selected unit", PlayTarget.AttackProblem("a", [true, false]));
    }

    [TestMethod]
    public void ATransportNeedsASelectionWithAUnitThatTakesTheOrder()
    {
        Assert.IsNull(PlayTarget.TransportProblem("a", [true, false]));
        Assert.AreEqual("no unit is selected to transport to 'a'", PlayTarget.TransportProblem("a", []));
        Assert.AreEqual(
            "no selected unit can take a transport order on 'a'",
            PlayTarget.TransportProblem("a", [false, false])
        );
    }

    [TestMethod]
    public void ATransportStepFailsOnAnUnboundChordAndOnASelectionOfOnlyTransportingUnits()
    {
        Assert.IsNull(PlayTarget.TransportChordProblem(true));
        StringAssert.Contains(PlayTarget.TransportChordProblem(false)!, "the transport chord is unbound");
        Assert.IsNull(PlayTarget.TransportProblem("a", [true, true], [false, false]));
        Assert.IsNull(PlayTarget.TransportProblem("a", [false, true], [true, false]));
        Assert.AreEqual(
            "every selected unit is transporting: the transport chord would cancel them, not wait for 'a'",
            PlayTarget.TransportProblem("a", [false, false], [true, true])
        );
        Assert.AreEqual("no unit is selected to transport to 'a'", PlayTarget.TransportProblem("a", [], []));
    }

    [TestMethod]
    public void ATransportingSelectedUnitIsNoTakerSoAMixedSelectionWhereOnlyItCouldTakeTheOrderFails()
    {
        var idleTooSmall = CombatFixtures.State(CombatFixtures.Profile(crew: 5));
        var transporting = CombatFixtures.State(CombatFixtures.Profile(crew: 100));
        transporting.Transporting = true;
        var target = CombatFixtures.State(CombatFixtures.Profile(), team: 2);

        var (takes, flags) = PlayTarget.TransportSelection([idleTooSmall, transporting], target);

        CollectionAssert.AreEqual(new[] { false, false }, takes);
        CollectionAssert.AreEqual(new[] { false, true }, flags);
        Assert.AreEqual(
            "no selected unit can take a transport order on 'a'",
            PlayTarget.TransportProblem("a", takes, flags)
        );
    }

    [TestMethod]
    public void AnIdleUnitThatCanTransportTakesTheOrderAndAUnitTheWorldLostTakesNone()
    {
        var idle = CombatFixtures.State(CombatFixtures.Profile(crew: 100));
        var target = CombatFixtures.State(CombatFixtures.Profile(), team: 2);
        target.Shields = 0; // the order refuses a hostile whose shields are up

        var (takes, flags) = PlayTarget.TransportSelection([idle, null], target);

        CollectionAssert.AreEqual(new[] { true, false }, takes);
        CollectionAssert.AreEqual(new[] { false, false }, flags);
        CollectionAssert.AreEqual(new[] { false }, PlayTarget.TransportSelection([idle], null).Takes);
    }

    [TestMethod]
    public void ACropReachesHalfTheBoxsLongerSidePastEachEdgeRoundedOutward()
    {
        // a box 40 wide and 20 high pads by 20 on each side
        var crop = PlayTarget.CropOf(new ScreenBox(100.5f, 200.5f, 140.5f, 220.5f), Screen);

        Assert.AreEqual(new CropRect(80, 180, 81, 61), crop);
    }

    [TestMethod]
    public void ACropIsClippedToTheViewport()
    {
        var crop = PlayTarget.CropOf(new ScreenBox(-30, 700, 10, 740), Screen);

        Assert.AreEqual(new CropRect(0, 680, 31, 40), crop);
    }

    [TestMethod]
    public void ACropOfAOnePixelBoxIsOnePixel() =>
        Assert.AreEqual(new CropRect(5, 6, 1, 1), PlayTarget.CropOf(new ScreenBox(5, 6, 5, 6), Screen));

    [TestMethod]
    public void ACropOfABoxFarPastTheViewportIsTheWholeViewport() =>
        // a corner just in front of the camera projects to a coordinate no int holds
        Assert.AreEqual(new CropRect(0, 0, 1280, 720), PlayTarget.CropOf(new ScreenBox(0, 0, 1e12f, 10), Screen));

    [TestMethod]
    public void AScaledCropUpToTheImageLimitOnASideIsNoProblem()
    {
        Assert.IsNull(PlayTarget.ScaleProblem(new CropRect(0, 0, 500, 500), 32));
        Assert.IsNull(PlayTarget.ScaleProblem(new CropRect(0, 0, 1, 16384), 1));
    }

    [TestMethod]
    public void AScaledCropPastTheImageLimitOnASideIsAProblem()
    {
        Assert.AreEqual(
            "the crop of 500x400 pixels at scale 33 is 16500x13200, more than 16384 on a side",
            PlayTarget.ScaleProblem(new CropRect(0, 0, 500, 400), 33)
        );
        Assert.AreEqual(
            "the crop of 2x2 pixels at scale 2147483647 is 4294967294x4294967294, more than 16384 on a side",
            PlayTarget.ScaleProblem(new CropRect(0, 0, 2, 2), int.MaxValue)
        );
    }
}
