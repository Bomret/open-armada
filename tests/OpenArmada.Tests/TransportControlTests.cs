using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Assets;
using OpenArmada.Sandbox;
using static OpenArmada.Tests.CombatFixtures;

namespace OpenArmada.Tests;

// The transport gesture: the chord, the mode, the click, and the cursor choice, over a combat world built in code. The
// transporter values are the stock ones (range 500).
[TestClass]
public class TransportControlTests
{
    readonly TempInstall temp = new();

    [TestCleanup]
    public void CleanUp() => temp.CleanUp();

    sealed record Spec(string Label, int Team, float X, float Crew = 100, float Shields = 0);

    sealed class Rig
    {
        public required CombatWorld World { get; init; }
        public required Dictionary<string, PlacedObject> Units { get; init; }
        public required TransportControl Control { get; init; }
        public List<PlacedObject> Selected { get; } = [];

        public void Select(params string[] labels)
        {
            Selected.Clear();
            Selected.AddRange(labels.Select(l => Units[l]));
        }

        public bool Transporting(string label) => World.State(Units[label])!.Transporting;
    }

    const string Input = "transport {\n\t+ keyboard\tT\n\t- keyboard \tControl\n\t- keyboard\tShift\n\t- keyboard\tAlt\n}\n";

    static readonly KeyChord T = new() { Key = "T" };

    Rig Build(bool bound, params Spec[] specs)
    {
        var units = specs.ToDictionary(s => s.Label, s => Placed(s.Label, s.Team, s.X));
        var world = new CombatWorld(
            new UnitPoses(units.Values),
            specs.Select(s => new CombatUnit
            {
                Unit = units[s.Label],
                Profile = Profile(maxHealth: s.Shields, crew: s.Crew),
                DefinitionName = s.Label + ".odf",
                Radius = 0,
                Mobile = false,
            }),
            1,
            () => 0,
            (_, _, pose) => pose.Position
        );
        var bindings = AlertBindings.Read(
            new GameFileSystem(temp.MakeInstall(bound ? [("Input.map", Input)] : [("other.txt", "x")])),
            _ => { }
        );
        Rig? rig = null;
        var control = new TransportControl(world, bindings, () => rig!.Selected, u => u.Position);
        rig = new Rig
        {
            World = world,
            Units = units,
            Control = control,
        };

        return rig;
    }

    Rig Standard(bool bound = true) =>
        Build(
            bound,
            new Spec("a", 1, 0),
            new Spec("b", 1, 10),
            new Spec("enemy", 2, 100),
            new Spec("full", 1, 50),
            new Spec("far", 2, 2000),
            new Spec("shielded", 2, 120, Shields: 50)
        );

    [TestMethod]
    public void TheChordWithUnitsSelectedEntersTheModeAndTheClickOrdersEachUnitThatTakesIt()
    {
        var r = Standard();
        r.Select("a", "b");
        Assert.IsTrue(r.Control.Pressed(T, false));
        Assert.IsTrue(r.Control.Pending);
        Assert.IsTrue(r.Control.Click(r.Units["enemy"]));
        Assert.IsFalse(r.Control.Pending);
        Assert.IsTrue(r.Transporting("a"));
        Assert.IsTrue(r.Transporting("b"));
        CollectionAssert.AreEqual(new[] { r.Units["a"], r.Units["b"] }, r.Selected);
    }

    [TestMethod]
    public void AUnitThatCannotTakeTheOrderGetsNoneWhileTheOthersDo()
    {
        var r = Build(
            true,
            new Spec("weak", 1, 0, Crew: 9),
            new Spec("strong", 1, 10),
            new Spec("enemy", 2, 100)
        );
        r.Select("weak", "strong");
        r.Control.Pressed(T, false);
        r.Control.Click(r.Units["enemy"]);
        Assert.IsFalse(r.Transporting("weak"));
        Assert.IsTrue(r.Transporting("strong"));
    }

    [TestMethod]
    public void AClickOnAnInvalidTargetEndsTheModeAndGivesNoOrder()
    {
        var r = Standard();
        r.Select("a");
        r.Control.Pressed(T, false);
        Assert.IsTrue(r.Control.Click(r.Units["full"]));
        Assert.IsFalse(r.Control.Pending);
        Assert.IsFalse(r.Transporting("a"));
    }

    [TestMethod]
    public void AClickOnEmptySpaceKeepsTheModeAndGivesNoOrder()
    {
        var r = Standard();
        r.Select("a");
        r.Control.Pressed(T, false);
        Assert.IsTrue(r.Control.Click(null));
        Assert.IsTrue(r.Control.Pending);
        Assert.IsFalse(r.Transporting("a"));
        CollectionAssert.AreEqual(new[] { r.Units["a"] }, r.Selected);
    }

