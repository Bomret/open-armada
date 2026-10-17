using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Assets;

namespace OpenArmada.Tests;

// Per-target values read from definitions built in code (INV-0001).
[TestClass]
public class TargetValueTests
{
    readonly TempInstall temp = new();

    [TestCleanup]
    public void CleanUp() => temp.CleanUp();

    TargetValue Read(string odf)
    {
        var files = new GameFileSystem(temp.MakeInstall(("addon/d.odf", odf)));

        return ResolvedDefinition.Resolve(files, "d.odf").GetTargetValue("damageBase");
    }

    const string Overridden = "damageBase = 30\r\n\t\"zmama.odf\" 2\r\n\t\"other.odf\" 5.5f\r\n";

    [TestMethod]
    public void AnUnnamedTargetGetsTheDefault() => Assert.AreEqual(30f, Read(Overridden).For("fente.odf"));

    [TestMethod]
    public void ANamedTargetGetsItsOverride() => Assert.AreEqual(2f, Read(Overridden).For("zmama.odf"));

    [TestMethod]
    public void ANameDifferingOnlyInCaseGetsTheDefault() => Assert.AreEqual(30f, Read(Overridden).For("ZMAMA.odf"));

    [TestMethod]
    public void APlainValueHasNoOverrides()
    {
        var v = Read("damageBase = 30");
        Assert.AreEqual(30f, v.Default);
        Assert.AreEqual(0, v.Overrides.Count);
        Assert.IsFalse(v.Malformed);
    }

    [TestMethod]
    public void ANameWithoutAValueFallsBackToTheDefaultAndIsAProblem()
    {
        var v = Read("damageBase = 30\r\n\t\"zmama.odf\" 2\r\n\t\"lonely.odf\"\r\n");
        Assert.AreEqual(30f, v.For("zmama.odf"));
        Assert.IsTrue(v.Malformed);
    }

    [TestMethod]
    public void AValueThatIsNoNumberFallsBackToTheDefaultAndIsAProblem()
    {
        var v = Read("damageBase = 30\r\n\t\"zmama.odf\" lots\r\n");
        Assert.AreEqual(30f, v.For("zmama.odf"));
        Assert.IsTrue(v.Malformed);
    }

    [TestMethod]
    public void ADefaultThatIsNoNumberThrows() =>
        Assert.ThrowsExactly<DefinitionException>(() => Read("damageBase = lots"));
}
