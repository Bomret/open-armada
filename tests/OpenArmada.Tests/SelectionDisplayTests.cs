using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Sandbox;
using static OpenArmada.Tests.CombatFixtures;

namespace OpenArmada.Tests;

[TestClass]
public class SelectionDisplayTests
{
    // The fixture race: yellow at or below half the crew, red at or below a quarter.
    static UnitCombatState WithCrew(double crew)
    {
        var state = State(Profile(crew: 100, maximumCrew: 100));
        state.Crew = crew;
        return state;
    }

    // A box from x 100 to 149 (50 pixels wide) and y 200 to 239.
    static readonly ScreenBox Box = new(100, 200, 149, 239);

    static UnitCombatState WithShields(double shields, float maxHealth = 100)
    {
        var state = State(Profile(maxHealth: maxHealth));
        state.Shields = shields;
        return state;
    }

    static ScreenRect Rect(float x, float y, float width, float height) => new(new(x, y), new(width, height));

    [TestMethod]
    public void AtFullShieldsTheBarIsABoxWideGreenFillOverAGrayBackFivePixelsAboveTheBox()
    {
        var bar = SelectionDisplay.ShieldBarOf(Box, WithShields(100), 1)!.Value;
        Assert.AreEqual(Rect(100, 195, 50, 4), bar.Back);
        Assert.AreEqual(Rect(100, 195, 50, 4), bar.Fill);
        Assert.AreEqual(new(0, 1, 0), bar.Color);
    }

    [TestMethod]
    public void AtHalfShieldsTheFillIsHalfTheBoxAndHalfRedHalfGreen()
    {
        var bar = SelectionDisplay.ShieldBarOf(Box, WithShields(50), 1)!.Value;
        Assert.AreEqual(Rect(100, 195, 25, 4), bar.Fill);
        Assert.AreEqual(new(0.5f, 0.5f, 0), bar.Color);
    }

    [TestMethod]
    public void AtNoShieldsTheFillIsEmptyAndTheBackStays()
    {
        var bar = SelectionDisplay.ShieldBarOf(Box, WithShields(0), 1)!.Value;
        Assert.AreEqual(Rect(100, 195, 50, 4), bar.Back);
        Assert.AreEqual(0, bar.Fill.Size.X);
        Assert.AreEqual(new(1, 0, 0), bar.Color);
    }

    [TestMethod]
    public void ShieldsAboveTheMaximumGiveAFullGreenFill()
    {
        var bar = SelectionDisplay.ShieldBarOf(Box, WithShields(130), 1)!.Value;
        Assert.AreEqual(50, bar.Fill.Size.X);
        Assert.AreEqual(new(0, 1, 0), bar.Color);
    }

    [TestMethod]
    public void AUnitWithoutAShieldMaximumHasNoShieldBar()
    {
        Assert.IsNull(SelectionDisplay.ShieldBarOf(Box, WithShields(0, maxHealth: 0), 1));
        Assert.IsNull(SelectionDisplay.ShieldBarOf(Box, WithShields(0, maxHealth: -5), 1));
    }

    [TestMethod]
    public void AtDisplayScaleTwoTheBarIsTwiceAsHighAndTwiceAsFarAboveTheBox()
    {
        var bar = SelectionDisplay.ShieldBarOf(Box, WithShields(100), 2)!.Value;
        Assert.AreEqual(Rect(100, 190, 50, 8), bar.Back);
    }

    [TestMethod]
    public void TheCrewDotIsSevenPixelsSquareAtTheRightEdgeEightPixelsAboveTheBottom()
    {
        var dot = SelectionDisplay.CrewDotOf(Box, WithCrew(100), 1);
        Assert.AreEqual(Rect(149, 231, 7, 7), dot.Rect);
        Assert.AreEqual(CrewStatus.Green, dot.Status);
    }

    [TestMethod]
    public void TheCrewDotTakesTheCrewStatus()
    {
        Assert.AreEqual(CrewStatus.Yellow, SelectionDisplay.CrewDotOf(Box, WithCrew(50), 1).Status);
        Assert.AreEqual(CrewStatus.Red, SelectionDisplay.CrewDotOf(Box, WithCrew(25), 1).Status);
        Assert.AreEqual(CrewStatus.Green, SelectionDisplay.CrewDotOf(Box, WithCrew(150), 1).Status);
    }

    [TestMethod]
    public void ADerelictUnitShowsAnEmptyBarAndARedDot()
    {
        var state = State(Profile(crew: 0));
        Assert.IsTrue(state.Derelict);
        Assert.AreEqual(0, SelectionDisplay.ShieldBarOf(Box, state, 1)!.Value.Fill.Size.X);
        Assert.AreEqual(CrewStatus.Red, SelectionDisplay.CrewDotOf(Box, state, 1).Status);
    }

