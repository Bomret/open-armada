using System;
using System.Linq;
using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Formats;
using OpenArmada.Sandbox;

namespace OpenArmada.Tests;

[TestClass]
public class SprayEmitterTests
{
    static readonly EmitterFrame Origin = new(Vector3.Zero, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ);

    static SpriteEmitter Definition(
        double period = 0.1,
        double lifespan = 100,
        double velocity = 8,
        double spread = 0,
        double expansion = 0
    ) =>
        new()
        {
            Name = "plasmasml",
            Kind = EmitterKind.Spray,
            Sprite = "ventplas",
            FiringPeriod = period,
            Lifespan = lifespan,
            Velocity = velocity,
            SpreadAngle = spread,
            ExpansionRate = expansion,
        };

    static SprayEmitter Spray(SpriteEmitter definition, int seed = 1) => new(definition, new Random(seed).NextDouble);

    static void Run(
        SprayEmitter spray,
        double seconds,
        int framesPerSecond,
        bool showing = true,
        EmitterFrame? frame = null
    )
    {
        for (var i = 0; i < (int)Math.Round(seconds * framesPerSecond); i++)
        {
            spray.Step(1.0 / framesPerSecond, showing, frame ?? Origin);
        }
    }

    [TestMethod]
    [DataRow(10)]
    [DataRow(30)]
    [DataRow(60)]
    public void APeriodOfATenthSpawnsTenParticlesInASecondAtAnyFrameRateFromTen(int framesPerSecond)
    {
        var spray = Spray(Definition(period: 0.1));
        Run(spray, 1, framesPerSecond);
        Assert.AreEqual(10, spray.Particles.Count);
    }

    [TestMethod]
    public void APeriodOfZeroSpawnsOneParticlePerTick()
    {
        var spray = Spray(Definition(period: 0));
        Run(spray, 1, 10);
        Assert.AreEqual(10, spray.Particles.Count);
    }

    [TestMethod]
    public void AParticleStartsAtTheNodeAndMovesAlongItsZAtTheVelocity()
    {
        var spray = Spray(Definition(period: 100, velocity: 8));
        var frame = new EmitterFrame(new Vector3(5, 1, 2), Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ);
        spray.Step(0.1, true, frame);
        Assert.AreEqual(new Vector3(5, 1, 2), spray.Particles.Single().Position);
        Run(spray, 0.5, 10, frame: frame);
        Assert.AreEqual(new Vector3(5, 1, 6).Z, spray.Particles.Single().Position.Z, 1e-4);
    }

    [TestMethod]
    public void AParticleFollowsTheNodesOwnZNotTheWorldZ()
    {
        var spray = Spray(Definition(period: 100, velocity: 10));
        var turned = new EmitterFrame(Vector3.Zero, -Vector3.UnitZ, Vector3.UnitY, Vector3.UnitX);
        spray.Step(0.1, true, turned);
        Run(spray, 1, 10, frame: turned);
        Assert.AreEqual(new Vector3(10, 0, 0).X, spray.Particles.Single().Position.X, 1e-4);
    }

    [TestMethod]
    public void AParticleLeavesWithinTheSpreadAngleOfTheNodesZ()
    {
        var spray = Spray(Definition(period: 0.1, velocity: 10, spread: 5));
        Run(spray, 10, 10);
        var angles = spray
            .Particles.Select(p => Math.Acos(Vector3.Dot(Vector3.Normalize(p.Velocity), Vector3.UnitZ)) * 180 / Math.PI)
            .ToList();
        // Each of the three angles is within the spread of 0; two of them tilt the direction, so the tilt stays within
        // the spread times the square root of two, and the particles do differ.
        Assert.IsTrue(angles.All(a => a <= 5 * Math.Sqrt(2) + 1e-3), $"largest tilt {angles.Max()}");
        Assert.IsTrue(angles.Max() > 1, $"largest tilt {angles.Max()}");
    }

    [TestMethod]
    public void TheSameRandomSourceSpraysTheSameParticles()
    {
        var a = Spray(Definition(spread: 5), seed: 7);
        var b = Spray(Definition(spread: 5), seed: 7);
        Run(a, 1, 10);
        Run(b, 1, 10);
        CollectionAssert.AreEqual(
            a.Particles.Select(p => p.Position).ToArray(),
            b.Particles.Select(p => p.Position).ToArray()
        );
    }

    [TestMethod]
    public void AParticleIsGoneAtItsLifespan()
    {
        var spray = Spray(Definition(period: 100, lifespan: 1));
        spray.Step(0.1, true, Origin);
        Run(spray, 0.9, 10);
        Assert.AreEqual(1, spray.Particles.Count);
        Run(spray, 0.1, 10);
        Assert.AreEqual(0, spray.Particles.Count);
    }

    [TestMethod]
    public void AParticleGrowsByTheExpansionRatePerSecondOfAge()
    {
        var spray = Spray(Definition(period: 100, expansion: 1.1));
        spray.Step(0.1, true, Origin);
        Run(spray, 0.5, 10);
        Assert.AreEqual(2 + 1.1 * 0.5, spray.Size(2, spray.Particles.Single()), 1e-6);
    }

    [TestMethod]
    public void AParticleWhoseSizeReachesZeroOrLessIsNotDrawn()
    {
        var spray = Spray(Definition(period: 100, expansion: -1));
        spray.Step(0.1, true, Origin);
        Run(spray, 1.5, 10);
        Assert.IsTrue(spray.Drawn(spray.Particles.Single(), 2, 2));
        Run(spray, 0.5, 10);
        Assert.IsFalse(spray.Drawn(spray.Particles.Single(), 2, 2));
    }

    [TestMethod]
    public void AnEmitterUnderAHiddenNodeSpawnsNothing()
    {
        var spray = Spray(Definition());
        Run(spray, 2, 10, showing: false);
        Assert.AreEqual(0, spray.Particles.Count);
    }

    [TestMethod]
    public void LiveParticlesFreezeWhileTheirNodeIsHidden()
    {
        var spray = Spray(Definition(period: 100));
        Run(spray, 0.5, 10);
        var before = (spray.Particles.Single().Position, spray.Particles.Single().Age);
        Run(spray, 2, 10, showing: false);
        Assert.AreEqual(before, (spray.Particles.Single().Position, spray.Particles.Single().Age));
    }

    [TestMethod]
    public void ANodeShownAgainSpraysAgain()
    {
        var spray = Spray(Definition());
        Run(spray, 1, 10, showing: false);
        Run(spray, 1, 10);
        Assert.AreEqual(10, spray.Particles.Count);
    }
}