    [TestMethod]
    public void AClickOutsideTheModeIsNotTakenAndGivesNoOrder()
    {
        var r = Standard();
        r.Select("a");
        Assert.IsFalse(r.Control.Click(r.Units["enemy"]));
        Assert.IsFalse(r.Transporting("a"));
    }

    [TestMethod]
    public void ASelectionChangeEndsTheMode()
    {
        var r = Standard();
        r.Select("a");
        r.Control.Pressed(T, false);
        r.Control.SelectionChanged();
        Assert.IsFalse(r.Control.Pending);
    }

    [TestMethod]
    public void ARightClickInTheModeEndsItAndIsTakenSoItGivesNoMoveOrAttackOrder()
    {
        var r = Standard();
        r.Select("a");
        r.Control.Pressed(T, false);
        Assert.IsTrue(r.Control.RightClick());
        Assert.IsFalse(r.Control.Pending);
    }

    [TestMethod]
    public void ARightClickOutsideTheModeIsLeftToTheOrders()
    {
        var r = Standard();
        r.Select("a");
        Assert.IsFalse(r.Control.RightClick());
    }

    [TestMethod]
    public void TheChordOnATransportingUnitCancelsItsTransportAndEntersNoMode()
    {
        var r = Standard();
        r.World.Transport(r.Units["a"], r.Units["enemy"]);
        r.Select("a", "b");
        Assert.IsTrue(r.Control.Pressed(T, false));
        Assert.IsFalse(r.Transporting("a"));
        Assert.IsFalse(r.Control.Pending);
    }

    [TestMethod]
    public void TheChordCancelsOnlyTheSelectedUnitsThatAreTransporting()
    {
        var r = Standard();
        r.World.Transport(r.Units["a"], r.Units["enemy"]);
        r.World.Transport(r.Units["b"], r.Units["enemy"]);
        r.Select("a");
        r.Control.Pressed(T, false);
        Assert.IsFalse(r.Transporting("a"));
        Assert.IsTrue(r.Transporting("b"));
    }

    [TestMethod]
    public void AKeyRepeatEchoDoesNothing()
    {
        var r = Standard();
        r.Select("a");
        Assert.IsFalse(r.Control.Pressed(T, true));
        Assert.IsFalse(r.Control.Pending);
        r.Control.Pressed(T, false);
        Assert.IsFalse(r.Control.Pressed(T, true));
        Assert.IsTrue(r.Control.Pending);
    }

    [TestMethod]
    public void TheChordWithNothingSelectedOrAnotherKeyOrNoBindingDoesNothing()
    {
        var r = Standard();
        Assert.IsFalse(r.Control.Pressed(T, false));
        Assert.IsFalse(r.Control.Pending);
        r.Select("a");
        Assert.IsFalse(r.Control.Pressed(new KeyChord { Key = "G" }, false));
        var unbound = Standard(bound: false);
        unbound.Select("a");
        Assert.IsFalse(unbound.Control.Pressed(T, false));
        Assert.IsFalse(unbound.Control.Pending);
    }

    [TestMethod]
    public void TheCursorIsTheDefaultOutsideTheModeWhateverIsHovered()
    {
        var r = Standard();
        r.Select("a");
        Assert.AreEqual(TransportCursor.Default, r.Control.Cursor(r.Units["enemy"]));
    }

    [TestMethod]
    public void TheCursorIsTransportOverAnAcceptedUnitAndNoTransportOverAnyOther()
    {
        var r = Standard();
        r.Select("a");
        r.Control.Pressed(T, false);
        Assert.AreEqual(TransportCursor.Transport, r.Control.Cursor(r.Units["enemy"]));
        Assert.AreEqual(TransportCursor.NoTransport, r.Control.Cursor(r.Units["full"]), "a full friendly unit");
        Assert.AreEqual(TransportCursor.NoTransport, r.Control.Cursor(r.Units["far"]), "beyond the range");
        Assert.AreEqual(TransportCursor.NoTransport, r.Control.Cursor(r.Units["shielded"]), "a hostile with shields");
        Assert.AreEqual(TransportCursor.Default, r.Control.Cursor(null), "no unit under the cursor");
    }

    [TestMethod]
    public void TheCursorIsTransportWhenAnySelectedUnitAcceptsTheHoveredUnit()
    {
        var r = Build(
            true,
            new Spec("weak", 1, 0, Crew: 9),
            new Spec("strong", 1, 10),
            new Spec("enemy", 2, 100)
        );
        r.Select("weak", "strong");
        r.Control.Pressed(T, false);
        Assert.AreEqual(TransportCursor.Transport, r.Control.Cursor(r.Units["enemy"]));
    }
}
