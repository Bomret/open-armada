using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Assets;
using static OpenArmada.Tests.ZfsBuilder;

namespace OpenArmada.Tests;

[TestClass]
public class ObjectiveTextKindTests
{
    readonly TempInstall temp = new();

    [TestCleanup]
    public void CleanUp() => temp.CleanUp();

    GameFileSystem Install()
    {
        var zix = "1\n-\nDIR: objtxt.zfs\n-\n1 b1ob1.txt\n1 b1ob2.txt\n";
        var zfs = Build(0, M("b1ob1.txt", "Destroy the \u0093Enterprise\u0094"), M("b1ob2.txt", "Defend"));
        return new GameFileSystem(
            temp.MakeInstall(("objtxt.zfs", Encoding.Latin1.GetString(zfs)), ("startrek.zix", zix))
        );
    }

    [TestMethod]
    public void ListsEveryObjectiveTextOfTheArchive() =>
        CollectionAssert.AreEquivalent(
            new[] { "b1ob1.txt", "b1ob2.txt" },
            new ObjectiveTextKind().Enumerate(Install()).ToArray()
        );

    [TestMethod]
    public void LoadsTextAsWindows1252() =>
        Assert.AreEqual("Destroy the “Enterprise”", new ObjectiveTextKind().Load(Install(), "B1OB1.TXT"));

    [TestMethod]
    public void IsFoundByDiscovery() => Assert.IsTrue(AssetKind.Discover().Any(k => k is ObjectiveTextKind));

    [TestMethod]
    public void EmptyWhenInstallHasNoObjectiveArchive() =>
        Assert.AreEqual(0, new ObjectiveTextKind().Enumerate(new GameFileSystem(temp.MakeInstall())).Count());
}
