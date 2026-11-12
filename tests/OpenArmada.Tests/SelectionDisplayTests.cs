using System;
using System.Collections.Generic;
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
        var dot = SelectionDisplay.CrewDotOf(Box, WithCrew(100), 1)!.Value;
        Assert.AreEqual(Rect(149, 231, 7, 7), dot.Rect);
        Assert.AreEqual(CrewStatus.Green, dot.Status);
    }

    [TestMethod]
    public void TheCrewDotTakesTheCrewStatus()
    {
        Assert.AreEqual(CrewStatus.Yellow, SelectionDisplay.CrewDotOf(Box, WithCrew(50), 1)!.Value.Status);
        Assert.AreEqual(CrewStatus.Red, SelectionDisplay.CrewDotOf(Box, WithCrew(25), 1)!.Value.Status);
        Assert.AreEqual(CrewStatus.Green, SelectionDisplay.CrewDotOf(Box, WithCrew(150), 1)!.Value.Status);
    }

    [TestMethod]
    public void ADerelictUnitShowsAnEmptyBarAndARedDot()
    {
        var state = State(Profile(crew: 0));
        Assert.IsTrue(state.Derelict);
        Assert.AreEqual(0, SelectionDisplay.ShieldBarOf(Box, state, 1)!.Value.Fill.Size.X);
        Assert.AreEqual(CrewStatus.Red, SelectionDisplay.CrewDotOf(Box, state, 1)!.Value.Status);
    }

    [TestMethod]
    public void AtDisplayScaleTwoTheDotIsTwiceAsBigAndTwiceAsHigh()
    {
        Assert.AreEqual(Rect(149, 223, 14, 14), SelectionDisplay.CrewDotOf(Box, WithCrew(100), 2)!.Value.Rect);
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

    // Two units of team 1, one of team 2, one of team 0.
    static readonly PlacedObject A1 = Placed("a1", 1, 0);
    static readonly PlacedObject B1 = Placed("b1", 1, 10);
    static readonly PlacedObject C2 = Placed("c2", 2, 20);
    static readonly PlacedObject D0 = Placed("d0", 0, 30);
    static readonly PlacedObject[] Units = { A1, B1, C2, D0 };

    // The teams the world holds: each unit's placed team until a test changes it.
    static readonly Dictionary<PlacedObject, int> Teams = Units.ToDictionary(u => u, u => u.Team);

    static int Team(PlacedObject unit) => Teams[unit];

    static HoverState Hover(PlacedObject? pick, double delta = 0.1, HoverState? from = null) =>
        (from ?? new HoverState()).Step(pick, Units, delta, Team);

    static ShownUnit[] Shown(HoverState hover, params PlacedObject[] selected) =>
        SelectionDisplay.Shown(hover, Units, selected, Team).ToArray();

    [TestMethod]
    public void AUnitWhoseTeamChangedIsAMemberOfItsNewTeam()
    {
        var mover = Placed("m1", 1, 50);
        var units = new[] { A1, C2, mover };
        int Now(PlacedObject u) => u == mover ? 2 : u.Team;
        var hover = new HoverState().Step(C2, units, 0.1, Now);
        hover = hover.Step(C2, units, 0.3, Now);
        Assert.AreEqual(2, hover.Team);
        CollectionAssert.AreEqual(
            new[] { new ShownUnit(C2, 1, false), new ShownUnit(mover, 0.3, false) },
            SelectionDisplay.Shown(hover, units, Array.Empty<PlacedObject>(), Now).ToArray()
        );
        var asMover = new HoverState().Step(mover, units, 0.1, Now);
        Assert.AreEqual(2, asMover.Team);
    }

    [TestMethod]
    public void WithNoHoveredUnitNoUnitIsShownSelectedOrNot()
    {
        var hover = new HoverState();
        Assert.IsNull(hover.Hovered);
        Assert.AreEqual(0, Shown(hover, A1, B1, C2, D0).Length);
        Assert.AreEqual(0, Shown(Hover(null), A1, B1, C2, D0).Length);
    }

    [TestMethod]
    public void TheHoveredUnitShowsItsBarAtFullOpacityFromTheFirstFrame()
    {
        var shown = Shown(Hover(C2));
        CollectionAssert.AreEqual(new[] { new ShownUnit(C2, 1, false) }, shown);
    }

    [TestMethod]
    public void TheOtherUnitsOfTheHoveredTeamShowTheirBarsAtTheDisplayFadeEachOnce()
    {
        var hover = Hover(A1, 0.3, Hover(A1));
        Assert.AreEqual(0.3, hover.Fade, 1e-9);
        CollectionAssert.AreEqual(new[] { new ShownUnit(A1, 1, false), new ShownUnit(B1, 0.3, false) }, Shown(hover));
    }

    [TestMethod]
    public void AHoveredUnitOfTeamZeroShowsOnlyItsOwnBar()
    {
        var zero = Placed("e0", 0, 40);
        var hover = new HoverState().Step(D0, new[] { A1, D0, zero }, 0.1, u => u.Team);
        CollectionAssert.AreEqual(
            new[] { new ShownUnit(D0, 1, false) },
            SelectionDisplay.Shown(hover, new[] { A1, D0, zero }, Array.Empty<PlacedObject>(), u => u.Team).ToArray()
        );
    }

    [TestMethod]
    public void TheFadeStartsAtZeroAndGrowsByEachFramesTimeUpToOne()
    {
        Assert.AreEqual(0, new HoverState().Fade);
        var hover = Hover(A1, 0.5);
        Assert.AreEqual(0, hover.Fade, "the first hover changes the team and starts the fade over");
        hover = Hover(A1, 0.25, hover);
        Assert.AreEqual(0.25, hover.Fade, 1e-9);
        hover = Hover(A1, 0.5, hover);
        Assert.AreEqual(0.75, hover.Fade, 1e-9);
        hover = Hover(A1, 0.5, hover);
        Assert.AreEqual(1, hover.Fade);
    }

    [TestMethod]
    public void TheFadeStartsOverWhenTheHoveredTeamChangesAndKeepsGrowingWithinOneTeam()
    {
        var hover = Hover(A1, 0.4, Hover(A1));
        hover = Hover(B1, 0.2, hover);
        Assert.AreEqual(0.6, hover.Fade, 1e-9);
        hover = Hover(C2, 0.2, hover);
        Assert.AreEqual(0, hover.Fade);
        Assert.AreEqual(2, hover.Team);
    }

    [TestMethod]
    public void APartAtTheFadeAppearsOnlyAboveHalfAtTwiceTheExcess()
    {
        Assert.AreEqual(0, SelectionDisplay.AlphaOf(0));
        Assert.AreEqual(0, SelectionDisplay.AlphaOf(0.5));
        Assert.AreEqual(0.5f, SelectionDisplay.AlphaOf(0.75), 1e-6);
        Assert.AreEqual(1, SelectionDisplay.AlphaOf(1));
        Assert.AreEqual(1, SelectionDisplay.AlphaOf(2));
        Assert.IsTrue(SelectionDisplay.AlphaOf(0.5001) > 0);
    }

    [TestMethod]
    public void ASelectedUnitAmongTheShownShowsItsDotAndOneOutsideShowsNothing()
    {
        var hover = Hover(A1, 0.3, Hover(A1));
        CollectionAssert.AreEqual(
            new[] { new ShownUnit(A1, 1, true), new ShownUnit(B1, 0.3, true) },
            Shown(hover, A1, B1, C2)
        );
    }

    [TestMethod]
    public void SelectedUnitsShowNothingWhileAUnitOfAnotherTeamIsHovered()
    {
        CollectionAssert.AreEqual(new[] { new ShownUnit(C2, 1, false) }, Shown(Hover(C2), A1, B1));
    }

    [TestMethod]
    public void AUnitWithAGameMaximumCrewOfZeroHasNoCrewDot()
    {
        // 0x49afe0: the dot's gate is the game's maximum crew, maximumCrew (crewCost where missing) not 0
        var state = State(Profile() with { DefinedMaximumCrew = 0 });
        Assert.IsNull(SelectionDisplay.CrewDotOf(Box, state, 1));
    }

    [TestMethod]
    public void AUnitWithAFractionalMaximumCrewHasACrewDot()
    {
        var state = State(Profile() with { DefinedMaximumCrew = 0.5f });
        Assert.IsNotNull(SelectionDisplay.CrewDotOf(Box, state, 1));
    }

    [TestMethod]
    public void AUnitWithoutAShieldMaximumHasACrewDotAndNoShieldBar()
    {
        var state = WithShields(0, maxHealth: 0);
        Assert.IsNull(SelectionDisplay.ShieldBarOf(Box, state, 1));
        Assert.IsNotNull(SelectionDisplay.CrewDotOf(Box, state, 1));
    }

    [TestMethod]
    public void TheLastHoveredUnitStaysHoveredForTheHoldAfterThePickFindsNone()
    {
        var hover = Hover(A1);
        hover = Hover(null, 0.2, hover);
        Assert.AreEqual(A1, hover.Hovered);
        hover = Hover(null, 0.1, hover);
        Assert.AreEqual(A1, hover.Hovered);
        hover = Hover(null, 0.1, hover);
        Assert.IsNull(hover.Hovered, "0.4 s have passed");
        Assert.AreEqual(0, hover.Team);
    }

    [TestMethod]
    public void APickOfAnotherUnitHoversItAtOnceAndRestartsTheHold()
    {
        var hover = Hover(null, 0.3, Hover(A1));
        hover = Hover(C2, 0.01, hover);
        Assert.AreEqual(C2, hover.Hovered);
        hover = Hover(null, 0.3, hover);
        Assert.AreEqual(C2, hover.Hovered);
    }

    [TestMethod]
    public void APickOfTheHeldUnitRestartsTheHold()
    {
        var hover = Hover(null, 0.3, Hover(A1));
        hover = Hover(A1, 0.01, hover);
        hover = Hover(null, 0.3, hover);
        Assert.AreEqual(A1, hover.Hovered);
    }

    [TestMethod]
    public void ARemovedUnitEndsTheHoverOnThatFrameAndItsTeamIsNoLongerTheKey()
    {
        var hover = Hover(A1, 0.5, Hover(A1));
        hover = hover.Step(null, new[] { B1, C2, D0 }, 0.01, Team);
        Assert.IsNull(hover.Hovered);
        Assert.AreEqual(0, hover.Team);
        Assert.AreEqual(0, hover.Fade);
        Assert.AreEqual(0, SelectionDisplay.Shown(hover, new[] { B1, C2, D0 }, new[] { B1 }, Team).Count());
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
