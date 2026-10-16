using System.Collections.Generic;
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
    public void WholeNumberWrittenAsFloatIsAnInteger()
    {
        var def = Resolve("a.odf", ("a.odf", "a = 1.0\r\nb = 1f\r\nc = -2.0f\r\nd = 1.5"));
        Assert.AreEqual(1, def.GetInt("a"));
        Assert.AreEqual(1, def.GetInt("b"));
        Assert.AreEqual(-2, def.GetInt("c"));
        var e = Assert.ThrowsExactly<DefinitionException>(() => def.GetInt("d"));
        StringAssert.Contains(e.Message, "is not an integer: 1.5");
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
    public void UnrecognizedLinesOfAnyChainMemberAreNotedWithItsName()
    {
        var def = Resolve("b.odf", ("a.odf", "x = 1\r\ngarbage line"), ("b.odf", "#include \"a.odf\""));
        Assert.AreEqual(0, def.Problems.Count);
        Assert.AreEqual(1, def.Notes.Count);
        StringAssert.Contains(def.Notes[0], "a.odf");
        StringAssert.Contains(def.Notes[0], "line 2");
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

    [TestMethod, Timeout(10000)]
    public void DoubledIncludeChainResolvesEachDefinitionOnce()
    {
        const int depth = 40;
        var defs = Enumerable.Range(0, depth)
            .Select(i => ($"d{i}.odf", $"#include \"d{i + 1}.odf\"\r\n#include \"d{i + 1}.odf\"")).ToList();
        defs.Add(($"d{depth}.odf", "x = 1"));
        var def = Resolve("d0.odf", defs.ToArray());
        Assert.AreEqual(depth + 1, def.Chain.Count);
        Assert.AreEqual(1, def.GetInt("x"));
    }

    [TestMethod]
    public void IncludeWithUnreadableArchiveMemberIsReportedAndTheRestResolves()
    {
        var zix = "1\n-\nDIR: assets.zfs\n-\n1 a.odf\n1 b.odf\n";
        var zfs = Build(0, M("a.odf", "#include \"b.odf\"\r\nx = 1"), M("b.odf", "y = 2", 2));
        var files = new GameFileSystem(temp.MakeInstall(
            ("assets.zfs", Encoding.Latin1.GetString(zfs)), ("startrek.zix", zix)));
        var def = ResolvedDefinition.Resolve(files, "a.odf");
        Assert.AreEqual(1, def.Problems.Count);
        StringAssert.Contains(def.Problems[0], "b.odf");
        Assert.AreEqual(1, def.GetInt("x"));
    }

    [TestMethod]
    public void DefinitionFirstReachedAtTheDepthLimitStillResolvesItsIncludesViaAShallowerPath()
    {
        const int longChain = 62;
        var defs = new List<(string, string)>
        {
            ("root.odf", "#include \"short.odf\"\r\n#include \"long0.odf\""),
            ("short.odf", "#include \"mid.odf\""),
            ("mid.odf", "x = 1\r\n#include \"deep.odf\""),
            ("deep.odf", "y = 2"),
        };
        for (var i = 0; i < longChain; i++)
            defs.Add(($"long{i}.odf", $"#include \"{(i + 1 < longChain ? $"long{i + 1}" : "mid")}.odf\""));
        var def = Resolve("root.odf", defs.ToArray());
        Assert.AreEqual(2, def.GetInt("y"));
        Assert.AreEqual(1, def.Chain.Count(c => c == "mid.odf"));
    }

    [TestMethod]
    public void CycleBelowACutDefinitionIsListedOnceWhenAShallowerPathWalksItAgain()
    {
        const int longChain = 61;
        var defs = new List<(string, string)>
        {
            ("root.odf", "#include \"short.odf\"\r\n#include \"long0.odf\""),
            ("short.odf", "#include \"mid.odf\""),
            ("mid.odf", "x = 1\r\n#include \"deep.odf\""),
            ("deep.odf", "y = 2\r\n#include \"mid.odf\"\r\n#include \"deeper.odf\""),
            ("deeper.odf", "z = 3"),
        };
        for (var i = 0; i < longChain; i++)
            defs.Add(($"long{i}.odf", $"#include \"{(i + 1 < longChain ? $"long{i + 1}" : "mid")}.odf\""));
        var def = Resolve("root.odf", defs.ToArray());
        Assert.AreEqual(3, def.GetInt("z"));
        Assert.AreEqual(1, def.Problems.Count(p => p.Contains("cycle")));
    }

    // The long path enters the cycle at mid, the shallower re-walk at deep: one cycle, one line.
    [TestMethod]
    public void CycleEnteredAtTwoMembersIsListedOnce()
    {
        const int longChain = 61;
        var defs = new List<(string, string)>
        {
            ("root.odf", "#include \"short.odf\"\r\n#include \"long0.odf\""),
            ("short.odf", "#include \"deep.odf\""),
            ("mid.odf", "x = 1\r\n#include \"deep.odf\""),
            ("deep.odf", "y = 2\r\n#include \"mid.odf\"\r\n#include \"deeper.odf\""),
            ("deeper.odf", "z = 3"),
        };
        for (var i = 0; i < longChain; i++)
            defs.Add(($"long{i}.odf", $"#include \"{(i + 1 < longChain ? $"long{i + 1}" : "mid")}.odf\""));
        var def = Resolve("root.odf", defs.ToArray());
        CollectionAssert.AreEqual(new[] { "include cycle deep.odf -> mid.odf -> deep.odf" },
            def.Problems.Where(p => p.Contains("cycle")).ToArray());
    }

    // Windows has no POSIX file modes; MSTest reports the test as skipped there.
    [TestMethod, System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    public void IncludeThatExistsButCannotBeReadIsReportedAndTheRestResolves()
    {
        if (System.OperatingSystem.IsWindows()) Assert.Inconclusive("needs POSIX file modes");
        var root = temp.MakeInstall(("addon/a.odf", "#include \"x.odf\"\r\nx = 1"), ("addon/x.odf", "y = 2"));
        var locked = System.IO.Path.Combine(root, "addon", "x.odf");
        System.IO.File.SetUnixFileMode(locked, System.IO.UnixFileMode.None);
        // Root reads a file whatever its mode (container CI), so the fault cannot be staged there.
        try { System.IO.File.ReadAllBytes(locked); Assert.Inconclusive("the file stays readable (running as root)"); }
        catch (System.UnauthorizedAccessException) { }
        var def = ResolvedDefinition.Resolve(new GameFileSystem(root), "a.odf");
        Assert.AreEqual(1, def.Problems.Count);
        StringAssert.Contains(def.Problems[0], "x.odf");
        Assert.AreEqual(1, def.GetInt("x"));
    }

    [TestMethod]
    public void IndexedKeyWithOverflowingSuffixIsListedLast()
    {
        var def = Resolve("a.odf", ("a.odf", "weapon99999999999999 = c\r\nweapon2 = b\r\nweapon1 = a"));
        CollectionAssert.AreEqual(new[] { "weapon1", "weapon2", "weapon99999999999999" },
            def.Indexed("weapon").Select(v => v.Key).ToArray());
    }

    [TestMethod]
    public void DiamondIncludeResolvesTheSharedDefinitionOnce()
    {
        var def = Resolve("top.odf",
            ("top.odf", "#include \"l.odf\"\r\n#include \"r.odf\""),
            ("l.odf", "#include \"base.odf\""), ("r.odf", "#include \"base.odf\""), ("base.odf", "x = 1\r\njunk line"));
        Assert.AreEqual(1, def.Chain.Count(c => c == "base.odf"));
        Assert.AreEqual(1, def.Notes.Count);
        Assert.AreEqual(0, def.Problems.Count);
    }

    [TestMethod]
    public void NoteOfTheDefinitionItselfDoesNotRepeatItsName()
    {
        var notes = Resolve("a.odf", ("a.odf", "x = 1\r\ngarbage line")).Notes;
        Assert.AreEqual("line 2: not a key = value line: garbage line", notes[0]);
    }

    [TestMethod]
    public void NonFiniteNumberAskedAsFloatIsReportedForThatKey()
    {
        var def = Resolve("a.odf", ("a.odf", "a = NaN\r\nb = Infinity\r\nc = -Infinity\r\nd = 1e999\r\ne = 1"));
        foreach (var key in new[] { "a", "b", "c", "d" })
            StringAssert.Contains(Assert.ThrowsExactly<DefinitionException>(() => def.GetFloat(key)).Message, $"'{key}'");
        Assert.AreEqual(1f, def.GetFloat("e"));
    }

    // The engine's rule, not confirmed against the game's reader: a repeated key in one file keeps its last value.
    [TestMethod]
    public void RepeatedKeyInOneFileKeepsTheLastValue() =>
        Assert.AreEqual(2, Resolve("a.odf", ("a.odf", "x = 1\r\nx = 2")).GetInt("x"));

    // The engine's rule, not confirmed against the game's reader: includes are visited depth first, the
    // last include first, so a later include and everything it pulls in beat an earlier include's.
    [TestMethod]
    public void NestedIncludesFollowDepthFirstOrderLastIncludeFirst()
    {
        var def = Resolve("c.odf",
            ("c.odf", "#include \"a.odf\"\r\n#include \"b.odf\""),
            ("a.odf", "#include \"p.odf\"\r\nx = 1"), ("p.odf", "y = p\r\nz = p"),
            ("b.odf", "#include \"q.odf\"\r\nx = 2"), ("q.odf", "y = q"));
        Assert.AreEqual(2, def.GetInt("x"));
        Assert.AreEqual("q", def.GetString("y"));
        Assert.AreEqual("p", def.GetString("z"));
    }

    [TestMethod]
    public void IncludeNameWithoutQuotesResolves() =>
        Assert.AreEqual(1, Resolve("c.odf", ("a.odf", "x = 1"), ("c.odf", "#include a.odf")).GetInt("x"));

    [TestMethod]
    public void EveryListedDefinitionResolvesByItsListedNameAndAnAddonSubfolderFileIsNotListed()
    {
        var install = Install(new[] { ("a.odf", "x = 1") }, ("b.odf", "x = 2"), ("mods/s.odf", "x = 7"));
        var listed = DefinitionKind.Named(install, ".odf").ToArray();
        CollectionAssert.AreEquivalent(new[] { "a.odf", "b.odf" }, listed);
        foreach (var name in listed) ResolvedDefinition.Resolve(install, name);
    }

    [TestMethod]
    public void NameWithWindows1252CharacterKeepsIt()
    {
        var install = new GameFileSystem(temp.MakeInstall(("addon/a.odf", "name = \"Kahless\u0092 Ship\"")));
        Assert.AreEqual("Kahless’ Ship", ResolvedDefinition.Resolve(install, "a.odf").GetString("name"));
    }
}
