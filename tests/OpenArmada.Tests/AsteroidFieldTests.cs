using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Assets;
using OpenArmada.Formats;
using OpenArmada.Sandbox;

namespace OpenArmada.Tests;

// The rocks of an asteroid field and the field's definition values, from values and maps built in code (INV-0001).
[TestClass]
public class AsteroidFieldTests
{
    static readonly string[] Eight =
    {
        "master",
        "master1",
        "master2",
        "master3",
        "master4",
        "master5",
        "master6",
        "master7",
    };

    static AsteroidField Field(
        uint seed = 1,
        int min = 2,
        int max = 9,
        float radius = 100,
        float spin = 0.5f,
        float orbit = 0.25f
    ) =>
        new()
        {
            Center = new Vector3(10, 20, 30),
            Min = min,
            Max = max,
            Radius = radius,
            RotationSpeed = spin,
            OrbitingSpeed = orbit,
            Seed = seed,
            Models = Eight,
        };

    [TestMethod]
    public void TheRockCountLiesInTheRangeAndReachesBothEnds()
    {
        var counts = Enumerable.Range(0, 300).Select(s => Field((uint)s).Rocks(5).Count).ToList();
        Assert.IsTrue(counts.All(c => c is >= 2 and <= 9));
        Assert.AreEqual(2, counts.Min());
        Assert.AreEqual(9, counts.Max());
    }

    [TestMethod]
    public void EveryRockLiesInsideTheFlattenedSphereAtAnyTime()
    {
        const float extent = 10,
            r = 100 - extent;
        foreach (var s in Enumerable.Range(0, 50))
        {
            foreach (var rock in Field((uint)s, 20, 30).Rocks(extent))
            {
                foreach (var t in new[] { 0.0, 3.7 })
                {
                    var d = rock.Pose(t).Position - new Vector3(10, 20, 30);
                    Assert.IsTrue(d.X * d.X / (r * r) + d.Y * d.Y / (r * r * 0.25f) + d.Z * d.Z / (r * r) <= 1.0001f);
                }
            }
        }
    }

    [TestMethod]
    public void TheDrawBoxHoldsARockAtTheCenterOfAFieldSmallerThanTheLargestRock()
    {
        const float extent = 10;
        var (min, max) = Field(1, 2, 9, 3).DrawBox(extent);
        Assert.IsTrue(min.X <= 10 - extent && min.Y <= 20 - extent && min.Z <= 30 - extent);
        Assert.IsTrue(max.X >= 10 + extent && max.Y >= 20 + extent && max.Z >= 30 + extent);
    }

    [TestMethod]
    public void TheDrawBoxHoldsEveryPlannedRockAtAnyTimeInAFieldMuchLargerThanTheLargestRock()
    {
        const float extent = 2;
        var field = Field(5, 20, 30, 100);
        var (min, max) = field.DrawBox(extent);
        foreach (var rock in field.Rocks(extent))
        {
            foreach (var t in new[] { 0.0, 1.3, 7.9, 25.0 })
            {
                var p = rock.Pose(t).Position;
                Assert.IsTrue(
                    p.X >= min.X && p.Y >= min.Y && p.Z >= min.Z && p.X <= max.X && p.Y <= max.Y && p.Z <= max.Z
                );
            }
        }
    }

    [TestMethod]
    public void OnlyTheEightNamedModelsAreChosenAndAllAppear()
    {
        var models = Enumerable
            .Range(0, 100)
            .SelectMany(s => Field((uint)s, 20, 30).Rocks(5))
            .Select(r => r.Model)
            .Distinct()
            .ToList();
        CollectionAssert.AreEquivalent(Eight, models);
    }

    [TestMethod]
    public void TheSameValuesAndSeedGiveTheSameRocks()
    {
        CollectionAssert.AreEqual(
            Field(7).Rocks(5).Select(r => r.Pose(1)).ToList(),
            Field(7).Rocks(5).Select(r => r.Pose(1)).ToList()
        );
    }

    [TestMethod]
    public void AnotherSeedGivesOtherRocks()
    {
        CollectionAssert.AreNotEqual(
            Field(7, 20, 30).Rocks(5).Select(r => r.Pose(0).Position).ToList(),
            Field(8, 20, 30).Rocks(5).Select(r => r.Pose(0).Position).ToList()
        );
    }

