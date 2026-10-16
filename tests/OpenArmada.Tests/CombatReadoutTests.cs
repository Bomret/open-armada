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
        Assert.AreEqual("Defiant  shields 46/120  crew 79", CombatReadout.Line("Defiant", state));
    }

    [TestMethod]
    public void TheLineFollowsTheStateAsItChanges()
    {
        var state = State(Profile(maxHealth: 100, crew: 10));
        var before = CombatReadout.Line("Defiant", state);
        state.Shields = 0;
        state.Crew = 3;
        Assert.AreNotEqual(before, CombatReadout.Line("Defiant", state));
        Assert.AreEqual("Defiant  shields 0/100  crew 3", CombatReadout.Line("Defiant", state));
    }
}
