using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenArmada.Tests;

// tools/focused-tests.sh runs from the repository root with a fake dotnet first on PATH that writes its arguments to
// a file, so the test reads the --filter the script builds without running the suite.
// tools/focused-tests.sh is a POSIX shell script.
[TestClass]
[OSCondition(ConditionMode.Exclude, OperatingSystems.Windows)]
[System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
public sealed class FocusedTestsTests
{
    const string Unit = "TestCategory!=Install&TestCategory!=Godot";

    public static IEnumerable<object[]> TestFiles =>
        Directory
            .GetFiles(Path.Combine(CheckedInPlayTests.Root(), "tests", "OpenArmada.Tests"), "*.cs")
            .Order()
            .Select(f => new object[] { Path.GetFileName(f) });

    // The arguments dotnet received for the changed paths.
    static string[] Dotnet(params string[] paths) => Dotnet(CheckedInPlayTests.Root(), paths);

    static string[] Dotnet(string root, string[] paths)
    {
        var dir = Directory.CreateTempSubdirectory("focused-tests");
        try
        {
            var args = Path.Combine(dir.FullName, "args");
            var fake = Path.Combine(dir.FullName, "dotnet");
            File.WriteAllText(fake, "#!/bin/sh\nprintf '%s\\n' \"$@\" > \"$FAKE_ARGS\"\n");
            File.SetUnixFileMode(fake, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var info = new ProcessStartInfo("sh") { WorkingDirectory = root };
            info.ArgumentList.Add("tools/focused-tests.sh");
            foreach (var path in paths)
                info.ArgumentList.Add(path);
            info.Environment["PATH"] = $"{dir.FullName}{Path.PathSeparator}{info.Environment["PATH"]}";
            info.Environment["FAKE_ARGS"] = args;
            info.RedirectStandardOutput = true;
            using var process = Process.Start(info)!;
            process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            Assert.AreEqual(0, process.ExitCode);

            return File.Exists(args) ? File.ReadAllLines(args) : [];
        }
        finally
        {
            dir.Delete(true);
        }
    }

    static string Filter(string[] args) => args[Array.IndexOf(args, "--filter") + 1];

    // The types declared after a [TestClass] line, past any attribute lines between them.
    static string[] DeclaredClasses(string file) =>
        Regex
            .Matches(
                File.ReadAllText(Path.Combine(CheckedInPlayTests.Root(), "tests", "OpenArmada.Tests", file)),
                @"^[ \t]*\[TestClass\][ \t]*\r?\n(?:[ \t]*\[.*\]\r?\n)*[ \t]*(?:\w+[ \t]+)*?class[ \t]+(\w+)",
                RegexOptions.Multiline
            )
            .Select(m => m.Groups[1].Value)
            .ToArray();

    [TestMethod]
    [DynamicData(nameof(TestFiles))]
    public void ATestFileSelectsExactlyTheTestClassesItDeclares(string file)
    {
        var classes = DeclaredClasses(file);
        var expected =
            classes.Length == 0
                ? Unit
                : $"({string.Join("|", classes.Select(c => $"FullyQualifiedName~OpenArmada.Tests.{c}."))})&{Unit}";

        Assert.AreEqual(expected, Filter(Dotnet($"tests/OpenArmada.Tests/{file}")));
    }

    [TestMethod]
    public void AnOnlyMarkdownChangeRunsNoTest() =>
        CollectionAssert.AreEqual(Array.Empty<string>(), Dotnet("README.md"));

    [TestMethod]
    public void ANonTestSourcePathRunsEveryUnitTest() => Assert.AreEqual(Unit, Filter(Dotnet("src/Anything.cs")));

    [TestMethod]
    public void AFileWithSeveralTestClassesSelectsEach()
    {
        var dir = Directory.CreateTempSubdirectory("focused-tests-root");
        try
        {
            Directory.CreateDirectory(Path.Combine(dir.FullName, "tools"));
            Directory.CreateDirectory(Path.Combine(dir.FullName, "tests", "OpenArmada.Tests"));
            File.Copy(
                Path.Combine(CheckedInPlayTests.Root(), "tools", "focused-tests.sh"),
                Path.Combine(dir.FullName, "tools", "focused-tests.sh")
            );
            File.WriteAllText(
                Path.Combine(dir.FullName, "tests", "OpenArmada.Tests", "TwoTests.cs"),
                "// class Comment\n[TestClass]\n[Foo(\"class Quoted\")]\npublic sealed class FirstTests\n{\n}\n\n"
                    + "[TestClass]\n// class Other\npublic class SecondTests\n{\n}\n"
            );

            Assert.AreEqual(
                $"(FullyQualifiedName~OpenArmada.Tests.FirstTests.|FullyQualifiedName~OpenArmada.Tests.SecondTests.)&{Unit}",
                Filter(Dotnet(dir.FullName, ["tests/OpenArmada.Tests/TwoTests.cs"]))
            );
        }
        finally
        {
            dir.Delete(true);
        }
    }
}
