using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenArmada.Tests;

// INV-0012: once the map sandbox runs, only the map plan, the map summary, and the map previews read the team the map
// placed a unit on. Everything else reads the unit's current team from its combat state.
[TestClass]
public class PlacedTeamReadTests
{
    static readonly string[] MapFiles = ["SandboxPlan.cs", "MapSummary.cs", "MapPreview.cs", "MapView.cs"];

    // A read of the team of something that names a placed object: a unit, a placed object, or a selectable unit's object.
    static readonly Regex PlacedRead = new(@"(\b(unit|Unit|o|obj|placed|Hovered|hovered)\??|\.Object)\??\.Team\b");

    // The two reads that stay: the combat state's starting team, and the dump's team for a unit with no combat state.
    static readonly string[] Allowed = ["Team { get; set; } = unit.Team;", ": unit.Team)"];

    static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Sandbox")))
            dir = dir.Parent;

        return dir?.FullName ?? throw new InvalidOperationException("the repository root was not found");
    }

    [TestMethod]
    public void NoSandboxOrViewerCodeOutsideTheMapFilesReadsThePlacedTeam()
    {
        var files = new[] { "src/Sandbox", "src/Viewer" }
            .SelectMany(d => Directory.GetFiles(Path.Combine(Root(), d), "*.cs", SearchOption.AllDirectories))
            .Where(f => !MapFiles.Contains(Path.GetFileName(f)));
        var reads = files
            .SelectMany(f =>
                File.ReadAllLines(f)
                    .Select((line, i) => (File: Path.GetFileName(f), Line: i + 1, Text: line.Trim()))
                    .Where(l => !l.Text.StartsWith("///") && PlacedRead.IsMatch(l.Text))
                    .Where(l => !Allowed.Any(l.Text.Contains))
            )
            .Select(l => $"{l.File}:{l.Line}: {l.Text}")
            .ToList();

        Assert.AreEqual(0, reads.Count, string.Join("\n", reads));
    }
}
