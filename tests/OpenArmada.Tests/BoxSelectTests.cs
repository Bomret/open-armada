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

    static string? Nearest(params (string, float?, bool, bool, bool)[] entries) => BoxSelect.Nearest(entries);

    [TestMethod]
    public void TheNearestBoxTheRayEntersIsTheHit() =>
        Assert.AreEqual("near", Nearest(("far", 9f, true, false, false), ("near", 3f, false, false, false), ("miss", null, true, false, false)));

    [TestMethod]
    public void AUnitWhoseBoxHoldsTheCameraTakesNoClick() =>
        Assert.AreEqual("behind", Nearest(("unit", 0f, true, true, true), ("behind", 5f, true, false, false)));

    [TestMethod]
    public void APlanetWhoseBoxHoldsTheCameraAndWhoseShapeTheRayHitsBlocksTheUnitBehindIt() =>
        Assert.AreEqual("planet", Nearest(("planet", 0f, false, true, true), ("behind", 5f, true, false, false)));

    [TestMethod]
    public void APlanetWhoseBoxHoldsTheCameraAndWhoseShapeTheRayMissesBlocksNothing() =>
        Assert.AreEqual("behind", Nearest(("planet", 0f, false, true, false), ("behind", 5f, true, false, false)));

    [TestMethod]
    public void ADragOfThreePixelsIsAClickAndOneOfFourIsABox()
    {
        Assert.IsTrue(BoxSelect.IsClick(new Vector2(10, 10), new Vector2(13, 13)));
        Assert.IsFalse(BoxSelect.IsClick(new Vector2(10, 10), new Vector2(14, 10)));
        Assert.IsFalse(BoxSelect.IsClick(new Vector2(10, 10), new Vector2(10, 6)));
    }
}
