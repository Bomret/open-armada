using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenArmada.Tests;

/// <summary>
/// Finds the lines of one engine source that break INV-0004. A parser is a file under <c>Formats/</c>; an asset
/// kind is any file declaring <c>AssetKind</c> or a subclass of it.<c>ViewerLog</c> and <c>RepeatedFaults</c> count as
/// <c>Log</c> because they only forward to it.
/// </summary>
static class EngineLogScan
{
    // The two sinks, allowed by file and exact line: the default console sink and the Godot sink.
    static readonly (string Path, string Line)[] Sinks =
    [
        ("Diagnostics/Log.cs", "static Action<string> _sink = Console.Out.WriteLine;"),
        ("Diagnostics/Log.cs", "_sink = value ?? Console.Out.WriteLine;"),
        ("Viewer/AssetBrowserView.cs", "Log.Sink = line => GD.Print(line);"),
    ];

    static readonly Regex Output = new(
        @"\bConsole\s*\.|using\s+static\s+(System\.)?Console\b|\bGD\s*\.\s*(Print\w*|Push(Error|Warning))\b"
            + @"|using\s+static\s+Godot\.GD\b|\b(Debug|Trace)\s*\.\s*Write\w*"
            + @"|using\s+\w+\s*=\s*(System\.Console|Godot\.GD)\b"
    );

    static readonly Regex LogUse = new(
        @"\b(Log|ViewerLog)\s*\.|\bRepeatedFaults\b|using\s+static\s+[\w.]*\b(Log|ViewerLog|RepeatedFaults)\b"
    );

    // The abstract base counts too: its virtual methods run for every kind that does not override them.
    static readonly Regex KindDeclaration = new(@"\bclass\s+\w+\s*:\s*AssetKind\b|\babstract\s+class\s+AssetKind\b");

    /// <summary>One message per offending line, naming the file (relative to <c>src/</c>) and line.</summary>
    public static List<string> Violations(string path, string text)
    {
        var mustNotLog = path.StartsWith("Formats/") || KindDeclaration.IsMatch(text);
        var found = new List<string>();
        var sinksSeen = new HashSet<string>();
        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            // simplicity: only whole-line comments are skipped; a trailing comment is still scanned.
            if (line.StartsWith("//"))
                continue;
            // Each sink line is allowed once per file; a verbatim copy is flagged.
            if (Sinks.Contains((path, line)) && sinksSeen.Add(line))
                continue;
            if (Output.IsMatch(line))
                found.Add($"{path}:{i + 1}: console or Godot print call outside the sinks: {line}");
            else if (mustNotLog && LogUse.IsMatch(line))
                found.Add($"{path}:{i + 1}: parsers and asset kinds do not call Log: {line}");
        }
        return found;
    }
}

