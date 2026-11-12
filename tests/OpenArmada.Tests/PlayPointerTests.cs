using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Sandbox;

namespace OpenArmada.Tests;

[TestClass]
public class PlayPointerTests
{
    static readonly Vector2 Screen = new(1280, 720);

    [TestMethod]
    public void ThePointerIsOutsideBeforeTheFirstMouseEvent() => Assert.IsNull(new PlayPointer().At);

    [TestMethod]
    public void AMouseEventMovesThePointerToWhereItLands()
    {
        var pointer = new PlayPointer();

        pointer.Land(new Vector2(10, 20));

        Assert.AreEqual(new Vector2(10, 20), pointer.At);
    }

    [TestMethod]
    public void ALeaveMovesThePointerOutOfTheWindow()
    {
        var pointer = new PlayPointer();
        pointer.Land(new Vector2(10, 20));

        pointer.Leave();

        Assert.IsNull(pointer.At);
    }

    [TestMethod]
    public void AHoverOnTheUnitItPicksMovesThePointerOntoIt()
    {
        var pointer = new PlayPointer();

        var problem = pointer.Hover("a", new Vector2(640, 360), Screen, _ => "a");

        Assert.AreEqual((null as string, new Vector2(640, 360) as Vector2?), (problem, pointer.At));
    }

    [TestMethod]
    public void AHoverOnAViewportEdgePixelFailsAndLeavesThePointerWhereItWas()
    {
        var pointer = new PlayPointer();
        pointer.Land(new Vector2(10, 20));

        var problem = pointer.Hover("a", new Vector2(0, 360), Screen, _ => "a");

        Assert.AreEqual(("'a' lies on the viewport edge", new Vector2(10, 20) as Vector2?), (problem, pointer.At));
    }

    [TestMethod]
    public void AHoverThatPicksAnotherUnitFailsAndLeavesThePointerWhereItWas()
    {
        var pointer = new PlayPointer();
        pointer.Land(new Vector2(10, 20));

        var problem = pointer.Hover("a", new Vector2(640, 360), Screen, _ => "b");

        Assert.AreEqual(("the click on 'a' would pick 'b'", new Vector2(10, 20) as Vector2?), (problem, pointer.At));
    }
}
