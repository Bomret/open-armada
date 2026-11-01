using System.Linq;
using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Formats;
using OpenArmada.Sandbox;

namespace OpenArmada.Tests;

// The nebula grid (0x43e880, 0x43ea20, 0x43edc0): 50-unit cells over the map bounds, a cell covered by a nebula when its
// center at y = 0 lies within the effect radius, once per nebula kind however many nebulae cover it. The map spans 0 to
// 6400 on both ground axes; the nebula positions here are in Godot's frame, whose Z is the map's negated.
[TestClass]
public class NebulaGridTests
{
    static readonly MapBounds Bounds = new()
    {
        MinX = 0,
        MaxX = 6400,
        MinY = 0,
        MaxY = 0,
        MinZ = 0,
        MaxZ = 6400,
    };

    static readonly NebulaKind[] Kinds =
    [
        new() { Kind = 1, Coefficient = 1f },
        new() { Kind = 2, Coefficient = 0.7f },
        new() { Kind = 3, Coefficient = 0.6f },
        new() { Kind = 4, Coefficient = 1f },
        new()
        {
            Kind = 5,
            Coefficient = 1f,
            Limit = 4f,
        },
    ];

    static Nebula Nebula(int kind, float x, float z, float y = 0, float radius = 100) =>
        new()
        {
            Label = "n",
            Class = $"mnebula{kind}",
            Kind = kind,
            Center = new Vector3(x, y, -z),
            EffectRadius = radius,
        };

    static NebulaGrid Grid(params Nebula[] nebulae) => new(nebulae, Kinds, Bounds);

    static Vector3 Ship(float x, float z, float y = 20) => new(x, y, -z);

    [TestMethod]
    public void AShipInsideANebulaTakesItsKindsValues()
    {
        var kinds = Grid(Nebula(3, 1000, 1000)).At(Ship(1000, 1000));
        Assert.AreEqual(0.6f, kinds.Single().Coefficient);
        Assert.AreEqual(3, kinds.Single().Kind);
    }

    [TestMethod]
    public void AShipOutsideEveryNebulaTakesNone()
    {
        Assert.AreEqual(0, Grid(Nebula(3, 1000, 1000)).At(Ship(2000, 1000)).Count);
    }

    [TestMethod]
    public void TwoNebulaeOfOneKindApplyOnce()
    {
        var kinds = Grid(Nebula(2, 1000, 1000), Nebula(2, 1030, 1000)).At(Ship(1010, 1000));
        Assert.AreEqual(1, kinds.Count);
        Assert.AreEqual(0.7f, kinds[0].Coefficient);
    }

    [TestMethod]
    public void TwoNebulaeOfTwoKindsApplyEachOnce()
    {
        var kinds = Grid(Nebula(2, 1000, 1000), Nebula(3, 1030, 1000)).At(Ship(1010, 1000));
        CollectionAssert.AreEquivalent(new[] { 2, 3 }, kinds.Select(k => k.Kind).ToArray());
    }

    [TestMethod]
    public void ANebulaAboveOrBelowThePlaneByMoreThanItsRadiusCoversNoCell()
    {
        Assert.AreEqual(0, Grid(Nebula(3, 1025, 1025, y: 100.5f)).At(Ship(1025, 1025)).Count);
        Assert.AreEqual(0, Grid(Nebula(3, 1025, 1025, y: -150)).At(Ship(1025, 1025)).Count);
    }

    [TestMethod]
    public void ANebulaExactlyItsRadiusAboveOrBelowThePlaneCoversTheCellWhoseCenterLiesUnderIt()
    {
        // the test is <= (0x43ea20): the cell centered at (1025, 1025) is at distance 100 from the nebula
        Assert.AreEqual(1, Grid(Nebula(3, 1025, 1025, y: 100)).At(Ship(1025, 1025)).Count);
        Assert.AreEqual(1, Grid(Nebula(3, 1025, 1025, y: -100)).At(Ship(1025, 1025)).Count);
        Assert.AreEqual(0, Grid(Nebula(3, 1025, 1025, y: 100)).At(Ship(1075, 1025)).Count);
    }

    [TestMethod]
    public void ANebulaOffThePlaneCoversTheCellsWithinTheCutOfItsSphere()
    {
        // 60 above the plane: the cut has radius 80, so the cell centered at 1075 (dx 75, dz 25: 75^2 + 25^2 + 60^2 = 9850) counts
        // and the cell centered at 1125 (125 away) does not
        var grid = Grid(Nebula(3, 1000, 1000, y: 60));
        Assert.AreEqual(1, grid.At(Ship(1075, 1025)).Count);
        Assert.AreEqual(0, grid.At(Ship(1125, 1025)).Count);
    }

    [TestMethod]
    public void ACellCountsByItsCenterNotByTheShipsDistance()
    {
        // the cell 22 holds x 1100 to 1149 and its center 1125 is 125 from the nebula: a ship at exactly the radius is outside;
        // the cell 21 (center 1075) is covered all the way to its edge at 1099.9
        var grid = Grid(Nebula(3, 1000, 1025));
        Assert.AreEqual(1, grid.At(Ship(1099.9f, 1025)).Count);
        Assert.AreEqual(0, grid.At(Ship(1100f, 1025)).Count);
    }

    [TestMethod]
    public void TheShipsHeightDoesNotCount()
    {
        var grid = Grid(Nebula(3, 1000, 1000));
        Assert.AreEqual(1, grid.At(Ship(1000, 1000, y: 1000)).Count);
    }

    [TestMethod]
    public void TheGridStartsAtTheMapBoundsNotAtZero()
    {
        var shifted = new MapBounds
        {
            MinX = 100,
            MaxX = 6500,
            MinY = 0,
            MaxY = 0,
            MinZ = 20,
            MaxZ = 6420,
        };
        // cells start at x 100, z 20: the cell centers lie at 125 + 50 n and 45 + 50 n, so a nebula of radius 10 at (125, 45) covers one cell
        var grid = new NebulaGrid([Nebula(3, 125, 45, radius: 10)], Kinds, shifted);
        Assert.AreEqual(1, grid.At(Ship(110, 30)).Count); // the same cell: x 100..149, z 20..69
        Assert.AreEqual(0, grid.At(Ship(160, 30)).Count);
    }

    [TestMethod]
    public void AShipOutsideTheGridTakesNoKind()
    {
        // a nebula at the map's far edge covers its in-grid cells only; a ship beyond the grid is in none
        var grid = Grid(Nebula(3, 6390, 3000));
        Assert.AreEqual(1, grid.At(Ship(6390, 3000)).Count);
        Assert.AreEqual(0, grid.At(Ship(6500, 3000)).Count);
        Assert.AreEqual(0, grid.At(Ship(-200, 3000)).Count);
    }

    [TestMethod]
    public void AShipLessThanOneCellBelowTheOriginIsInTheFirstCell()
    {
        // the conversion truncates toward zero (_ftol): x -10 is cell 0
        var grid = Grid(Nebula(3, 25, 25));
        Assert.AreEqual(1, grid.At(Ship(-10, 10)).Count);
    }

    [TestMethod]
    public void ANebulaOfAKindWithoutValuesAppliesNothing()
    {
        var grid = new NebulaGrid([Nebula(3, 1000, 1000)], [], Bounds);
        Assert.AreEqual(0, grid.At(Ship(1000, 1000)).Count);
    }
}