    [TestMethod]
    public void ThePoseAtATimeDoesNotDependOnTheFrameStep()
    {
        var rock = Field(3).Rocks(5)[0];
        double Advance(double step)
        {
            double t = 0;
            for (var i = 0; i < 10 / step; i++)
            {
                t += step;
            }

            return t;
        }

        var a = rock.Pose(Advance(0.5));
        var b = rock.Pose(Advance(0.125));
        Assert.AreEqual(a.Position, b.Position);
        Assert.AreEqual(a.Orientation, b.Orientation);
    }

    [TestMethod]
    public void ARockCirclesTheCenterAtTheOrbitingSpeedAndSpinsAtTheRotationSpeed()
    {
        var f = Field(3, radius: 50, spin: 0.5f, orbit: 0.25f);
        var rock = f.Rocks(5)[0];
        var (p0, q0) = rock.Pose(0);
        var (p1, q1) = rock.Pose(2);
        var center = new Vector3(10, 20, 30);
        Assert.AreEqual((p0 - center).Y, (p1 - center).Y, 1e-3f);
        var a0 = MathF.Atan2((p0 - center).Z, (p0 - center).X);
        var a1 = MathF.Atan2((p1 - center).Z, (p1 - center).X);
        var turned = MathF.Abs(MathF.IEEERemainder(a0 - a1, 2 * MathF.PI));
        Assert.AreEqual(0.5f, turned, 1e-3f); // orbit 0.25 rad/s over 2 s
        var spun = 2 * MathF.Acos(MathF.Min(1, MathF.Abs(Quaternion.Dot(q0, q1))));
        Assert.AreEqual(1f, spun, 1e-3f); // spin 0.5 rad/s over 2 s
    }

    static BznBuilder.Obj Obj(string cls, uint handle) =>
        new()
        {
            Class = cls,
            Team = 0,
            Position = new Vector3(100, 0, 200),
            Label = "f" + handle,
            Pointer = handle,
        };

    static (
        IReadOnlyList<AsteroidField> Fields,
        IReadOnlyList<ReportEntry> Reports,
        IReadOnlyList<Obstacle> Obstacles
    ) Collect(BznBuilder map, string odf, Func<string, bool>? hasModel = null)
    {
        var temp = new TempInstall();
        try
        {
            var install = new GameFileSystem(temp.MakeInstall(("addon/mafield.odf", odf)));
            var parsed = Bzn.Read(map.Build());
            var asset = new MapAsset
            {
                Name = "bzn/t.bzn",
                Map = parsed,
                Starts = Array.Empty<MapStart>(),
                Overview = null,
                ScriptedPaths = Array.Empty<MapPath>(),
            };

            return AsteroidField.Collect(
                parsed,
                SandboxPlan.Definitions(install, asset).Found,
                hasModel ?? (m => Eight.Contains(m))
            );
        }
        finally
        {
            temp.CleanUp();
        }
    }

    static BznBuilder MapOf(string description, params BznBuilder.Obj[] objects)
    {
        var b = new BznBuilder { Background = "", Description = description };
        b.Objects.AddRange(objects);

        return b;
    }

    const string Good =
        "classLabel = AsteroidField\nminAsteroids = 3\nmaxAsteroids = 8\nfieldRadius = 400\nrotationSpeed = 0.5\norbitingSpeed = 0.25";

    [TestMethod]
    public void AFieldTakesItsValuesFromTheDefinitionAndTheMirroredPosition()
    {
        var (fields, reports, _) = Collect(MapOf("m", Obj("mafield", 1)), Good);
        var f = fields.Single();
        Assert.AreEqual((3, 8, 400f, 0.5f, 0.25f), (f.Min, f.Max, f.Radius, f.RotationSpeed, f.OrbitingSpeed));
        Assert.AreEqual(new Vector3(100, 0, -200), f.Center);
        Assert.AreEqual(0, reports.Count);
    }

    [TestMethod]
    public void TwoFieldsOfOneMapGetDifferentSeedsAndASeedIsTheSameOnEveryRead()
    {
        var a = Collect(MapOf("m", Obj("mafield", 1), Obj("mafield", 2)), Good).Fields;
        var b = Collect(MapOf("m", Obj("mafield", 1), Obj("mafield", 2)), Good).Fields;
        Assert.AreNotEqual(a[0].Seed, a[1].Seed);
        CollectionAssert.AreEqual(a.Select(f => f.Seed).ToList(), b.Select(f => f.Seed).ToList());
    }

