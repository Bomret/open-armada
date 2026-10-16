using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Assets;
using OpenArmada.Formats;

namespace OpenArmada.Tests;

[TestClass]
public class MapKindTests
{
    readonly TempInstall temp = new();

    [TestCleanup]
    public void CleanUp() => temp.CleanUp();

    static string S(byte[] bytes) => Encoding.Latin1.GetString(bytes);

    static BznBuilder Sample()
    {
        var b = new BznBuilder();
        b.Objects.Add(new("fbattle", 1, new Vector3(100, 38.5f, 200), "ship1"));
        b.Objects.Add(new("Msun1", 0, new Vector3(16000, 0, 3000), "sun"));
        return b;
    }

    GameFileSystem Install(params (string Path, string Content)[] files) => new(temp.MakeInstall(files));

    static readonly string Mdf = "StartLocations = 2\r\nStart1 = 0 0\r\nStart2 = 117 117\r\n";

    [TestMethod]
    public void MapsListTheBznFilesOfTheBznFolder()
    {
        var fs = Install(("bzn/a.bzn", S(Sample().Build())), ("bzn/a.bmp", ""), ("other/b.bzn", ""));
        CollectionAssert.AreEqual(new[] { "bzn/a.bzn" }, new MapKind().Enumerate(fs).ToArray());
    }

    [TestMethod]
    public void MapLoadsWithStartsOverviewAndScriptedPaths()
    {
        var fs = Install(("bzn/a.bzn", S(Sample().Build())), ("bzn/a.mdf", Mdf), ("bzn/a.bmp", S(BmpPaletteTests.MakeBmp(128, 128, 24))),
            ("3D_Paths/p2.pth", "1\r\n1 2 3\r\n"));
        var m = (MapAsset)new MapKind().Load(fs, "bzn/a.bzn");
        Assert.AreEqual(2, m.Starts.Count);
        Assert.AreEqual((6400f, 0f), m.Map.Bounds.FromStartCell(m.Starts[1].Cell));
        Assert.AreEqual("bzn/a.bmp", m.Overview);
        Assert.AreEqual(("p2.pth", new Vector3(1, 2, 3)), (m.ScriptedPaths.Single().Name, m.ScriptedPaths.Single().Points[0]));
    }

    [TestMethod]
    public void MapWithoutCompanionsLoadsWithoutStartsOrOverview()
    {
        var fs = Install(("bzn/a.bzn", S(Sample().Build())));
        var m = (MapAsset)new MapKind().Load(fs, "bzn/a.bzn");
        Assert.AreEqual((0, (string?)null), (m.Starts.Count, m.Overview));
    }

    [TestMethod]
    public void ObjectOutsideTheBoundsIsANoteNotAFailure()
    {
        var fs = Install(("bzn/a.bzn", S(Sample().Build())));
        var notes = new MapKind().Check(fs, "bzn/a.bzn");
        Assert.AreEqual(1, notes.Count);
        StringAssert.Contains(notes[0], "sun");
        StringAssert.Contains(notes[0], "Msun1");
        StringAssert.Contains(notes[0], "16000");
    }

    [TestMethod]
    public void CorruptMapFailsItsOwnAssetAndTheOthersStillLoad()
    {
        var good = Sample().Build();
        var root = temp.MakeInstall(("bzn/good.bzn", S(good)), ("bzn/cut.bzn", S(good[..(good.Length - 5)])));
        var output = new StringWriter();
        var code = AssetCheck.Run(root, new AssetKind[] { new MapKind() }, output);
        Assert.AreEqual(1, code);
        StringAssert.Contains(output.ToString(), "FAIL maps/bzn/cut.bzn");
        StringAssert.Contains(output.ToString(), "maps: 1 loaded");
    }

    [TestMethod]
    public void UnrecognizedTokenTypeFailsTheMapWithItsOffset()
    {
        var b = Sample();
        var good = b.Build();
        b.Extra = new() { BznBuilder.Token(99, new byte[4]) };
        var fs = Install(("bzn/a.bzn", S(b.Build())));
        var e = Assert.ThrowsExactly<InvalidDataException>(() => new MapKind().Check(fs, "bzn/a.bzn"));
        StringAssert.Contains(e.Message, $"offset {good.Length}");
    }

    [TestMethod]
    public void BrokenScriptedPathFailsTheMapNamingTheFile()
    {
        var fs = Install(("bzn/a.bzn", S(Sample().Build())), ("3D_Paths/bad.pth", "2\r\n1 2 3\r\n"));
        var e = Assert.ThrowsExactly<InvalidDataException>(() => new MapKind().Load(fs, "bzn/a.bzn"));
        StringAssert.Contains(e.Message, "3D_Paths/bad.pth");
    }

    [TestMethod]
    public void MdfWithoutItsStartsFailsTheMap()
    {
        var fs = Install(("bzn/a.bzn", S(Sample().Build())), ("bzn/a.mdf", "StartLocations = 3\r\nStart1 = 1 1\r\n"));
        Assert.ThrowsExactly<InvalidDataException>(() => new MapKind().Load(fs, "bzn/a.bzn"));
    }
}