/// <summary>Enforces INV-0004 by scanning the engine sources under <c>src/</c>.</summary>
[TestClass]
public class EngineLoggingTests
{
    static string SourceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "OpenArmada.csproj")))
            dir = dir.Parent;
        Assert.IsNotNull(dir, "OpenArmada.csproj not found above the test binaries");
        return Path.Combine(dir.FullName, "src");
    }

    [TestMethod]
    public void EngineSourcesRespectTheLogInvariant()
    {
        var root = SourceRoot();
        var violations = Directory
            .EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .SelectMany(file =>
                EngineLogScan.Violations(Path.GetRelativePath(root, file).Replace('\\', '/'), File.ReadAllText(file))
            )
            .ToList();
        Assert.AreEqual(0, violations.Count, string.Join("\n", violations));
    }

    [TestMethod]
    public void GodotPrintOutsideTheSinkIsNamed()
    {
        var found = EngineLogScan.Violations("Sandbox/Foo.cs", "void F() { GD.Print(\"x\"); }");
        Assert.AreEqual(1, found.Count);
        StringAssert.Contains(found[0], "Sandbox/Foo.cs");
    }

    [TestMethod]
    [DataRow("Console.WriteLine(x);")]
    [DataRow("Console.Error.Write(x);")]
    [DataRow("var o = System.Console.Out;")]
    [DataRow("using static System.Console;")]
    [DataRow("GD.PrintErr(x);")]
    [DataRow("GD.PrintRich(x);")]
    [DataRow("GD.PrintS(x);")]
    [DataRow("GD.PrintT(x);")]
    [DataRow("GD.PrintRaw(x);")]
    [DataRow("GD.PushError(x);")]
    [DataRow("GD.PushWarning(x);")]
    [DataRow("using static Godot.GD;")]
    [DataRow("Debug.WriteLine(x);")]
    [DataRow("Trace.WriteLine(x);")]
    [DataRow("using C = System.Console;")]
    [DataRow("using G = Godot.GD;")]
    public void EverySpellingOfAnOutputCallIsCaught(string line)
    {
        Assert.AreEqual(1, EngineLogScan.Violations("Viewer/Foo.cs", line).Count, line);
    }

    [TestMethod]
    [DataRow("Formats/Odf.cs")]
    [DataRow("Assets/AssetCheck.cs")]
    public void TheSinksAreAllowedOnlyOnTheirOwnLine(string path)
    {
        Assert.AreEqual(
            0,
            EngineLogScan.Violations("Diagnostics/Log.cs", "_sink = value ?? Console.Out.WriteLine;").Count
        );
        Assert.AreEqual(
            0,
            EngineLogScan.Violations("Viewer/AssetBrowserView.cs", "        Log.Sink = line => GD.Print(line);").Count
        );
        Assert.AreEqual(1, EngineLogScan.Violations(path, "_sink = value ?? Console.Out.WriteLine;").Count);
        Assert.AreEqual(1, EngineLogScan.Violations(path, "Log.Sink = line => GD.Print(line);").Count);
        Assert.AreEqual(1, EngineLogScan.Violations("Viewer/AssetBrowserView.cs", "GD.Print(x);").Count);
    }

    [TestMethod]
    [DataRow("Diagnostics/Log.cs", "_sink = value ?? Console.Out.WriteLine;")]
    [DataRow("Viewer/AssetBrowserView.cs", "Log.Sink = line => GD.Print(line);")]
    public void ASinkLineIsAllowedOnceAFile(string path, string line)
    {
        Assert.AreEqual(0, EngineLogScan.Violations(path, line).Count);
        Assert.AreEqual(1, EngineLogScan.Violations(path, line + "\n    " + line).Count);
    }

    [TestMethod]
    [DataRow("Formats/Bzn.cs", "Log.Warn(Area.Assets, \"x\");")]
    [DataRow("Formats/Bzn.cs", "ViewerLog.Failed(x);")]
    [DataRow("Formats/Bzn.cs", "var f = new RepeatedFaults(Area.Assets);")]
    [DataRow("Formats/Bzn.cs", "using static OpenArmada.Diagnostics.Log;")]
    [DataRow("Formats/Bzn.cs", "using static OpenArmada.Diagnostics.ViewerLog;")]
    [DataRow("Assets/AssetKind.cs", "public abstract class AssetKind { void F() { Log.Warn(Area.Assets, \"x\"); } }")]
    [DataRow(
        "Assets/FooKind.cs",
        "public sealed class FooKind : AssetKind { void F() { Log.Error(Area.Assets, \"x\"); } }"
    )]
    public void LogInAParserOrAnAssetKindIsNamed(string path, string text)
    {
        var found = EngineLogScan.Violations(path, text);
        Assert.AreEqual(1, found.Count, text);
        StringAssert.Contains(found[0], path);
    }

    [TestMethod]
    public void LogOutsideParsersAndKindsAndCommentsIsAllowed()
    {
        Assert.AreEqual(0, EngineLogScan.Violations("Assets/AssetCheck.cs", "Log.Warn(Area.Check, \"x\");").Count);
        Assert.AreEqual(0, EngineLogScan.Violations("Sandbox/Foo.cs", "Log.Info(Area.Sandbox, \"x\");").Count);
        Assert.AreEqual(
            0,
            EngineLogScan.Violations("Formats/Odf.cs", "/// see <see cref=\"Log\"/> and GD.Print").Count
        );
        Assert.AreEqual(0, EngineLogScan.Violations("Formats/Odf.cs", "    // Log.Warn is not called here").Count);
    }
}
