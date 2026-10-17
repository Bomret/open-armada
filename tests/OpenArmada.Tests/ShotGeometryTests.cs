using System;
using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Sandbox;

namespace OpenArmada.Tests;

// Where a drawn beam sits and how a missed shot is aimed, as plain vectors.
[TestClass]
public class ShotGeometryTests
{
    static void AssertSpans(Vector3 start, Vector3 end)
    {
        var pose = ShotGeometry.Beam(start, end)!.Value;
        Assert.AreEqual(Vector3.Distance(start, end), pose.X.Length(), 1e-3f);
        Assert.AreEqual(1f, pose.Y.Length(), 1e-5f);
        Assert.AreEqual(1f, pose.Z.Length(), 1e-5f);
        Assert.AreEqual(0f, Vector3.Dot(pose.X, pose.Y), 1e-4f);
        Assert.AreEqual(0f, Vector3.Dot(pose.X, pose.Z), 1e-4f);
        Assert.AreEqual(0f, Vector3.Dot(pose.Y, pose.Z), 1e-5f);
        Assert.AreEqual((start + end) / 2, pose.Center);
    }

    [TestMethod]
    public void BeamAlongZIsAsLongAsItsDistance() => AssertSpans(new(0, 0, 0), new(0, 0, 50));

    [TestMethod]
    public void BeamAlongYIsAsLongAsItsDistance() => AssertSpans(new(0, 0, 0), new(0, -30, 0));

    [TestMethod]
    public void DiagonalBeamIsAsLongAsItsDistanceAndNotSheared() => AssertSpans(new(1, 2, 3), new(41, -18, 33));

    [TestMethod]
    public void BeamBetweenOnePointHasNoPose() => Assert.IsNull(ShotGeometry.Beam(new(1, 1, 1), new(1, 1, 1)));

    [TestMethod]
    public void MissAimLiesBesideTheTargetByItsRadii()
    {
        var from = new Vector3(0, 0, 0);
        var center = new Vector3(0, 0, 100);
        var aim = ShotGeometry.MissAim(from, center, 5);
        Assert.AreEqual(10f, Vector3.Distance(aim, center), 1e-3f);
        Assert.AreEqual(0f, Vector3.Dot(aim - center, center - from), 1e-3f);
    }

    [TestMethod]
    public void MissAimAtAPointBesideAVerticalLineStillMoves()
    {
        var aim = ShotGeometry.MissAim(new(0, 0, 0), new(0, 100, 0), 5);
        Assert.AreEqual(10f, Vector3.Distance(aim, new(0, 100, 0)), 1e-3f);
    }
}
