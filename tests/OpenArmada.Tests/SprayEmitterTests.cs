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

    // The timer resets to the period on a spawn (0x57549f) and counts down on the fixed 0.02 s tick. A period of 0.11 s
    // is 6 ticks (0.11 - 6 * 0.02 < 0, 5 ticks leave 0.01), so the 50 ticks of a second spawn on ticks 1, 7, ..., 49.
    [TestMethod]
    [DataRow(10)]
    [DataRow(24)]
    [DataRow(30)]
    [DataRow(45)]
    [DataRow(60)]
    public void TheSameCountSpawnsInASecondOfSimulatedTimeAtAnyFrameRate(int framesPerSecond)
    {
        var spray = Spray(Definition(period: 0.11));
        Run(spray, 1, framesPerSecond);
        Assert.AreEqual(9, spray.Particles.Count);
    }

    [TestMethod]
    public void OneStepOfASecondSpawnsTheSameCountAsManySmallOnes()
    {
        var spray = Spray(Definition(period: 0.11));
        spray.Step(1, true, Origin);
        Assert.AreEqual(9, spray.Particles.Count);
    }

    [TestMethod]
    public void AStepLongerThanThePeriodSpawnsAtMostOneParticlePerTick()
    {
        var spray = Spray(Definition(period: 0.01));
        spray.Step(1, true, Origin);
        Assert.AreEqual(50, spray.Particles.Count);
    }

    [TestMethod]
    public void APeriodOfZeroSpawnsOneParticlePerTick()
    {
        var spray = Spray(Definition(period: 0));
        Run(spray, 1, 10);
        Assert.AreEqual(50, spray.Particles.Count);
    }

    [TestMethod]
    public void TheOvershootOfATickDoesNotCarryIntoThePeriod()
    {
        // A period of 0.1 s is 5 ticks, and in 32-bit float the timer then ends a hair above 0, so a spawn takes the 6th
        // tick: with no carry that is one particle per 0.12 s (about 8.3 per second), 9 in the 50 ticks of a second.
        var spray = Spray(Definition(period: 0.1));
        Run(spray, 1, 50);
        Assert.AreEqual(9, spray.Particles.Count);
    }

    [TestMethod]
    public void AParticleStartsAtTheNodeAndMovesAlongItsZAtTheVelocity()
    {
        var spray = Spray(Definition(period: 100, velocity: 8));
        var frame = new EmitterFrame(new Vector3(5, 1, 2), Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ);
        spray.Step(CombatWorld.Tick, true, frame);
        Assert.AreEqual(new Vector3(5, 1, 2), spray.Particles.Single().Position);
        Run(spray, 0.5, 10, frame: frame);
        Assert.AreEqual(5, spray.Particles.Single().Position.X, 1e-4);
        Assert.AreEqual(1, spray.Particles.Single().Position.Y, 1e-4);
        Assert.AreEqual(6, spray.Particles.Single().Position.Z, 1e-4);
    }

    [TestMethod]
    public void AParticleOfATurnedNodeStartsAtItAndMovesOnlyAlongItsZ()
    {
        var spray = Spray(Definition(period: 100, velocity: 8));
        var turned = new EmitterFrame(new Vector3(5, 1, 2), -Vector3.UnitZ, Vector3.UnitY, Vector3.UnitX);
        spray.Step(CombatWorld.Tick, true, turned);
        Run(spray, 0.5, 10, frame: turned);
        var position = spray.Particles.Single().Position;
        Assert.AreEqual(
            (9f, 1f, 2f),
            (MathF.Round(position.X, 3), MathF.Round(position.Y, 3), MathF.Round(position.Z, 3))
        );
    }

    [TestMethod]
    public void AParticleFollowsTheNodesOwnZNotTheWorldZ()
    {
        var spray = Spray(Definition(period: 100, velocity: 10));
        var turned = new EmitterFrame(Vector3.Zero, -Vector3.UnitZ, Vector3.UnitY, Vector3.UnitX);
        spray.Step(CombatWorld.Tick, true, turned);
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
        spray.Step(CombatWorld.Tick, true, Origin);
        Run(spray, 0.9, 10);
        Assert.AreEqual(1, spray.Particles.Count);
        Run(spray, 0.1, 10);
        Assert.AreEqual(0, spray.Particles.Count);
    }

    [TestMethod]
    public void AParticleGrowsByTheExpansionRatePerSecondOfAge()
    {
        var spray = Spray(Definition(period: 100, expansion: 1.1));
        spray.Step(CombatWorld.Tick, true, Origin);
        Run(spray, 0.5, 10);
        Assert.AreEqual(2 + 1.1 * 0.5, spray.Size(2, spray.Particles.Single()), 1e-6);
    }

    [TestMethod]
    public void AParticleWhoseSizeReachesZeroOrLessIsNotDrawn()
    {
        var spray = Spray(Definition(period: 100, expansion: -1));
        spray.Step(CombatWorld.Tick, true, Origin);
        Run(spray, 1.5, 10);
        Assert.IsTrue(
            SprayEmitter.Drawn(spray.Size(2, spray.Particles.Single()), spray.Size(2, spray.Particles.Single()))
        );
        Run(spray, 0.5, 10);
        Assert.IsFalse(
            SprayEmitter.Drawn(spray.Size(2, spray.Particles.Single()), spray.Size(2, spray.Particles.Single()))
        );
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
        var spray = Spray(Definition(period: 0.11));
        Run(spray, 1, 10, showing: false);
        Run(spray, 1, 10);
        Assert.AreEqual(9, spray.Particles.Count);
    }
}