    [TestMethod]
    public void AFieldWithMinAboveMaxOrARadiusOfZeroIsReportedWithACountAndSkipped()
    {
        var map = MapOf("m", Obj("mafield", 1), Obj("mafield", 2));
        var (fields, reports, _) = Collect(map, Good.Replace("minAsteroids = 3", "minAsteroids = 9"));
        Assert.AreEqual(0, fields.Count);
        Assert.AreEqual(2, reports.Single().Count);
        (fields, reports, _) = Collect(map, Good.Replace("fieldRadius = 400", "fieldRadius = 0"));
        Assert.AreEqual(0, fields.Count);
        Assert.AreEqual(2, reports.Single().Count);
    }

    [TestMethod]
    public void AFieldWithAMissingKeyIsReportedOncePerKeyWithACountAndSkipped()
    {
        var (fields, reports, _) = Collect(
            MapOf("m", Obj("mafield", 1), Obj("mafield", 2)),
            Good.Replace("orbitingSpeed = 0.25", "")
        );
        Assert.AreEqual(0, fields.Count);
        Assert.AreEqual(2, reports.Single().Count);
        StringAssert.Contains(reports.Single().Text, "orbitingSpeed");
    }

    [TestMethod]
    public void AMissingModelIsReportedAndTheOthersAreUsed()
    {
        var (fields, reports, _) = Collect(
            MapOf("m", Obj("mafield", 1)),
            Good,
            m => Eight.Contains(m) && m != "master3"
        );
        CollectionAssert.DoesNotContain(fields.Single().Models.ToList(), "master3");
        Assert.AreEqual(7, fields.Single().Models.Count);
        CollectionAssert.AreEqual(new[] { "master3" }, reports.Single().Names.ToArray());
    }

    [TestMethod]
    public void AMissingModelIsCountedOncePerFieldWhateverTheNumberOfMissingModels()
    {
        var (_, reports, _) = Collect(
            MapOf("m", Obj("mafield", 1), Obj("mafield", 2), Obj("mafield", 3)),
            Good,
            m => Eight.Contains(m) && m != "master3" && m != "master4"
        );
        Assert.AreEqual(3, reports.Single().Count);
        CollectionAssert.AreEqual(new[] { "master3", "master4" }, reports.Single().Names.ToArray());
    }

    [TestMethod]
    public void AFieldWithNoModelLeftIsReportedWithACountAndSkipped()
    {
        var (fields, reports, _) = Collect(MapOf("m", Obj("mafield", 1), Obj("mafield", 2)), Good, _ => false);
        Assert.AreEqual(0, fields.Count);
        var noModel = reports.Single(r => r.Cause.StartsWith("no asteroid model left"));
        Assert.AreEqual(2, noModel.Count);
    }

    [TestMethod]
    public void AFieldPushesByItsRadiusAroundItsMirroredCenter()
    {
        var (_, _, obstacles) = Collect(MapOf("m", Obj("mafield", 1)), Good);
        Assert.AreEqual(new Obstacle(new Vector3(100, 0, -200), 400, true), obstacles.Single());
    }

    [TestMethod]
    public void AFieldsCollisionRadiusIsReadAndAMissingOrBadOneIsZero()
    {
        float Read(string keys) =>
            Collect(MapOf("m", Obj("mafield", 1)), Good + keys).Obstacles.Single().CollisionRadius;
        Assert.AreEqual(35f, Read("\ncollisionRadius = 35"));
        Assert.AreEqual(0f, Read(""));
        Assert.AreEqual(0f, Read("\ncollisionRadius = -4"));
        Assert.AreEqual(0f, Read("\ncollisionRadius = wide"));
    }

    [TestMethod]
    public void AFieldCollisionRadiusThatIsNoNumberIsReportedOnceAndOneThatIsMissingOrNotAbove0IsNot()
    {
        IReadOnlyList<ReportEntry> Reports(string keys) =>
            Collect(MapOf("m", Obj("mafield", 1), Obj("mafield", 2)), Good + keys)
                .Reports.Where(r => r.Cause.Contains("collisionRadius"))
                .ToList();
        var bad = Reports("\ncollisionRadius = wide").Single();
        Assert.AreEqual(2, bad.Count, "both fields of the class");
        foreach (var value in new[] { "1e40", "NaN", "Infinity" })
        {
            Assert.AreEqual(
                2,
                Reports($"\ncollisionRadius = {value}").Single().Count,
                $"{value}: reported once per cause"
            );
        }

        Assert.AreEqual(0, Reports("").Count, "missing");
        Assert.AreEqual(0, Reports("\ncollisionRadius = -4").Count, "not above 0");
    }

