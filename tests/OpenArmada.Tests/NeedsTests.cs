using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenArmada.Tests;

// The unit test commands (mise.toml tasks test and check, tools/focused-tests.sh) filter out exactly the categories of
// Needs; the integration command (mise.toml task test:integration) selects exactly them.
[TestClass]
public sealed class NeedsTests
{
    static string Read(string path) => File.ReadAllText(Path.Combine(CheckedInPlayTests.Root(), path));

    // The --filter of the task's section, which ends at the next line that opens a section.
    static string Filter(string task) =>
        Regex
            .Match(Read("mise.toml").Split($"[tasks.{task}]")[1].Split("\n[")[0], "--filter '([^']*)'")
            .Groups[1]
            .Value;

    static string[] Categories(string prefix) =>
        typeof(Needs).GetFields().Select(f => $"{prefix}{f.GetRawConstantValue()}").Order().ToArray();

    [TestMethod]
    [DataRow("test")]
    [DataRow("check")]
    public void AUnitTestTaskFiltersOutExactlyTheCategoriesOfNeeds(string task) =>
        CollectionAssert.AreEqual(Categories("TestCategory!="), Filter(task).Split('&').Order().ToArray());

    [TestMethod]
    public void TheFocusedCommandFiltersOutExactlyTheCategoriesOfNeeds()
    {
        var unit = Regex.Match(Read("tools/focused-tests.sh"), "unit='([^']*)'").Groups[1].Value;

        CollectionAssert.AreEqual(Categories("TestCategory!="), unit.Split('&').Order().ToArray());
    }

    [TestMethod]
    public void TheIntegrationTaskSelectsExactlyTheCategoriesOfNeeds() =>
        CollectionAssert.AreEqual(
            Categories("TestCategory="),
            Filter("\"test:integration\"").Split('|').Order().ToArray()
        );
}
