using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Assets;
using OpenArmada.Sandbox;

namespace OpenArmada.Tests;

// Every checked-in play script and layout reads without a problem, so a typo shows in the unit tests and not only in a
// Movie Maker run.
[TestClass]
public class CheckedInPlayTests
{
    static string Root()
    {
        var dir = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "OpenArmada.csproj")))
            dir = dir.Parent;

        return dir?.FullName
            ?? throw new DirectoryNotFoundException("OpenArmada.csproj not found above the test binaries");
    }

    static IEnumerable<object[]> Files(string folder, string pattern) =>
        Directory
            .GetFiles(Path.Combine(Root(), folder), pattern)
            .Order()
            .Select(f => new object[] { Path.GetFileName(f) });

    public static IEnumerable<object[]> Scripts => Files("play", "*.play");

    public static IEnumerable<object[]> Layouts => Files("layouts", "*.jsonc");

    [TestMethod]
    [DynamicData(nameof(Scripts))]
    public void ACheckedInPlayScriptReadsWithoutAProblem(string name)
    {
        var script = PlayScript.Read(Path.Combine(Root(), "play", name));

        CollectionAssert.AreEqual(new string[0], script.Problems.ToArray());
        Assert.AreNotEqual(0, script.Steps.Count);
    }

    [TestMethod]
    [DynamicData(nameof(Layouts))]
    public void ACheckedInLayoutReadsWithoutAProblem(string name) =>
        Assert.IsNull(Layout.Load(Path.Combine(Root(), "layouts", name)).Problem);
}
