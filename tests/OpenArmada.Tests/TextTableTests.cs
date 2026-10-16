using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Assets;
using OpenArmada.Formats;

namespace OpenArmada.Tests;

// Text tables (.ldl): a synthetic key and synthetic encoded files, built in code (INV-0001).
[TestClass]
public class TextTableTests
{
    static readonly byte[] Key = Enumerable.Range(0, TextTable.KeyLength).Select(i => (byte)(i * 7 + 3)).ToArray();

    static byte[] Encode(string plain, byte[]? key = null)
    {
        var k = key ?? Key;
        var bytes = Encoding.Latin1.GetBytes(plain);
        for (var i = 0; i < bytes.Length; i++)
            bytes[i] ^= k[i % k.Length];
        return bytes;
    }

    // Reads `text` as a file named t.ldl; `others` are the importable files.
    static TextTable Read(string text, params (string Name, string Text)[] others)
    {
        var map = others.ToDictionary(o => o.Name, o => Encode(o.Text));
        return TextTable.Read("t.ldl", Encode(text), Key, n => map.GetValueOrDefault(n));
    }

    [TestMethod]
    public void NestedBlocksHoldStringEntriesAddressedByBlockPathAndKey()
    {
        var t = Read(
            "switch {\r\n  Default {\r\n    string file \"default.ldl\"\r\n  }\r\n  string a \"b\"\r\n}\r\nplain:parent { string k \"v\" }\r\n"
        );
        CollectionAssert.AreEqual(
            new[] { "switch", "switch.Default", "plain" },
            t.Blocks.Select(b => b.Path).ToArray()
        );
        Assert.AreEqual("parent", t.Blocks[2].Parent);
        CollectionAssert.AreEqual(
            new[] { "switch.Default/file/default.ldl", "switch/a/b", "plain/k/v" },
            t.Entries.Select(e => $"{e.Block}/{e.Key}/{e.Value}").ToArray()
        );
        Assert.AreEqual(3, t.Entries[0].Line);
        Assert.AreEqual("t.ldl", t.Entries[0].File);
        Assert.AreEqual(0, t.Notes.Count);
    }

    [TestMethod]
    public void ABlockReopenedWithAnotherParentKeepsTheFirstAndNotesTheDifference()
    {
        var t = Read("b:p { string a \"1\" }\nb:q { string c \"2\" }\nb:p { }\nb { }\n");
        Assert.AreEqual("p", t.Blocks.Single().Parent);
        Assert.AreEqual(2, t.Entries.Count);
        var notes = t.Notes.Where(n => n.Contains("'b'") && n.Contains("parent")).ToList();
        Assert.AreEqual(1, notes.Count, string.Join("|", t.Notes));
        StringAssert.StartsWith(notes[0], "t.ldl:2:");
    }

    [TestMethod]
    public void AHeaderWithoutASpaceBeforeTheBraceIsANoteNotABlock()
    {
        var t = Read("b{ string a \"1\" }\n");
        Assert.AreEqual(0, t.Blocks.Count);
        Assert.IsTrue(t.Notes.Any(n => n.StartsWith("t.ldl:1:") && n.Contains("b{")), string.Join("|", t.Notes));
    }

    [TestMethod]
    public void CommentsOutsideQuotesTemplatesAndTheDollarEndAreHandled()
    {
        var t = Read(
            "# head\nb { # open\n string a \"x # not a comment\" # tail\n template\n string c \"d\"\n}\n$\nb { string never \"1\" }\n"
        );
        CollectionAssert.AreEqual(new[] { "x # not a comment", "d" }, t.Entries.Select(e => e.Value).ToArray());
        Assert.AreEqual(0, t.Notes.Count);
    }

    [TestMethod]
    public void QuotedValuesDecodeEscapesContinuationsAndSpanLines()
    {
        var t = Read(
            "b {\n string e \"q\\\"s\\\\t\\tn\\nz\"\n string c \"\\\r\n  long\"\n string m \"one\r\ntwo\"\n}\n"
        );
        Assert.AreEqual("q\"s\\t\tn\nz", t.Entries[0].Value);
        Assert.AreEqual("  long", t.Entries[1].Value);
        Assert.AreEqual("one\r\ntwo", t.Entries[2].Value);
    }

    [TestMethod]
    public void ByteBeyondAsciiIsReadAsWindows1252()
    {
        Assert.AreEqual("Pentium®", Read("b { string p \"Pentium®\" }").Entries.Single().Value);
        // 0x80 is the euro sign in Windows-1252 and a control character in Latin-1.
        Assert.AreEqual("€", Read("b { string p \"\u0080\" }").Entries.Single().Value);
    }

