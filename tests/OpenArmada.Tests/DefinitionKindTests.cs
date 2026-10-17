using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Assets;

namespace OpenArmada.Tests;

[TestClass]
public class DefinitionKindTests
{
    readonly ResolvedDefinitionTests defs = new();

    [TestCleanup]
    public void CleanUp() => defs.CleanUp();

    [TestMethod]
    public void CheckFailsForADefinitionWithAnUnreadableIncludeButNotAMissingOne()
    {
        var install = defs.Install(
            new[] { ("a.odf", "#include \"gone.odf\""), ("b.odf", "x = 1"), ("c.odf", "#include \"c.odf\"") }
        );
        Assert.AreEqual(0, new DefinitionKind().Check(install, "a.odf").Count);
        Assert.AreEqual(0, new DefinitionKind().Check(install, "b.odf").Count);
        var e = Assert.ThrowsExactly<InvalidDataException>(() => new DefinitionKind().Check(install, "c.odf"));
        StringAssert.Contains(e.Message, "cycle");
    }

    [TestMethod]
    public void CheckNotesAnUnrecognizedLineWithoutFailing()
    {
        var notes = new DefinitionKind().Check(defs.Install(new[] { ("a.odf", "x = 1\r\nwhat is this") }), "a.odf");
        Assert.AreEqual(1, notes.Count);
        StringAssert.Contains(notes[0], "line 2");
    }

    [TestMethod]
    public void PreviewMarksOwnInheritedAndOverriddenValues()
    {
        var install = defs.Install(
            new[]
            {
                ("p.odf", "speed = 5\r\nhull = 1"),
                ("c.odf", "#include \"p.odf\"\r\nname = \"K\u0092s ship\"\r\nlist = \"a b\" c"),
            },
            ("p.odf", "speed = 9")
        );
        var text = DefinitionText.Format(ResolvedDefinition.Resolve(install, "c.odf"));
        var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
        CollectionAssert.Contains(lines, "name = \"K’s ship\"    [own]");
        CollectionAssert.Contains(lines, "list = \"a b\" c    [own]");
        CollectionAssert.Contains(lines, "speed = 9    [overridden by the override layer, p.odf]");
        Assert.IsFalse(lines.Any(l => l.StartsWith("hull")), "the override replaces p.odf whole");
    }

    [TestMethod]
    public void PreviewMarksInheritedWithItsSourceAndListsProblems()
    {
        var install = defs.Install(
            new[] { ("p.odf", "speed = 5"), ("c.odf", "#include \"p.odf\"\r\n#include \"gone.odf\"") }
        );
        var text = DefinitionText.Format(ResolvedDefinition.Resolve(install, "c.odf"));
        StringAssert.Contains(text, "speed = 5    [inherited from p.odf]");
        StringAssert.Contains(text, "gone.odf");
    }

    [TestMethod]
    public void RemarksListEachProblemAndNoteTheTextShows()
    {
        var install = defs.Install(
            new[] { ("p.odf", "speed = 5\r\nwhat is this"), ("c.odf", "#include \"p.odf\"\r\n#include \"gone.odf\"") }
        );
        var def = ResolvedDefinition.Resolve(install, "c.odf");
        var remarks = DefinitionText.Remarks(def);
        Assert.AreEqual(def.Problems.Count + def.Notes.Count, remarks.Count);
        Assert.IsTrue(remarks.Count >= 2);
        Assert.IsTrue(remarks.Any(r => r.StartsWith("problem: ") && r.Contains("gone.odf")));
        foreach (var remark in remarks)
        {
            StringAssert.Contains(DefinitionText.Format(def), remark);
        }
    }
}
