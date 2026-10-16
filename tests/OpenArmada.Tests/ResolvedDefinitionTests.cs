using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Assets;
using static OpenArmada.Tests.ZfsBuilder;

namespace OpenArmada.Tests;

[TestClass]
public class ResolvedDefinitionTests
{
    readonly TempInstall temp = new();

    [TestCleanup]
    public void CleanUp() => temp.CleanUp();

    // An install whose assets.zfs holds the definitions; `addon` files are loose in the override layer.
    internal GameFileSystem Install((string Name, string Text)[] archive, params (string Name, string Text)[] addon)
    {
        var zix = "1\n-\nDIR: assets.zfs\n-\n" + string.Concat(archive.Select(d => "1 " + d.Name + "\n"));
        var zfs = Build(0, archive.Select(d => M(d.Name, d.Text)).ToArray());
        return new GameFileSystem(temp.MakeInstall(
            new[] { ("assets.zfs", Encoding.Latin1.GetString(zfs)), ("startrek.zix", zix) }
                .Concat(addon.Select(a => ("addon/" + a.Name, a.Text))).ToArray()));
    }

    ResolvedDefinition Resolve(string name, params (string Name, string Text)[] archive) =>
        ResolvedDefinition.Resolve(Install(archive), name);

    [TestMethod]
    public void ThreeLevelChainChildWinsAndMarksOrigin()
    {
        var def = Resolve("c.odf",
            ("a.odf", "hull = 1\r\nspeed = 5\r\nname = \"A\""),
            ("b.odf", "#include \"a.odf\"\r\nspeed = 6\r\nname = \"B\""),
            ("c.odf", "#include \"b.odf\"\r\nname = \"C\""));
        Assert.AreEqual("C", def.GetString("name"));
        Assert.AreEqual(6, def.GetInt("speed"));
        Assert.AreEqual(1, def.GetInt("hull"));
        CollectionAssert.AreEqual(new[] { "c.odf", "b.odf", "a.odf" }, def.Chain.ToArray());
        Assert.AreEqual(0, def.Problems.Count);
        Assert.AreEqual(ValueOrigin.Own, def.Find("name")!.Origin);
        Assert.AreEqual(ValueOrigin.Inherited, def.Find("speed")!.Origin);
        Assert.AreEqual("b.odf", def.Find("speed")!.Source);
        Assert.AreEqual("a.odf", def.Find("hull")!.Source);
    }

    [TestMethod]
    public void LaterIncludeBeatsEarlierOne()
    {
        var def = Resolve("c.odf", ("a.odf", "x = 1"), ("b.odf", "x = 2"), ("c.odf", "#include \"a.odf\"\r\n#include \"b.odf\""));
        Assert.AreEqual(2, def.GetInt("x"));
    }

    [TestMethod]
    public void KeysMatchRegardlessOfCase()
    {
        var def = Resolve("a.odf", ("a.odf", "ShotDelay = 2.0f"));
        Assert.AreEqual(2.0f, def.GetFloat("shotdelay"));
        Assert.AreEqual(2.0f, def.GetFloat("SHOTDELAY"));
    }

    [TestMethod]
    public void IncludeNameMatchesRegardlessOfCase() =>
        Assert.AreEqual(1, Resolve("c.odf", ("a.odf", "x = 1"), ("c.odf", "#include \"A.ODF\"")).GetInt("x"));

    [TestMethod]
    public void MissingKeyIsReportedForThatKey()
    {
        var e = Assert.ThrowsExactly<DefinitionException>(() => Resolve("a.odf", ("a.odf", "x = 1")).GetInt("y"));
        StringAssert.Contains(e.Message, "a.odf");
        StringAssert.Contains(e.Message, "'y'");
    }

    [TestMethod]
    public void NonNumericValueAskedAsNumberIsReportedForThatKey()
    {
        var def = Resolve("a.odf", ("a.odf", "x = fast\r\ny = 3"));
        var e = Assert.ThrowsExactly<DefinitionException>(() => def.GetFloat("x"));
        StringAssert.Contains(e.Message, "'x'");
        Assert.ThrowsExactly<DefinitionException>(() => def.GetInt("x"));
        Assert.AreEqual(3, def.GetInt("y"));
    }

    [TestMethod]
    public void FractionAskedAsIntegerAndManyTokensAskedAsStringAreWrongType()
    {
        var def = Resolve("a.odf", ("a.odf", "x = 2.5\r\nl = 1 2"));
        Assert.ThrowsExactly<DefinitionException>(() => def.GetInt("x"));
        Assert.ThrowsExactly<DefinitionException>(() => def.GetString("l"));
    }

