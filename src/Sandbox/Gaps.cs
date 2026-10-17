using System.Collections.Generic;
using System.Linq;
using OpenArmada.Diagnostics;

namespace OpenArmada.Sandbox;

// Gaps grouped by cause in order of first sight, each with its distinct names and its count (INV-0002: each
// cause is reported once). Each Add counts once, unless it names a subject (an object the gap touches): then
// the count is the number of distinct subjects, so several gaps of one cause on one subject count it once.
internal sealed class Gaps
{
    readonly List<string> _causes = new();
    readonly Dictionary<string, (List<string> Names, HashSet<object> Subjects, int Count)> _byCause = new();

    public void Add(string cause, string name, object? subject = null)
    {
        if (!_byCause.TryGetValue(cause, out var gap))
        {
            _causes.Add(cause);
            gap = (new List<string>(), new HashSet<object>(ReferenceEqualityComparer.Instance), 0);
        }

        if (!gap.Names.Contains(name))
        {
            gap.Names.Add(name);
        }

        if (subject == null || gap.Subjects.Add(subject))
        {
            gap.Count++;
        }

        _byCause[cause] = gap;
    }

    // Writes each report as one WARN entry of area sandbox, with the text the label shows (INV-0002), and returns
    // them. A plan that owns its reports calls this once, when it is complete.
    public static IReadOnlyList<ReportEntry> Logged(IReadOnlyList<ReportEntry> reports)
    {
        foreach (var report in reports)
        {
            Log.Warn(Area.Sandbox, report.Text);
        }

        return reports;
    }

    public IReadOnlyList<ReportEntry> Entries =>
        _causes
            .Select(c => new ReportEntry
            {
                Cause = c,
                Names = _byCause[c].Names,
                Count = _byCause[c].Count,
            })
            .ToList();
}
