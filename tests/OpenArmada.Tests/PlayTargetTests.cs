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
}