    [TestMethod]
    public void ImportLoadsTheNamedFileAtTopLevelEachDecodedOnItsOwn()
    {
        var t = Read("import \"o.ldl\"\nb { string a \"1\" }\n", ("o.ldl", "b { string o \"2\" }\n"));
        CollectionAssert.AreEqual(new[] { "o.ldl", "t.ldl" }, t.Entries.Select(e => e.File).ToArray());
        CollectionAssert.AreEqual(new[] { "b" }, t.Blocks.Select(b => b.Path).ToArray());
    }

    [TestMethod]
    public void ImportCycleTerminatesMissingImportAndImportInABlockAreNotes()
    {
        var t = Read(
            "import \"a.ldl\"\nimport \"nope.ldl\"\nb { import \"a.ldl\" }\n",
            ("a.ldl", "import \"t.ldl\"\nz { string y \"1\" }\n")
        );
        Assert.AreEqual(1, t.Entries.Count);
        Assert.IsTrue(t.Notes.Any(n => n.StartsWith("a.ldl:1:") && n.Contains("cycle")), string.Join("|", t.Notes));
        Assert.IsTrue(t.Notes.Any(n => n.StartsWith("t.ldl:2:") && n.Contains("nope.ldl")), string.Join("|", t.Notes));
        Assert.IsTrue(
            t.Notes.Any(n => n.StartsWith("t.ldl:3:") && n.Contains("inside a block")),
            string.Join("|", t.Notes)
        );
    }

    [TestMethod]
    public void UnknownStatementsAndMalformedEntriesAreNotesWithFileAndLineAndTheRestLoads()
    {
        var t = Read("junk\nb {\n string a unquoted\n string c \"ok\"\n}\n}\nd {\n");
        CollectionAssert.AreEqual(new[] { "ok" }, t.Entries.Select(e => e.Value).ToArray());
        var notes = string.Join("|", t.Notes);
        Assert.IsTrue(t.Notes.Any(n => n.StartsWith("t.ldl:1:") && n.Contains("junk")), notes);
        Assert.IsTrue(t.Notes.Any(n => n.StartsWith("t.ldl:3:") && n.Contains("not quoted")), notes);
        Assert.IsTrue(t.Notes.Any(n => n.StartsWith("t.ldl:6:")), notes);
        Assert.IsTrue(t.Notes.Any(n => n.StartsWith("t.ldl:7:") && n.Contains("not closed")), notes);
    }

    [TestMethod]
    public void TextThatDoesNotDecodeWithTheKeyIsUnsupported()
    {
        var wrong = Key.Select(b => (byte)(b * 5 + 1)).ToArray(); // 200 decoded characters: some fall below 32
        var e = Assert.ThrowsExactly<UnsupportedFormatException>(() =>
            TextTable.Read("t.ldl", Encode("b { string a \"" + new string('a', 200) + "\" }\n"), wrong, _ => null)
        );
        StringAssert.Contains(e.Message, "t.ldl");
    }

    [TestMethod]
    public void TheKeyIsTheFourHundredBytesAtTheKnownOffsetOfSplashExe()
    {
        var exe = new byte[TextTable.KeyOffset + TextTable.KeyLength];
        Key.CopyTo(exe, TextTable.KeyOffset);
        CollectionAssert.AreEqual(Key, TextTable.KeyFrom("splash.exe", exe));
        var e = Assert.ThrowsExactly<UnsupportedFormatException>(() => TextTable.KeyFrom("splash.exe", exe[..^1]));
        StringAssert.Contains(e.Message, "too short");
    }

    [TestMethod]
    public void AKeyOfAnotherLengthThanTheFileKeyIsUnsupported()
    {
        foreach (var key in new[] { Array.Empty<byte>(), Key[..^1] })
        {
            var e = Assert.ThrowsExactly<UnsupportedFormatException>(() =>
                TextTable.Read("t.ldl", Encode("b { }"), key, _ => null)
            );
            StringAssert.Contains(e.Message, "400");
        }
    }

    // --- the kind: the key is read from splash.exe of the install at run time ---

    readonly TempInstall temp = new();

    [TestCleanup]
    public void CleanUp() => temp.CleanUp();

    string Latin1(byte[] b) => Encoding.Latin1.GetString(b);

    GameFileSystem Install(bool withSplash, params (string Path, string Content)[] files)
    {
        var exe = new byte[TextTable.KeyOffset + TextTable.KeyLength];
        Key.CopyTo(exe, TextTable.KeyOffset);
        var all = files.ToList();
        if (withSplash)
            all.Add(("splash.exe", Latin1(exe)));
        return new GameFileSystem(temp.MakeInstall(all.ToArray()));
    }