    [TestMethod]
    public void ListsStringsAndFloatsAsTheGameWritesThem()
    {
        var def = Resolve("a.odf", ("a.odf", "c = 1.0 0.5f 0\r\nn = \"a b\" \"c\"\r\ntitle = \"Star Base\"\r\nwhen = 2.0f // s"));
        CollectionAssert.AreEqual(new[] { "1.0", "0.5f", "0" }, def.GetList("c").ToArray());
        CollectionAssert.AreEqual(new[] { "a b", "c" }, def.GetList("n").ToArray());
        Assert.AreEqual("Star Base", def.GetString("title"));
        Assert.AreEqual(2.0f, def.GetFloat("when"));
        Assert.AreEqual(0.5f, float.Parse(def.GetList("c")[1].TrimEnd('f'), System.Globalization.CultureInfo.InvariantCulture));
    }

    [TestMethod]
    public void IndexedKeysComeBackInNumericOrder()
    {
        var def = Resolve("a.odf", ("a.odf", "weapon10 = \"j\"\r\nweapon2 = \"b\"\r\nweapon1 = \"a\"\r\nweaponry = \"x\""));
        CollectionAssert.AreEqual(new[] { "a", "b", "j" }, def.Indexed("weapon").Select(v => v.Tokens[0]).ToArray());
    }

    [TestMethod]
    public void IncludeCycleIsReportedAndTerminates()
    {
        var def = Resolve("a.odf", ("a.odf", "#include \"b.odf\"\r\nx = 1"), ("b.odf", "#include \"a.odf\"\r\ny = 2"));
        Assert.AreEqual(1, def.Problems.Count);
        StringAssert.Contains(def.Problems[0], "cycle");
        Assert.AreEqual(1, def.GetInt("x"));
        Assert.AreEqual(2, def.GetInt("y"));
    }

    [TestMethod]
    public void MissingIncludeIsReportedForThatDefinitionOnly()
    {
        var install = Install(new[] { ("a.odf", "#include \"gone.odf\"\r\nx = 1"), ("b.odf", "y = 2") });
        var a = ResolvedDefinition.Resolve(install, "a.odf");
        Assert.AreEqual(1, a.Problems.Count);
        StringAssert.Contains(a.Problems[0], "gone.odf");
        Assert.AreEqual(1, a.GetInt("x"));
        Assert.AreEqual(0, ResolvedDefinition.Resolve(install, "b.odf").Problems.Count);
    }

    [TestMethod]
    public void MissingDefinitionThrowsWithItsName()
    {
        var e = Assert.ThrowsExactly<System.IO.FileNotFoundException>(() => Resolve("nope.odf", ("a.odf", "x = 1")));
        StringAssert.Contains(e.Message, "nope.odf");
    }

    [TestMethod]
    public void ParseProblemsOfAnyChainMemberAreReportedWithItsName()
    {
        var def = Resolve("b.odf", ("a.odf", "x = 1\r\ngarbage line"), ("b.odf", "#include \"a.odf\""));
        Assert.AreEqual(1, def.Problems.Count);
        StringAssert.Contains(def.Problems[0], "a.odf");
        StringAssert.Contains(def.Problems[0], "line 2");
    }

    [TestMethod]
    public void OverrideLayerValuesAreMarkedOverridden()
    {
        var install = Install(new[] { ("a.odf", "x = 1\r\ny = 1"), ("b.odf", "#include \"a.odf\"\r\nz = 1") }, ("a.odf", "x = 9"));
        var def = ResolvedDefinition.Resolve(install, "b.odf");
        Assert.AreEqual(9, def.GetInt("x"));
        Assert.AreEqual(ValueOrigin.Overridden, def.Find("x")!.Origin);
        Assert.AreEqual("a.odf", def.Find("x")!.Source);
        Assert.AreEqual(ValueOrigin.Own, def.Find("z")!.Origin);
        Assert.IsFalse(def.Has("y"), "the override file replaces the archive file whole");
    }

    [TestMethod]
    public void DeepChainTerminatesWithAProblem()
    {
        var defs = Enumerable.Range(0, 300)
            .Select(i => ($"d{i}.odf", $"#include \"d{i + 1}.odf\"")).ToArray();
        var def = Resolve("d0.odf", defs);
        Assert.IsTrue(def.Problems.Count > 0);
    }

    [TestMethod]
    public void NameWithWindows1252CharacterKeepsIt()
    {
        var install = new GameFileSystem(temp.MakeInstall(("addon/a.odf", "name = \"Kahless\u0092 Ship\"")));
        Assert.AreEqual("Kahless’ Ship", ResolvedDefinition.Resolve(install, "a.odf").GetString("name"));
    }
}