    [TestMethod]
    public void AFieldWhoseRocksCannotBeDrawnStillPushesByItsRadius()
    {
        var (fields, reports, obstacles) = Collect(MapOf("m", Obj("mafield", 1)), Good, _ => false);
        Assert.AreEqual(0, fields.Count);
        Assert.AreEqual(1, reports.Count(r => r.Cause.StartsWith("no asteroid model left")));
        Assert.AreEqual(400f, obstacles.Single().Radius);
    }

    [TestMethod]
    [DataRow("fieldRadius = 400", "fieldRadius = 0")]
    [DataRow("fieldRadius = 400", "fieldRadius = -5")]
    [DataRow("fieldRadius = 400", "fieldRadius = nan")]
    [DataRow("fieldRadius = 400", "fieldRadius = 1e999")]
    [DataRow("fieldRadius = 400", "fieldRadius = x")]
    [DataRow("fieldRadius = 400", "")]
    public void AFieldWithAnInvalidRadiusPushesNothingAndIsReportedOnceForTheClass(string from, string to)
    {
        var (fields, reports, obstacles) = Collect(
            MapOf("m", Obj("mafield", 1), Obj("mafield", 2)),
            Good.Replace(from, to)
        );
        Assert.AreEqual(0, fields.Count);
        Assert.AreEqual(0, obstacles.Count);
        Assert.AreEqual(1, reports.Count);
        Assert.AreEqual(2, reports.Single().Count);
    }

    [TestMethod]
    public void AFieldWithAnotherMissingValueButAValidRadiusStillPushes()
    {
        var (fields, reports, obstacles) = Collect(
            MapOf("m", Obj("mafield", 1)),
            Good.Replace("orbitingSpeed = 0.25", "")
        );
        Assert.AreEqual(0, fields.Count);
        Assert.AreEqual(1, reports.Count);
        Assert.AreEqual(400f, obstacles.Single().Radius);
    }

    [TestMethod]
    [DataRow("minAsteroids = 3", "minAsteroids = -1")]
    [DataRow("maxAsteroids = 8", "maxAsteroids = -1")]
    [DataRow("maxAsteroids = 8", "maxAsteroids = 2147483647")]
    [DataRow("maxAsteroids = 8", "maxAsteroids = 1000000")]
    public void AFieldWithANegativeOrHugeCountIsReportedWithACountAndSkipped(string from, string to)
    {
        var (fields, reports, _) = Collect(MapOf("m", Obj("mafield", 1), Obj("mafield", 2)), Good.Replace(from, to));
        Assert.AreEqual(0, fields.Count);
        Assert.AreEqual(2, reports.Single().Count);
        StringAssert.Contains(reports.Single().Cause, "count");
    }

    static uint SeedUnder(string path, string description)
    {
        var temp = new TempInstall();
        try
        {
            var install = new GameFileSystem(temp.MakeInstall(("addon/mafield.odf", Good)));
            var parsed = Bzn.Read(MapOf(description, Obj("mafield", 1)).Build());
            var asset = new MapAsset
            {
                Name = path,
                Map = parsed,
                Starts = Array.Empty<MapStart>(),
                Overview = null,
                ScriptedPaths = Array.Empty<MapPath>(),
            };

            return SandboxPlan
                .Build(asset, SandboxPlan.Definitions(install, asset), m => Eight.Contains(m))
                .Fields.Single()
                .Seed;
        }
        finally
        {
            temp.CleanUp();
        }
    }

    [TestMethod]
    public void TheSeedDoesNotDependOnTheSpellingOfTheMapPath()
    {
        Assert.AreEqual(SeedUnder("bzn/4mama.bzn", "m"), SeedUnder("BZN/4MAMA.BZN", "m"));
    }

    [TestMethod]
    public void TheSeedFollowsTheMapContent()
    {
        Assert.AreNotEqual(SeedUnder("bzn/t.bzn", "one"), SeedUnder("bzn/t.bzn", "two"));
    }
}
