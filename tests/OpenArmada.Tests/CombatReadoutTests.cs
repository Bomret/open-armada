using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Sandbox;
using static OpenArmada.Tests.CombatFixtures;

namespace OpenArmada.Tests;

[TestClass]
public class CombatReadoutTests
{
    [TestMethod]
    public void TheLineShowsTheNameShieldsCurrentAndMaximumAndCrew()
    {
        var state = State(Profile(maxHealth: 120, crew: 80));
        state.Shields = 45.6;
        state.Crew = 79.2;
        Assert.AreEqual(
            "Defiant  shields 46/120  crew 79  engines -  life support -  weapons -  shield generator -  sensors -",
            CombatReadout.Line("Defiant", state)
        );
    }

    [TestMethod]
    public void TheLineShowsEachSystemsHitPointsCurrentAndMaximumOrAbsent()
    {
        var state = State(Profile(systems: new[] { Sys("Engines", 5), Sys("Weapons", 8), Sys("Sensors", 0) }));
        state.Systems["Weapons"].HitPoints = 3.6;
        StringAssert.Contains(
            CombatReadout.Line("Defiant", state),
            "engines 5/5  life support -  weapons 3/8  shield generator -  sensors -"
        );
    }

    [TestMethod]
    public void TheLineMarksADerelictUnit()
    {
        var state = State(Profile());
        Assert.IsFalse(CombatReadout.Line("Defiant", state).Contains("derelict"));
        state.Derelict = true;
        Assert.IsTrue(CombatReadout.Line("Defiant", state).EndsWith("  derelict"));
    }

    [TestMethod]
    public void TheLineFollowsTheStateAsItChanges()
    {
        var state = State(Profile(maxHealth: 100, crew: 10));
        var before = CombatReadout.Line("Defiant", state);
        state.Shields = 0;
        state.Crew = 3;
        Assert.AreNotEqual(before, CombatReadout.Line("Defiant", state));
        StringAssert.StartsWith(CombatReadout.Line("Defiant", state), "Defiant  shields 0/100  crew 3  ");
    }
}
