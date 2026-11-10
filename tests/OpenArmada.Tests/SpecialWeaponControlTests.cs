using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Assets;
using OpenArmada.Sandbox;
using static OpenArmada.Tests.CombatFixtures;

namespace OpenArmada.Tests;

// The special-weapon gesture: the fire_special_<n> key, the click on the target, and the turning off, over a combat world
// built in code and the Input.map blocks of the reference install.
[TestClass]
public class SpecialWeaponControlTests
{
    readonly TempInstall temp = new();

    [TestCleanup]
    public void CleanUp() => temp.CleanUp();

    static string Block(int n) =>
        $"fire_special_{n} {{\n\t+ keyboard\tF{n}\n\t- keyboard \tControl\n\t- keyboard\tShift\n\t- keyboard\tAlt\n}}\n";

    static readonly string InputMap = string.Concat(Enumerable.Range(1, 6).Select(Block));

    static KeyChord F(int n) => new() { Key = $"F{n}" };

    sealed class Rig
    {
        public required CombatWorld World { get; init; }
        public required Dictionary<string, PlacedObject> Units { get; init; }
        public required SpecialWeaponControl Control { get; init; }
        public List<PlacedObject> Selected { get; } = [];

        public void Select(params string[] labels)
        {
            Selected.Clear();
            Selected.AddRange(labels.Select(l => Units[l]));
        }

        public bool Running(string label) => World.State(Units[label])!.SpecialOrder != null;
    }

    static SpecialWeaponProfile Hold() =>
        new()
        {
            Slot = 4,
            Range = 300,
            EnergyCost = 50,
            TargetRange = 300,
            LifeSpan = 30,
            BoardingSize = 20,
            BoardingInterval = 1,
            BoardingDelay = 1,
        };

    Rig Build(bool bound = true)
    {
        var cubes = new[] { ("cube", 1, 0f), ("cube2", 1, 10f) };
        var others = new[] { ("enemy", 2, 100f), ("friend", 1, 110f), ("neutral", 0, 120f), ("station", 2, 130f) };
        var units = cubes.Concat(others).ToDictionary(s => s.Item1, s => Placed(s.Item1, s.Item2, s.Item3));
        var world = new CombatWorld(
            new UnitPoses(units.Values),
            units.Select(u => new CombatUnit
            {
                Unit = u.Value,
                Profile = u.Key.StartsWith("cube")
                    ? Profile(maxSpecialEnergy: 1000) with
                    {
                        SpecialWeapons = [Hold()],
                    }
                    : Profile(),
                DefinitionName = u.Key + ".odf",
                Radius = 0,
                Mobile = false,
                Starbase = u.Key == "station",
            }),
            1,
            () => 0,
            (_, _, pose) => pose.Position
        );
        var bindings = AlertBindings.Read(
            new GameFileSystem(temp.MakeInstall(bound ? [("Input.map", InputMap)] : [("other.txt", "x")])),
            _ => { }
        );
        Rig? rig = null;
        rig = new Rig
        {
            World = world,
            Units = units,
            Control = new SpecialWeaponControl(world, bindings, () => rig!.Selected),
        };

        return rig;
    }

    [TestMethod]
    public void TheKeyThenAClickOnAnEnemyShipStartsTheBeamOfEverySelectedCubeThatHasTheWeapon()
    {
        var r = Build();
        r.Select("cube", "cube2");
        Assert.IsTrue(r.Control.Pressed(F(4), false));
        Assert.IsTrue(r.Control.Pending);
        Assert.IsFalse(r.Running("cube"));
        Assert.IsTrue(r.Control.Click(r.Units["enemy"]));
        Assert.IsFalse(r.Control.Pending);
        Assert.IsTrue(r.Running("cube"));
        Assert.IsTrue(r.Running("cube2"));
        CollectionAssert.AreEqual(new[] { r.Units["cube"], r.Units["cube2"] }, r.Selected);
    }

    [TestMethod]
    public void AKeyNoSelectedUnitHasAWeaponForDoesNothing()
    {
        var r = Build();
        r.Select("cube");
        Assert.IsFalse(r.Control.Pressed(F(3), false));
        Assert.IsFalse(r.Control.Pending);
        r.Select("enemy");
        Assert.IsFalse(r.Control.Pressed(F(4), false));
        Assert.IsFalse(r.Control.Pending);
        r.Select();
        Assert.IsFalse(r.Control.Pressed(F(4), false));
        Assert.IsFalse(r.Control.Pressed(new KeyChord { Key = "T" }, false));
    }

    [TestMethod]
    public void AClickOnAFriendlyANeutralAStationOrTheCubeItselfStartsNothingAndEndsTheMode()
    {
        foreach (var target in new[] { "friend", "neutral", "station", "cube" })
        {
            var r = Build();
            r.Select("cube");
            r.Control.Pressed(F(4), false);
            Assert.IsTrue(r.Control.Click(r.Units[target]), target);
            Assert.IsFalse(r.Control.Pending, target);
            Assert.IsFalse(r.Running("cube"), target);
        }
    }

    [TestMethod]
    public void AClickOnEmptySpaceStartsNothingAndKeepsTheModeUntilAUnitIsClicked()
    {
        var r = Build();
        r.Select("cube");
        r.Control.Pressed(F(4), false);
        Assert.IsTrue(r.Control.Click(null));
        Assert.IsTrue(r.Control.Pending);
        Assert.IsFalse(r.Running("cube"));
        r.Control.Click(r.Units["enemy"]);
        Assert.IsTrue(r.Running("cube"));
    }

    [TestMethod]
    public void TheKeyAgainTurnsARunningBeamOffAndEntersNoMode()
    {
        var r = Build();
        r.Select("cube");
        r.Control.Pressed(F(4), false);
        r.Control.Click(r.Units["enemy"]);
        Assert.IsTrue(r.Control.Pressed(F(4), false));
        Assert.IsFalse(r.Running("cube"));
        Assert.IsFalse(r.Control.Pending);
        Assert.AreEqual(950, r.World.State(r.Units["cube"])!.SpecialEnergy, 1e-9);
    }

    [TestMethod]
    public void ASelectionChangeOrARightClickEndsTheModeAndAKeyRepeatDoesNothing()
    {
        var r = Build();
        r.Select("cube");
        Assert.IsFalse(r.Control.Pressed(F(4), true));
        Assert.IsFalse(r.Control.Pending);
        r.Control.Pressed(F(4), false);
        r.Control.SelectionChanged();
        Assert.IsFalse(r.Control.Pending);
        r.Control.Pressed(F(4), false);
        Assert.IsTrue(r.Control.RightClick());
        Assert.IsFalse(r.Control.Pending);
        Assert.IsFalse(r.Control.RightClick());
        Assert.IsFalse(r.Control.Click(r.Units["enemy"]));
    }

    [TestMethod]
    public void AnUnboundKeyDoesNothing()
    {
        var r = Build(bound: false);
        r.Select("cube");
        Assert.IsFalse(r.Control.Pressed(F(4), false));
        Assert.IsFalse(r.Control.Pending);
    }
}
