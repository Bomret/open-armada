using System;
using System.Collections.Generic;
using System.IO;

namespace OpenArmada.Tests;

// Temp folders and synthesized installs for one test. Hold one instance per test class and call
// CleanUp from that class's [TestCleanup]; MSTest creates a fresh test-class instance per test.
internal sealed class TempInstall
{
    // Builds an install in a temp folder from "relative/path" -> content pairs. Fixtures are
    // synthesized here; no byte of a real install is used (INV-0001).
    internal string MakeInstall(params (string Path, string Content)[] files)
    {
        var root = MakeDir();
        Directory.CreateDirectory(Path.Combine(root, "SOD"));
        foreach (var (path, content) in files)
        {
            var full = Path.Combine(root, path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, content, System.Text.Encoding.Latin1); // one byte per char
        }
        return root;
    }

    // Folders this instance made.
    readonly List<string> made = new();

    // An empty temp folder, deleted by CleanUp after the test.
    internal string MakeDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "oa-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        made.Add(dir);
        return dir;
    }

    // Deletes every folder this instance made.
    internal void CleanUp()
    {
        foreach (var dir in made)
        {
            Directory.Delete(dir, true);
        }
    }
}
