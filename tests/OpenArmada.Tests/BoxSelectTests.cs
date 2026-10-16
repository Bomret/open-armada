using System.Linq;
using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Sandbox;

namespace OpenArmada.Tests;

// Box selection over projected screen points (INV-0001: points built in code).
[TestClass]
public class BoxSelectTests
{
    static readonly (string, Vector2, bool)[] Entries =
    {
        ("in", new Vector2(50, 50), true),
        ("edge", new Vector2(100, 100), true),
        ("out", new Vector2(150, 50), true),
        ("behind", new Vector2(60, 60), false),
    };

    static string[] Select(Vector2 from, Vector2 to) => BoxSelect.Inside(Entries, from, to).ToArray();

    [TestMethod]
    public void TheBoxTakesTheUnitsInsideItInFrontOfTheCamera() =>
        CollectionAssert.AreEqual(new[] { "in", "edge" }, Select(new Vector2(0, 0), new Vector2(100, 100)));

    [TestMethod]
    public void EveryDragDirectionSelectsTheSameUnits()
    {
        var expected = Select(new Vector2(0, 0), new Vector2(100, 100));
        CollectionAssert.AreEqual(expected, Select(new Vector2(100, 100), new Vector2(0, 0)));
        CollectionAssert.AreEqual(expected, Select(new Vector2(100, 0), new Vector2(0, 100)));
        CollectionAssert.AreEqual(expected, Select(new Vector2(0, 100), new Vector2(100, 0)));
    }

    [TestMethod]
    public void ADragOfTwoPixelsIsAClickAndOneOfTwentyIsNot()
    {
        Assert.IsTrue(BoxSelect.IsClick(new Vector2(10, 10), new Vector2(12, 10)));
        Assert.IsFalse(BoxSelect.IsClick(new Vector2(10, 10), new Vector2(30, 10)));
    }

    static string? Nearest(params (string, float?, bool, bool, float?)[] entries) => BoxSelect.Nearest(entries);

    [TestMethod]
    public void TheNearestBoxTheRayEntersIsTheHit() =>
        Assert.AreEqual(
            "near",
            Nearest(("far", 9f, true, false, null), ("near", 3f, false, false, null), ("miss", null, true, false, null))
        );

    [TestMethod]
    public void AUnitWhoseBoxHoldsTheCameraTakesNoClick() =>
        Assert.AreEqual("behind", Nearest(("unit", 0f, true, true, 1f), ("behind", 5f, true, false, null)));

    [TestMethod]
    public void APlanetWhoseBoxHoldsTheCameraAndWhoseSphereTheRayEntersBlocksTheUnitBehindIt() =>
        Assert.AreEqual("planet", Nearest(("planet", 0f, false, true, 2f), ("behind", 5f, true, false, null)));

    [TestMethod]
    public void AUnitBetweenTheCameraAndThePlanetSurfaceIsPicked() =>
        Assert.AreEqual("unit", Nearest(("planet", 0f, false, true, 8f), ("unit", 3f, true, false, null)));

    [TestMethod]
    public void APlanetWhoseBoxHoldsTheCameraAndWhoseSphereTheRayMissesBlocksNothing() =>
        Assert.AreEqual("behind", Nearest(("planet", 0f, false, true, null), ("behind", 5f, true, false, null)));

    [TestMethod]
    public void ASphereHoldingTheCameraHasNoEntryDistanceSoItBlocksNothing() =>
        Assert.IsNull(BoxSelect.SphereEntry(Vector3.Zero, 10f, new Vector3(1, 0, 0), Vector3.UnitX));

    [TestMethod]
    public void ASphereAheadOfTheCameraIsEnteredAtItsNearSurface() =>
        Assert.AreEqual(8f, BoxSelect.SphereEntry(new Vector3(10, 0, 0), 2f, Vector3.Zero, Vector3.UnitX));

    [TestMethod]
    public void ASphereBehindTheCameraOrOffTheRayIsNotEntered()
    {
        Assert.IsNull(BoxSelect.SphereEntry(new Vector3(-10, 0, 0), 2f, Vector3.Zero, Vector3.UnitX));
        Assert.IsNull(BoxSelect.SphereEntry(new Vector3(10, 5, 0), 2f, Vector3.Zero, Vector3.UnitX));
    }

    [TestMethod]
    public void ADragOfThreePixelsIsAClickAndOneOfFourIsABox()
    {
        Assert.IsTrue(BoxSelect.IsClick(new Vector2(10, 10), new Vector2(13, 13)));
        Assert.IsFalse(BoxSelect.IsClick(new Vector2(10, 10), new Vector2(14, 10)));
        Assert.IsFalse(BoxSelect.IsClick(new Vector2(10, 10), new Vector2(10, 6)));
    }
}
