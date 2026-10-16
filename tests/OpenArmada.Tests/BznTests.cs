using System;
using System.IO;
using System.Linq;
using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Formats;

namespace OpenArmada.Tests;

[TestClass]
public class BznTests
{
    static BznBuilder Sample()
    {
        var b = new BznBuilder();
        b.Objects.Add(new("fbattle", 2, new Vector3(100, 38.5f, 200), "ship1", 5));
        return b;
    }

    [TestMethod]
    public void ReadsHeaderBoundsAndEnvironment()
    {
        var map = Bzn.Read(Sample().Build());
        Assert.AreEqual((2051, "Test Map", "tech1.tt", "federation", "mbgstars.sod"), (map.Version, map.Description, map.TechTree, map.Race, map.BackgroundArt));
        Assert.AreEqual(new MapBounds(0, 6400, -28.5f, 105.5f, 0, 6400), map.Bounds);
    }

    [TestMethod]
    public void ReadsObjectWithClassPositionOrientationAndTeam()
    {
        var b = Sample();
        b.Objects[0] = b.Objects[0] with { Yaw = 0.5f };
        var o = Bzn.Read(b.Build()).Objects.Single();
        Assert.AreEqual(("fbattle", 2, "ship1", new Vector3(100, 38.5f, 200)), (o.Class, o.Team, o.Label, o.Position));
        Assert.AreEqual(MathF.Cos(0.5f), o.Right.X, 1e-6f);
        Assert.AreEqual(MathF.Sin(0.5f), o.Front.X, 1e-6f);
    }

    [TestMethod]
    public void ObjectWithClassPrefixKeepsPrefixTokensRaw()
    {
        var b = Sample();
        b.Objects[0] = b.Objects[0] with { Prefix = new[] { BznBuilder.Ptr(9), BznBuilder.Bool(false) } };
        var o = Bzn.Read(b.Build()).Objects.Single();
        CollectionAssert.AreEqual(new byte[] { 8, 1 }, o.Prefix.Select(t => t.Type).ToArray());
        Assert.AreEqual(20, o.Tail.Count);
        Assert.AreEqual(("Display fbattle", "federation"), (o.DisplayName, o.Race));
    }

    [TestMethod]
    public void ReadsTeamsMissionAndPaths()
    {
        var b = Sample();
        b.Paths.Add(("Camera_1", new[] { new Vector3(1, 38.5f, 2), new Vector3(3, 38.5f, 4) }));
        var map = Bzn.Read(b.Build());
        Assert.AreEqual((9, 8, 3500f), (map.Teams.Count, map.Teams[8].Color, map.Teams[8].Unknown2));
        Assert.AreEqual("EmptyMission", map.MissionClass);
        Assert.AreEqual(("Camera_1", 2), (map.Paths[0].Name, map.Paths[0].Points.Count));
        Assert.AreEqual(new Vector3(3, 38.5f, 4), map.Paths[0].Points[1]);
    }

    [TestMethod]
    public void TeamOutsideTheGameRangeFails()
    {
        var b = Sample();
        b.Objects[0] = b.Objects[0] with { Team = 9 };
        var e = Assert.ThrowsExactly<InvalidDataException>(() => Bzn.Read(b.Build()));
        StringAssert.Contains(e.Message, "team 9");
    }

    [TestMethod]
    public void NonFinitePositionFails()
    {
        var b = Sample();
        b.Objects[0] = b.Objects[0] with { Position = new Vector3(float.NaN, 0, 0) };
        var e = Assert.ThrowsExactly<InvalidDataException>(() => Bzn.Read(b.Build()));
        StringAssert.Contains(e.Message, "ship1");
    }

    [TestMethod]
    public void UnrecognizedTokenTypeIsReportedWithItsOffset()
    {
        var b = Sample();
        var good = b.Build();
        b.Extra = new() { BznBuilder.Token(99, new byte[4]) };
        var e = Assert.ThrowsExactly<InvalidDataException>(() => Bzn.Read(b.Build()));
        StringAssert.Contains(e.Message, $"type 99 at offset {good.Length}");
    }

    [TestMethod]
    public void TokensAfterTheLastSectionAreReportedWithTheirOffset()
    {
        var b = Sample();
        var good = b.Build();
        b.Extra = new() { BznBuilder.Int(1) };
        var e = Assert.ThrowsExactly<InvalidDataException>(() => Bzn.Read(b.Build()));
        StringAssert.Contains(e.Message, $"offset {good.Length}");
    }

    [TestMethod]
    public void TruncatedMapFailsWithTheOffsetItEndsAt()
    {
        var bytes = Sample().Build();
        foreach (var cut in new[] { bytes.Length - 3, bytes.Length - 200, 20, 400 })
            Assert.ThrowsExactly<InvalidDataException>(() => Bzn.Read(bytes[..cut]), $"cut at {cut}");
    }

    [TestMethod]
    public void ObjectCountLargerThanTheFileFailsWithoutAllocating()
    {
        var b = Sample();
        b.CountOverride = int.MaxValue;
        Assert.ThrowsExactly<InvalidDataException>(() => Bzn.Read(b.Build()));
    }

    [TestMethod]
    public void VersionsOutsideTheReadRangeAreUnsupported()
    {
        var b = Sample();
        b.Version = 2040;
        Assert.ThrowsExactly<UnsupportedFormatException>(() => Bzn.Read(b.Build()));
    }

    [TestMethod]
    public void NotAMapFails()
    {
        Assert.ThrowsExactly<InvalidDataException>(() => Bzn.Read(new byte[] { 1, 2, 3 }));
    }
}