    [TestMethod]
    public void AtDisplayScaleTwoTheDotIsTwiceAsBigAndTwiceAsHigh()
    {
        Assert.AreEqual(Rect(149, 223, 14, 14), SelectionDisplay.CrewDotOf(Box, WithCrew(100), 2).Rect);
    }

    static readonly System.Numerics.Vector2 Screen = new(800, 600);

    [TestMethod]
    public void TheScreenBoxIsTheSmallestRectangleAroundTheProjectedCorners()
    {
        System.Numerics.Vector2?[] corners = { new(120, 210), new(100, 239), new(149, 200), new(130, 220) };
        Assert.AreEqual(Box, SelectionDisplay.BoxOf(corners, Screen));
    }

    [TestMethod]
    public void AUnitWithACornerBehindTheCameraHasNoScreenBox()
    {
        System.Numerics.Vector2?[] corners = { new(120, 210), null, new(149, 200) };
        Assert.IsNull(SelectionDisplay.BoxOf(corners, Screen));
    }

    [TestMethod]
    public void AUnitWhollyOffScreenHasNoScreenBoxAndAUnitPartlyOnScreenHasOne()
    {
        System.Numerics.Vector2?[] right = { new(810, 10), new(900, 50) };
        System.Numerics.Vector2?[] above = { new(10, -90), new(50, -1) };
        System.Numerics.Vector2?[] partly = { new(790, 10), new(900, 50) };
        Assert.IsNull(SelectionDisplay.BoxOf(right, Screen));
        Assert.IsNull(SelectionDisplay.BoxOf(above, Screen));
        Assert.IsNotNull(SelectionDisplay.BoxOf(partly, Screen));
    }

    [TestMethod]
    public void TheHoveredUnitShowsTheShieldBarWithoutTheCrewDot()
    {
        CollectionAssert.AreEqual(
            new[] { ("a", true), ("b", true), ("c", false) },
            SelectionDisplay.Shown(new[] { "a", "b" }, "c").ToArray()
        );
    }

    [TestMethod]
    public void ASelectedAndHoveredUnitIsShownOnceWithItsDot()
    {
        CollectionAssert.AreEqual(
            new[] { ("a", true), ("b", true) },
            SelectionDisplay.Shown(new[] { "a", "b" }, "b").ToArray()
        );
    }

    [TestMethod]
    public void WithoutAHoveredUnitOnlyTheSelectedAreShown()
    {
        CollectionAssert.AreEqual(new[] { ("a", true) }, SelectionDisplay.Shown(new[] { "a" }, null).ToArray());
    }

    [TestMethod]
    public void TheCursorHoversOnlyInsideTheWindowOfARunWithAMouse()
    {
        var mouse = new System.Numerics.Vector2(10, 20);
        Assert.AreEqual(
            mouse,
            SelectionDisplay.HoverPoint(mouse, mouseInWindow: true, confined: false, hasMouse: true)
        );
        Assert.IsNull(SelectionDisplay.HoverPoint(mouse, mouseInWindow: false, confined: false, hasMouse: true));
        Assert.IsNull(SelectionDisplay.HoverPoint(mouse, mouseInWindow: true, confined: false, hasMouse: false));
    }

    [TestMethod]
    public void AConfinedCursorHoversAfterTheWindowReportsTheMouseExited()
    {
        // The same rule as the edge scroll: on macOS a confined cursor on the right or bottom boundary lies
        // outside the view's tracking area, and a focused launch with the cursor outside confines it unreported.
        var mouse = new System.Numerics.Vector2(800, 300);
        Assert.AreEqual(
            mouse,
            SelectionDisplay.HoverPoint(mouse, mouseInWindow: false, confined: true, hasMouse: true)
        );
    }

    [TestMethod]
    public void TheCrewStatusIsGreenAboveTheYellowLevelYellowAtItAndRedAtTheRedLevel()
    {
        Assert.AreEqual(CrewStatus.Green, CombatRules.CrewStatusOf(WithCrew(51)));
        Assert.AreEqual(CrewStatus.Yellow, CombatRules.CrewStatusOf(WithCrew(50)));
        Assert.AreEqual(CrewStatus.Yellow, CombatRules.CrewStatusOf(WithCrew(26)));
        Assert.AreEqual(CrewStatus.Red, CombatRules.CrewStatusOf(WithCrew(25)));
        Assert.AreEqual(CrewStatus.Red, CombatRules.CrewStatusOf(WithCrew(0)));
        Assert.AreEqual(CrewStatus.Green, CombatRules.CrewStatusOf(WithCrew(150)));
    }
}