    [TestMethod]
    public void TextTablesAreTheRootLdlFilesDecodedWithTheKeyOfSplashExe()
    {
        var files = Install(true, ("string.ldl", Latin1(Encode("b { string p \"Pentium®\" }"))), ("a/b.ldl", ""));
        var kind = new TextTableKind();
        CollectionAssert.AreEqual(new[] { "string.ldl" }, kind.Enumerate(files).ToArray());
        Assert.AreEqual("Pentium®", ((TextTable)kind.Load(files, "string.ldl")).Entries.Single().Value);
    }

    [TestMethod]
    public void TextTableImportsResolveThroughTheInstall()
    {
        var files = Install(
            true,
            ("default.ldl", Latin1(Encode("import \"string.ldl\"\n"))),
            ("string.ldl", Latin1(Encode("b { string p \"1\" }")))
        );
        Assert.AreEqual(
            "string.ldl",
            ((TextTable)new TextTableKind().Load(files, "default.ldl")).Entries.Single().File
        );
    }

    [TestMethod]
    public void AnImportResolvesRelativeToTheFolderOfTheImportingFile()
    {
        var files = Install(
            true,
            ("default.ldl", Latin1(Encode("import \"sub/a.ldl\"\n"))),
            ("sub/a.ldl", Latin1(Encode("import \"b.ldl\"\n"))),
            ("sub/b.ldl", Latin1(Encode("b { string p \"1\" }"))),
            ("b.ldl", Latin1(Encode("b { string p \"root\" }")))
        );
        var t = (TextTable)new TextTableKind().Load(files, "default.ldl");
        Assert.AreEqual("1", t.Entries.Single().Value);
        Assert.AreEqual(0, t.Notes.Count, string.Join("|", t.Notes));
    }

    [TestMethod]
    public void AnImportOutOfTheInstallRootIsNotFoundAndBecomesANote()
    {
        // A real x.ldl sits one folder above the install root, so an import that escapes would read it.
        var exe = new byte[TextTable.KeyOffset + TextTable.KeyLength];
        Key.CopyTo(exe, TextTable.KeyOffset);
        var root = temp.MakeInstall(
            ("x.ldl", Latin1(Encode("b { string p \"escaped\" }"))),
            ("game/SOD/a.sod", ""),
            ("game/splash.exe", Latin1(exe)),
            ("game/default.ldl", Latin1(Encode("import \"..\\x.ldl\"\nimport \"../x.ldl\"\n")))
        );
        var files = new GameFileSystem(System.IO.Path.Combine(root, "game"));
        var t = (TextTable)new TextTableKind().Load(files, "default.ldl");
        Assert.AreEqual(
            (0, 2),
            (t.Entries.Count, t.Notes.Count(n => n.Contains("not found"))),
            string.Join("|", t.Notes)
        );
    }

    [TestMethod]
    public void TextTablesSplashExeAndImportsAreReadFromTheInstallRootByPathNotThroughTheAddonLayer()
    {
        var exe = new byte[TextTable.KeyOffset + TextTable.KeyLength];
        Key.CopyTo(exe, TextTable.KeyOffset);
        var files = Install(
            true,
            ("default.ldl", Latin1(Encode("import \"string.ldl\"\nb { string r \"root\" }"))),
            ("string.ldl", Latin1(Encode("c { string r \"root\" }"))),
            ("addon/default.ldl", Latin1(Encode("b { string r \"addon\" }"))),
            ("addon/string.ldl", Latin1(Encode("c { string r \"addon\" }"))),
            ("addon/splash.exe", Latin1(new byte[exe.Length])),
            ("addon/only.ldl", Latin1(Encode("b { }")))
        );
        var t = (TextTable)new TextTableKind().Load(files, "default.ldl");
        CollectionAssert.AreEqual(new[] { "root", "root" }, t.Entries.Select(e => e.Value).ToArray());
        Assert.AreEqual(0, t.Notes.Count, string.Join("|", t.Notes));
        Assert.ThrowsExactly<System.IO.FileNotFoundException>(() => new TextTableKind().Load(files, "only.ldl"));
    }

    [TestMethod]
    public void WithoutSplashExeATextTableIsUnsupportedWithThatReason()
    {
        var files = Install(false, ("string.ldl", Latin1(Encode("b { }"))));
        var e = Assert.ThrowsExactly<UnsupportedFormatException>(() => new TextTableKind().Load(files, "string.ldl"));
        StringAssert.Contains(e.Message, "splash.exe");
    }
}
