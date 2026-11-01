using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenArmada.Tests;

/// <summary>Keeps the namespaces free of cycles: Formats and Assets never depend on Sandbox or Viewer.</summary>
[TestClass]
public class NamespaceTests
{
    static readonly Regex UpperUsing = new(@"^\s*using\s+(?:static\s+)?OpenArmada\.(?:Sandbox|Viewer)\b");

    static string SourceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "OpenArmada.csproj")))
            dir = dir.Parent;
        Assert.IsNotNull(dir, "OpenArmada.csproj not found above the test binaries");

        return Path.Combine(dir.FullName, "src");
    }

    [TestMethod]
    [DataRow("Assets")]
    [DataRow("Formats")]
    public void NoSourceOfTheFolderUsesSandboxOrViewer(string folder)
    {
        var root = Path.Combine(SourceRoot(), folder);
        var sources = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories).ToList();
        Assert.IsTrue(sources.Count > 0, $"no sources under {root}");

        var found = sources
            .SelectMany(path =>
                File.ReadAllLines(path)
                    .Select((line, i) => (line, i))
                    .Where(l => UpperUsing.IsMatch(l.line))
                    .Select(l => $"{Path.GetRelativePath(root, path)}:{l.i + 1}: {l.line.Trim()}")
            )
            .ToList();

        Assert.AreEqual(0, found.Count, string.Join("\n", found));
    }
}
