using System;
using System.Linq;
using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Sandbox;

namespace OpenArmada.Tests;

// The group formation of a move click, in the game's frame (the frame of the layout files): the ships' positions in
// selection order and the click give each ship its destination. Spacing 40 and the middle of the combat band 38.5
// unless a test says otherwise; ships stand at the middle height, so the 3D distance of the greedy assignment is the
// ground distance.
[TestClass]
public class FormationTests
{
    const float K = 40;
    const float Mid = 38.5f;

    static Vector3 Ship(float x, float z) => new(x, Mid, z);

    static Vector2[] Layout(Vector2 click, float spacing, params Vector3[] ships) =>
        Formation.Destinations(ships, click, spacing, Mid, Formation.Heading(ships, click)).ToArray();

    static Vector2 At(Vector2 click, float cols, float rows, float spacing = K) =>
        click + new Vector2(cols * spacing, rows * spacing);

    [TestMethod]
    public void OneShipGoesToTheClickItself()
    {
        var click = new Vector2(500, 700);
        CollectionAssert.AreEqual(new[] { click }, Layout(click, K, Ship(10, 20)));
    }

    [TestMethod]
    public void TwoShipsSideBySideTakeTheTwoCellsOfTheTopRowAndTheOddSideIsOffCentreByHalfACell()
    {
        var click = new Vector2(500, 700);
        var d = Layout(click, K, Ship(0, 0), Ship(100, 0));
        // side 2: the origin is the cell at index 1, so the block spans one cell left of the click and none right
        CollectionAssert.AreEqual(new[] { At(click, -1, 0), At(click, 0, 0) }, d);
    }

    [TestMethod]
    public void TheHeadingOfNoShipsIsZero()
    {
        Assert.AreEqual(0.0, Formation.Heading(Array.Empty<Vector3>(), new Vector2(500, 700)));
    }

    [TestMethod]
    public void ThreeShipsLeaveTheLastCellOfTheBottomRowEmpty()
    {
        var click = new Vector2(500, 700);
        var d = Layout(click, K, Ship(0, 0), Ship(100, 0), Ship(0, -100));
        CollectionAssert.AreEquivalent(new[] { At(click, -1, 0), At(click, 0, 0), At(click, -1, -1) }, d);
    }

    [TestMethod]
    public void FourShipsFillATwoByTwoBlockAndEachTakesTheCellNearestItsPlaceInTheLine()
    {
        var click = new Vector2(500, 700);
        var d = Layout(click, K, Ship(0, 0), Ship(10, 0), Ship(20, 0), Ship(30, 0));
        // cells in turn: top-left takes the leftmost ship, top-right the rightmost, then the two between along the bottom
        CollectionAssert.AreEqual(new[] { At(click, -1, 0), At(click, -1, -1), At(click, 0, -1), At(click, 0, 0) }, d);
    }

    [TestMethod]
    public void FiveShipsFillTheFirstFiveCellsOfAThreeByThreeBlockCentredOnTheClick()
    {
        var click = new Vector2(500, 700);
        var d = Layout(click, K, Ship(0, 0), Ship(10, 0), Ship(20, 0), Ship(30, 0), Ship(40, 0));
        CollectionAssert.AreEquivalent(
            new[] { At(click, -1, 1), At(click, 0, 1), At(click, 1, 1), At(click, -1, 0), At(click, 0, 0) },
            d
        );
    }

    [TestMethod]
    public void CellsAreOneFormationSpacingApart()
    {
        var click = new Vector2(500, 700);
        var d = Layout(click, 100, Ship(0, 0), Ship(10, 0));
        CollectionAssert.AreEqual(new[] { At(click, -1, 0, 100), At(click, 0, 0, 100) }, d);
    }

    [TestMethod]
    public void AShipOnTheLeftOfTheGroupEndsInALeftCell()
    {
        var click = new Vector2(500, 700);
        var d = Layout(click, K, Ship(300, 0), Ship(0, 0));
        Assert.IsTrue(d[1].X < d[0].X);
    }

    [TestMethod]
    public void AShipsNearestCellTieGoesToTheEarlierShipInSelectionOrder()
    {
        var click = new Vector2(500, 700);
        var d = Layout(click, K, Ship(0, 0), Ship(0, 0));
        CollectionAssert.AreEqual(new[] { At(click, -1, 0), At(click, 0, 0) }, d);
    }

