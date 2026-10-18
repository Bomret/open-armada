using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenArmada.Tests;

/// <summary>
/// Finds the lines of one preview source that write notes to the log, which breaks INV-0010: a preview returns its
/// notes, and the asset browser logs them.
/// </summary>
static class PreviewNotesScan
{
    // ViewerLog.Notes, a direct Log.Warn/Info/Error, or a using static of either class. ViewerLog.TextureFailed
    // stays allowed: a texture failure is no note.
    static readonly Regex NotesLog = new(
        @"\bViewerLog\s*\.\s*Notes\b|\bLog\s*\.\s*(?:Warn|Info|Error)\b|using\s+static\s+[\w.]*\b(?:Viewer)?Log\b"
    );

    /// <summary>One message per offending line, naming the file (relative to <c>src/</c>) and line.</summary>
    public static List<string> Violations(string path, string text)
    {
        var found = new List<string>();
        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (!line.StartsWith("//") && NotesLog.IsMatch(line))
            {
                found.Add($"{path}:{i + 1}: a preview returns its notes and never logs them: {line}");
            }
        }

        return found;
    }
}

/// <summary>Enforces the scan half of INV-0010 over the preview sources in <c>src/Viewer/</c>.</summary>
[TestClass]
public class PreviewNotesTests
{
    static string ViewerRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "OpenArmada.csproj")))
            dir = dir.Parent;
        Assert.IsNotNull(dir, "OpenArmada.csproj not found above the test binaries");

        return Path.Combine(dir.FullName, "src", "Viewer");
    }

    [TestMethod]
    public void No_preview_writes_its_notes_to_the_log()
    {
        var root = ViewerRoot();
        var sources = Directory.EnumerateFiles(root, "*Preview.cs").ToList();
        Assert.IsTrue(sources.Count >= 17, $"only {sources.Count} preview sources under {root}");

        var violations = sources
            .SelectMany(file => PreviewNotesScan.Violations("Viewer/" + Path.GetFileName(file), File.ReadAllText(file)))
            .ToList();

        Assert.AreEqual(0, violations.Count, string.Join("\n", violations));
    }

    [TestMethod]
    [DataRow("        ViewerLog.Notes(name, cursor.Notes);")]
    [DataRow("ViewerLog . Notes(name, notes);")]
    [DataRow("using static OpenArmada.Diagnostics.ViewerLog;")]
    [DataRow("        Log.Warn(Area.Browser, $\"{name}: {note}\");")]
    [DataRow("Log.Info(Area.Browser, note);")]
    [DataRow("Log . Error(Area.Browser, note);")]
    [DataRow("using static OpenArmada.Diagnostics.Log;")]
    public void A_preview_that_logs_its_notes_is_named(string line)
    {
        var found = PreviewNotesScan.Violations("Viewer/CursorPreview.cs", line);

        StringAssert.Contains(Assert.ContainsSingle(found), "Viewer/CursorPreview.cs:1:");
    }

    [TestMethod]
    public void A_texture_failure_is_no_note()
    {
        Assert.AreEqual(
            0,
            PreviewNotesScan
                .Violations("Viewer/InterfaceConfigPreview.cs", "ViewerLog.TextureFailed(Area.Browser, t, ex, asset);")
                .Count
        );
    }
}