    [TestMethod]
    public void ASelectionSpreadOver800UnitsFormsTwoGroupsTheSecondBesideTheFirstAlongTheHeading()
    {
        // bearings to the click (1000, 0) are 0 and pi: the mean heading is pi/2, so the second group's origin is one
        // formation spacing (the first group's side is 1) along (sin pi/2, -cos pi/2) = (1, 0)
        var click = new Vector2(1000, 0);
        var d = Layout(click, K, Ship(0, 0), Ship(2000, 0));
        Assert.AreEqual(click.X, d[0].X, 1e-3);
        Assert.AreEqual(click.Y, d[0].Y, 1e-3);
        Assert.AreEqual(click.X + K, d[1].X, 1e-3);
        Assert.AreEqual(click.Y, d[1].Y, 1e-3);
    }

    [TestMethod]
    public void TheSecondGroupIsOffsetByTheFirstGroupsWidth()
    {
        // two ships at one end (a group of side 2) and one at the other, 2000 apart: the bearings are 0, 0 and pi, so
        // the mean heading is pi/3 and the second group's origin lies 2 * K along (sin pi/3, -cos pi/3)
        var click = new Vector2(1000, 0);
        var d = Layout(click, K, Ship(0, 0), Ship(0, 0), Ship(2000, 0));
        Assert.AreEqual(click.X + 2 * K * MathF.Sin(MathF.PI / 3), d[2].X, 1e-2);
        Assert.AreEqual(click.Y - 2 * K * MathF.Cos(MathF.PI / 3), d[2].Y, 1e-2);
    }

    [TestMethod]
    public void AShipJoinsTheNearestGroupUnder800AndStartsANewOneAtOrOver()
    {
        var click = new Vector2(1000, 0);
        var near = Layout(click, K, Ship(0, 0), Ship(799, 0)); // one group of two
        Assert.AreEqual(near[0].Y, near[1].Y, 1e-3);
        Assert.AreEqual(K, near[1].X - near[0].X, 1e-3);
        var far = Layout(click, K, Ship(0, 0), Ship(800, 0)); // two groups of one
        Assert.AreEqual(click.X, far[0].X, 1e-3);
        Assert.AreNotEqual(near[1].X - near[0].X, far[1].X - far[0].X);
    }

    [TestMethod]
    public void AShipJoinsTheNearerOfTwoGroupsItIsWithin800Of()
    {
        // the third ship stands 760 from the first and 740 from the second ship, which are 1500 apart: it joins the
        // second group (a block of 2), leaving the first ship alone on the click
        var click = new Vector2(1000, 0);
        var d = Layout(click, K, Ship(0, 0), Ship(1500, 0), Ship(760, 0));
        Assert.AreEqual(click, d[0]);
        Assert.AreEqual(K, Vector2.Distance(d[1], d[2]), 1e-3);
    }

    [TestMethod]
    public void ACellTakesTheShipNearestInThreeDimensionsAtTheMiddleOfTheCombatBand()
    {
        // the anchor is (0, Mid, 0), so the first cell lies at (0, Mid, K). The first ship is right under it in the
        // ground plane but 200 above the band; the second is 50 away in 3D: it takes that cell (the left one)
        var click = new Vector2(500, 700);
        var d = Layout(click, K, new Vector3(0, Mid + 200, K), new Vector3(30, Mid, 0));
        Assert.AreEqual(At(click, -1, 0), d[1]);
        Assert.AreEqual(At(click, 0, 0), d[0]);
    }

    [TestMethod]
    public void TheHeadingIsThePlainMeanOfTheBearingsWithoutWrapAround()
    {
        // the first two ships' bearings to the click are pi - 0.0997 and -(pi - 0.0997), either side of the cut: their
        // plain mean with the third's 0 is 0, so the second group lies one side (2) further along (0, -1)
        var click = new Vector2(0, 0);
        var d = Layout(click, K, Ship(1000, 100), Ship(1000, -100), Ship(-3000, 0));
        Assert.AreEqual(0, d[2].X, 1e-3);
        Assert.AreEqual(-2 * K, d[2].Y, 1e-3);
    }

    [TestMethod]
    public void TheGroupBlockIsNeverRotatedByTheHeading()
    {
        var click = new Vector2(500, 700);
        var d = Layout(click, K, Ship(0, 0), Ship(300, 0), Ship(0, 300), Ship(300, 300));
        Assert.AreEqual(2, d.Select(v => v.X).Distinct().Count());
        Assert.AreEqual(2, d.Select(v => v.Y).Distinct().Count());
    }
}
