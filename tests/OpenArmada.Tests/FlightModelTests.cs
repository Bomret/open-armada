using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Sandbox;
using static OpenArmada.Tests.FlightFixtures;

namespace OpenArmada.Tests;

// The flight model's coefficients, physics step, steering, and move maneuver, with physics values built in code
// (INV-0001). A reference integrates the recovered formulas in double precision, apart from the engine's code.
[TestClass]
public class FlightModelTests
{
    const float Tick = 0.02f;
    static readonly CombatBand Band = CombatBand.Stock;

    // The keys that give the destphys coefficients of the notes (impulse speed 20).
    static readonly PhysicsKeys Destphys = PhysicsKeys.Game.With(
        ("impulseSpeed", 20),
        ("velocCoeffStill", 0.4),
        ("velocWhenCoeffHalf", 0.7),
        ("pitchRotationSpeed", 0.0667),
        ("maxPitch", 70),
        ("rollRotationSpeed", 0.5),
        ("maxRoll", 50),
        ("turnRadius", 10),
        ("frontBackInertia", 5),
        ("maxReverseSpeed", 0.667),
        ("upDownInertia", 3.75),
        ("maxVerticalSpeed", 0.3333),
        ("leftRightInertia", 3.75)
    );

    static void Near(double expected, double actual, double tolerance = 1e-3) =>
        Assert.AreEqual(expected, actual, tolerance);

    static void Near(Vector3 expected, Vector3 actual, double tolerance = 1e-3)
    {
        Near(expected.X, actual.X, tolerance);
        Near(expected.Y, actual.Y, tolerance);
        Near(expected.Z, actual.Z, tolerance);
    }

    // A ship with thrust that moves with the velocity `v` (sandbox frame) from the origin, level, facing +Z.
    static FlightModel Moving(Vector3 velocity, PhysicsValues? values = null)
    {
        var ship = Ship(new Vector3(0, 38.5f, 0), 0, values);
        ship.SetVelocity(velocity);

        return ship;
    }

    // ----- the coefficients

    [TestMethod]
    public void TheCoefficientsAreTheOnesTheGameDerivesFromTheKeys()
    {
        // the destphys values of docs/rules/flight-model.md, impulse speed 20
        var c = FlightCoefficients.Derive(Destphys);
        Near(0.02381, c.GainSlope, 1e-5);
        Near(1.6667, c.GainBase, 1e-4);
        Near(1.334, c.PitchRate, 1e-3);
        Near(0.83417, c.PitchStiffness, 1e-4);
        Near(1.01913, c.PitchControl, 1e-4);
        Near(2.2918, c.BankTurn, 1e-3);
        Near(4, c.ForwardDrag, 1e-4);
        Near(80, c.ForwardThrust, 1e-3);
        Near(53.34, c.ReverseThrust, 5e-2); // the maxReverseSpeed key here is the rounded 0.667
        Near(5.333, c.UpDownDrag, 1e-3);
        Near(35.55, c.Lift, 1e-2);
        Near(5.333, c.SideDrag, 1e-3);
    }

    [TestMethod]
    public void WithoutAnImpulseSpeedTheKeysStandUnscaledAndMaxForwardSpeedIsTheForwardSpeed()
    {
        var c = FlightCoefficients.Derive(PhysicsKeys.Game.With(("frontBackInertia", 2), ("maxForwardSpeed", 20)));
        Near(0.5, c.ForwardDrag);
        Near(10, c.ForwardThrust);
    }

    [TestMethod]
    public void TheYawServoCoefficientsDivideByMaxRollAndTheImpulseSpeed()
    {
        var keys = Destphys.With(("sy_rollVelocCoeff", 2), ("sy_rollAngleCoeff", 3), ("sp_pitchVelocCoeff", 4));
        var c = FlightCoefficients.Derive(keys);
        Near(2.0 / 50 / 20, c.YawServoRollRate, 1e-6);
        Near(3.0 / 50, c.YawServoRollAngle, 1e-6);
        Near(4.0 / 20, c.PitchServoRate, 1e-6);
    }

    // ----- the physics step, alive branch, against a reference of the recovered formulas

    // The recovered formulas in double precision, in the game's frame: a ship with thrust steered by a fixed direction.
    sealed class Reference
    {
        public double[] P =  { 0, 38.5, 0 },
            V =  { 0, 0, 0 };
        public double Pitch,
            Yaw,
            Roll,
            Wp,
            Wy,
            Wr;
        readonly double _gainA,
            _gainB,
            _pitchRate,
            _pitchK,
            _pitchGain,
            _rollRate,
            _rollK,
            _rollGain,
            _bank,
            _drag,
            _thrust,
            _reverse,
            _updown,
            _lift,
            _side,
            _yawRollRate,
            _yawRollAngle,
            _yawYawRate,
            _yawError,
            _pitchServoRate,
            _pitchServoAngle,
            _pitchServoPath,
            _impulse,
            _brakeStiffness,
            _brakeDamping,
            _turnRadius;
        double[] _start =  { 0, 0, 0 },
            _goal =  { 0, 0, 0 },
            _carrot =  { 0, 0, 0 };
        double _arc;
        bool _walked;

        public Reference(PhysicsKeys k)
        {
            var imp = k["impulseSpeed"];
            _impulse = imp;
            _brakeStiffness = k["braccelControlStiffness"];
            _brakeDamping = 2 * Math.Sqrt(_brakeStiffness);
            _turnRadius = k["turnRadius"];
            _gainB = 1 / (1 - k["velocCoeffStill"]);
            _gainA = (2 - _gainB) / (k["velocWhenCoeffHalf"] * imp);
            var vref = 1 - 1 / (_gainA * imp + _gainB);
            _pitchRate = k["pitchRotationSpeed"] * imp;
            _pitchK = 0.25 * _pitchRate * _pitchRate / vref;
            _pitchGain = _pitchK * k["maxPitch"] * Math.PI / 180;
            _rollRate = k["rollRotationSpeed"] * imp;
            _rollK = 0.25 * _rollRate * _rollRate;
            var rollMax = k["maxRoll"] * Math.PI / 180;
            _rollGain = _rollK * rollMax / vref;
            _bank = imp / (k["turnRadius"] * rollMax);
            _drag = imp / k["frontBackInertia"];
            _thrust = _drag * imp;
            _reverse = _drag * k["maxReverseSpeed"] * imp;
            _updown = imp / k["upDownInertia"];
            _lift = _updown * k["maxVerticalSpeed"] * imp;
            _side = imp / k["leftRightInertia"];
            _yawRollRate = k["sy_rollVelocCoeff"] / k["maxRoll"] / imp;
            _yawRollAngle = k["sy_rollAngleCoeff"] / k["maxRoll"];
            _yawYawRate = k["sy_yawVelocCoeff"];
            _yawError = k["sy_yawAngleCoeff"];
            _pitchServoRate = k["sp_pitchVelocCoeff"] / imp;
            _pitchServoAngle = k["sp_pitchAngleCoeff"];
            _pitchServoPath = k["sp_velocAngleCoeff"];
        }

        static double Dot(double[] a, double[] b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];

        static double Clamp(double x) => Math.Clamp(x, -1, 1);

        static double Distance(double[] a, double[] b) =>
            Math.Sqrt(Dot([a[0] - b[0], a[1] - b[1], a[2] - b[2]], [a[0] - b[0], a[1] - b[1], a[2] - b[2]]));

        double[] Forward() => [Math.Cos(Pitch) * Math.Sin(Yaw), -Math.Sin(Pitch), Math.Cos(Pitch) * Math.Cos(Yaw)];

        // A move order from where the ship stands to `goal` (game frame): the path of two points.
        public void Begin(double[] goal)
        {
            _start = (double[])P.Clone();
            _goal = goal;
            _arc = 0;
            _walked = false;
        }

        // The arrival test: near the goal and slower than half the impulse speed.
        public bool Arrived(double tooClose) => Distance(_goal, P) < tooClose && Math.Sqrt(Dot(V, V)) < 0.5 * _impulse;

        // One tick of the move order, then the physics step: the carrot walks 10 along the path (once at the start,
        // after that until it is `lead` from the ship or at the end), and the maneuver steers by it or brakes by the spring.
        public void Follow(double tooClose, double lead)
        {
            var length = Distance(_start, _goal);
            var onPath = (double s) =>
                s < length
                    ? new[] { 0, 1, 2 }.Select(i => _start[i] + (_goal[i] - _start[i]) * s / length).ToArray()
                    : _goal;
            if (!_walked)
            {
                _walked = true;
                _arc += 10;
                _carrot = onPath(_arc);
            }

            while (_arc < length && Distance(_carrot, P) < lead)
            {
                _arc += 10;
                _carrot = onPath(_arc);
            }

            // the carrot's velocity: the path's direction at the ship's speed cap, 0 at the path's end
            double[] carrotV =
                _arc < length
                    ? new[] { 0, 1, 2 }.Select(i => (_goal[i] - _start[i]) / length * _impulse).ToArray()
                    : [0, 0, 0];
            var f = Forward();
            double[] to = { _carrot[0] - P[0], _carrot[1] - P[1], _carrot[2] - P[2] };
            double Spring()
            {
                var wanted =
                    Dot(f, [carrotV[0] - V[0], carrotV[1] - V[1], carrotV[2] - V[2]]) * _brakeDamping
                    + Dot(f, to) * _brakeStiffness;
                var force = Dot(f, V) * _drag + wanted;

                return Clamp(force <= 0 ? force / _reverse : force / _thrust);
            }

            if (Distance(_goal, P) <= tooClose)
            {
                Advance(0, 0, Spring());

                return;
            }

            // inside the turning circle of the point's side: no steering
            double[] right = { f[2], 0, -f[0] };
            var rl = Math.Sqrt(right[0] * right[0] + right[2] * right[2]);
            right[0] /= rl;
            right[2] /= rl;
            var side = to[0] * right[0] + to[2] * right[2] <= 0 ? -1 : 1;
            var (dx, dz) = (to[0] - side * right[0] * _turnRadius, to[2] - side * right[2] * _turnRadius);
            if (dx * dx + dz * dz < _turnRadius * _turnRadius)
            {
                Advance(0, 0, Spring());

                return;
            }

            var distance = Math.Sqrt(Dot(to, to));
            double[] dir =
                distance < 0.001
                    ? [0, 0, 0]
                    : [to[0] / Math.Max(distance, 20), to[1] / Math.Max(distance, 20), to[2] / Math.Max(distance, 20)];
            if (P[1] > 67 && dir[1] > 0 || P[1] < 10 && dir[1] < 0)
                dir[1] = 0;
            var (ctlRoll, ctlPitch, throttle) = Steer(dir);
            var heading = Math.Sqrt(f[0] * f[0] + f[2] * f[2]);
            var toPlane = Math.Sqrt(to[0] * to[0] + to[2] * to[2]);
            if (f[0] * to[0] + f[2] * to[2] > toPlane * heading * 0.707)
                throttle = Spring();
            Advance(ctlRoll, ctlPitch, throttle);
        }

        // One tick of 0.02 s: the steering by `dir` (scale 1), then the physics step of one sub-step.
        public void Tick(double[] dir)
        {
            var (ctlRoll, ctlPitch, throttle) = Steer(dir);
            Advance(ctlRoll, ctlPitch, throttle);
        }

        (double Roll, double Pitch, double Throttle) Steer(double[] dir)
        {
            var len = Math.Max(Math.Sqrt(Dot(dir, dir)), 0.001);
            var throttle = Math.Min(len, 1);
            var thetaPitch = Math.Asin(dir[1] / len);
            var speed = Math.Sqrt(Dot(V, V));
            var path = speed >= 1e-4 ? Math.Asin(V[1] / speed) - thetaPitch : 0;
            var ctlPitch = Clamp(
                _pitchServoPath * path
                    + _pitchK / _pitchGain * thetaPitch
                    + _pitchServoRate * Wp
                    + (-Pitch - thetaPitch) * _pitchServoAngle
            );
            var e = Yaw - Math.Atan2(dir[0], dir[2]);
            if (e > Math.PI)
                e -= 2 * Math.PI;
            if (e < -Math.PI)
                e += 2 * Math.PI;
            var ctlRoll = Clamp(_yawError * e + _yawRollRate * Wr + _yawRollAngle * Roll + _yawYawRate * Wy);

            return (ctlRoll, ctlPitch, throttle);
        }

        // The physics step of one 0.02 s sub-step with the controls (the lift control is 0).
        void Advance(double ctlRoll, double ctlPitch, double throttle)
        {
            const double dt = 0.02;
            double sp = Math.Sin(Pitch),
                cp = Math.Cos(Pitch),
                sy = Math.Sin(Yaw),
                cy = Math.Cos(Yaw),
                sr = Math.Sin(Roll),
                cr = Math.Cos(Roll);
            double[] f = { cp * sy, -sp, cp * cy };
            double[] r = { cr * cy + sr * sp * sy, sr * cp, -cr * sy + sr * sp * cy };
            double[] u = { -sr * cy + cr * sp * sy, cr * cp, sr * sy + cr * sp * cy };
            var uv = Dot(u, V);
            double[] perp = { V[0] - uv * u[0], V[1] - uv * u[1], V[2] - uv * u[2] };
            var vc = 1 - 1 / (Math.Sqrt(Dot(perp, perp)) * _gainA + _gainB);
            var kp = dt * _pitchRate < 1 ? _pitchRate : 1 / dt;
            var ap = -kp * Wp - ctlPitch * _pitchGain * vc - _pitchK * vc * Pitch;
            var kr = dt * _rollRate < 1 ? _rollRate : 1 / dt;
            var ar = -kr * Wr + _rollGain * ctlRoll * vc - _rollK * Roll;
            var s = Dot(f, V);
            var force =
                (
                    throttle > 0 ? _thrust
                    : s > 0 ? _reverse
                    : 0
                ) * throttle
                - _drag * s;
            if (s + force * dt < 0)
                force = -s / dt;
            var rv = Dot(r, V);
            double[] a = new double[3];
            for (var i = 0; i < 3; i++)
            {
                a[i] = force * f[i] + (0 * _lift - uv * _updown) * u[i] - _side * rv * r[i];
            }

            var h = dt / 2;
            Wp += ap * h;
            Wr += ar * h;
            Wy = -_bank * Roll;
            Pitch += dt * Wp;
            Yaw += dt * Wy;
            Roll += dt * Wr;
            Wp += ap * h;
            Wr += ar * h;
            for (var i = 0; i < 3; i++)
                V[i] += a[i] * h;
            Pitch = Math.Clamp(Pitch, -1.5, 1.5);
            for (var i = 0; i < 3; i++)
                P[i] += dt * V[i];
            for (var i = 0; i < 3; i++)
                V[i] += a[i] * h;
        }
    }

    [TestMethod]
    public void ASteeredShipFliesTheRecoveredFormulas()
    {
        // steered along a fixed direction up and to the side of its heading, from rest, for three seconds
        var keys = Fast;
        var ship = Ship(new Vector3(0, 38.5f, 0), MathF.PI, Values(keys, keys));
        var reference = new Reference(keys) { Yaw = 0 };
        var direction = new Vector3(0.6f, 0.3f, 0.7f); // sandbox frame; the game's Z is mirrored
        var mostRoll = 0f;
        for (var i = 0; i < 150; i++)
        {
            ship.Steer(direction, 1);
            ship.Step(Tick, false, true);
            reference.Tick([0.6, 0.3, -0.7]);
            mostRoll = Math.Max(mostRoll, Math.Abs(ship.Pose.Roll));
        }

        var pose = ship.Pose;
        Near(reference.P[0], pose.Position.X, 0.02);
        Near(reference.P[1], pose.Position.Y, 0.02);
        Near(-reference.P[2], pose.Position.Z, 0.02);
        Near(reference.Pitch, pose.Pitch, 1e-3);
        Near(reference.Yaw, pose.Yaw, 1e-3);
        Near(reference.Roll, pose.Roll, 1e-3);
        Near(reference.V[0], ship.Velocity.X, 0.02);
        Assert.IsTrue(mostRoll > 0.05, "the ship banked into the turn");
        Assert.IsTrue(Math.Abs(pose.Pitch) > 0.01, "the ship pitched toward the climb");
        Assert.IsTrue(Math.Abs(pose.Yaw) > 0.1, "and turned");
    }

    // An ordered flight from `start` to `goal` against the reference maneuver: the position, roll, and pitch agree at every
    // 25th tick, and the arrival comes in the same tick. Returns the tick the order ended in, or -1.
    static int FlyTheReferenceManeuver(Vector3 start, Vector3 goal, double tooClose, int ticks)
    {
        var ship = Ship(start, MathF.PI, Values(Fast, Fast, tooClose));
        var reference = new Reference(Fast) { P = [start.X, start.Y, -start.Z], Yaw = 0 };
        ship.StartMove([ship.Pose.Position, goal]);
        reference.Begin([goal.X, goal.Y, -goal.Z]);
        for (var i = 0; i < ticks; i++)
        {
            Assert.AreEqual(reference.Arrived(tooClose), ship.HasArrived, $"arrival at tick {i}");
            if (ship.HasArrived)
                return i;
            ship.Follow(Band);
            reference.Follow(tooClose, 150);
            ship.Step(Tick, false, true);
            if (i % 25 == 0)
            {
                var pose = ship.Pose;
                Assert.AreEqual(reference.P[0], pose.Position.X, 0.05, $"x at tick {i}");
                Assert.AreEqual(reference.P[1], pose.Position.Y, 0.05, $"y at tick {i}");
                Assert.AreEqual(-reference.P[2], pose.Position.Z, 0.05, $"z at tick {i}");
                Assert.AreEqual(reference.Roll, pose.Roll, 2e-3, $"roll at tick {i}");
                Assert.AreEqual(reference.Pitch, pose.Pitch, 2e-3, $"pitch at tick {i}");
            }
        }

        return -1;
    }

    [TestMethod]
    public void AnOrderedShipFliesTheRecoveredManeuverThroughATurnAndTheApproachSpring()
    {
        // ahead and to the right and up: the carrot, a banking turn into the goal, and the spring near it
        var arrived = FlyTheReferenceManeuver(new Vector3(0, 20, 0), new Vector3(120, 38.5f, -300), 40, 4000);
        Assert.IsTrue(arrived > 0, "the order ends");
    }

    [TestMethod]
    public void AnOrderedShipTurnsAroundToAGoalBehindItLikeTheReference()
    {
        // the goal lies about 110 degrees off the heading, not straight behind, where the yaw error's wrap would flip
        var arrived = FlyTheReferenceManeuver(new Vector3(0, 38.5f, 0), new Vector3(-250, 38.5f, 100), 40, 6000);
        Assert.IsTrue(arrived > 0, "the order ends");
    }

    [TestMethod]
    public void AnOrderedShipWithAGoalInsideTheTurningCircleRestsLikeTheReference()
    {
        // tooCloseToTurn 2 is under the turning radius 7.5: the circle branch brakes the ship toward its projection
        var arrived = FlyTheReferenceManeuver(new Vector3(0, 38.5f, 0), new Vector3(5, 38.5f, -2), 2, 1500);
        Assert.AreEqual(-1, arrived, "the order stays open");
    }

    [TestMethod]
    public void AShipThatBanksTurnsOnlyThroughItsBank()
    {
        // without a roll control the yaw rate stays 0: the yaw rate is the bank's alone (0x459440)
        var ship = Moving(new Vector3(0, 0, -10));
        ship.Step(Tick, false, true);
        Assert.AreEqual(0, ship.Rates.Yaw);
    }

    [TestMethod]
    public void TheYawRateIsMinusTheBankGainTimesTheRollAngle()
    {
        var ship = Ship(new Vector3(0, 38.5f, 0), MathF.PI, Values(Fast, Fast));
        for (var i = 0; i < 10; i++)
        {
            ship.Steer(new Vector3(1, 0, 0), 1); // toward +X: a roll control
            ship.Step(Tick, false, true);
        }

        var rollBefore = ship.Pose.Roll;
        ship.Steer(new Vector3(1, 0, 0), 1);
        ship.Step(Tick, false, true);
        Assert.IsTrue(rollBefore < 0, "the roll leans the ship into a turn toward +X");
        Near(-ship.Coefficients.BankTurn * rollBefore, ship.Rates.Yaw, 1e-5); // the roll the step began with
    }

    [TestMethod]
    public void TheForwardSpeedApproachesTheThrottleTimesTheImpulseSpeed()
    {
        // ds/dt = drag * (maxFwd * throttle - s): at throttle 1 the speed climbs to the impulse speed
        var ship = Ship(new Vector3(0, 38.5f, 0), MathF.PI, Values(Fast, Fast));
        for (var i = 0; i < 1500; i++)
        {
            ship.Steer(new Vector3(0, 0, -1), 1); // dead ahead in the game's frame is -Z of the sandbox at yaw 0
            ship.Step(Tick, false, true);
        }

        Near(20, ship.Speed, 0.05);
    }

    [TestMethod]
    public void TheTerrainCapLimitsTheForwardSpeedAndTheStepResetsIt()
    {
        var ship = Ship(new Vector3(0, 38.5f, 0), MathF.PI, Values(Fast, Fast));
        for (var i = 0; i < 1500; i++)
        {
            ship.Steer(new Vector3(0, 0, -1), 1);
            ship.CapSpeed(0.5f, 6);
            ship.Step(Tick, false, true);
        }

        Near(6, ship.Speed, 0.05);
        for (var i = 0; i < 1500; i++)
        {
            ship.Steer(new Vector3(0, 0, -1), 1);
            ship.Step(Tick, false, true);
        }

        Near(20, ship.Speed, 0.05);
    }

    [TestMethod]
    public void AVelocityComponentAbove120ResetsTheVelocity()
    {
        // the step's own drag takes a few units off before the test, so the velocity starts well above 120
        var ship = Moving(new Vector3(0, 0, 160));
        ship.Step(Tick, false, true);
        Assert.AreEqual(Vector3.Zero, ship.Velocity);
        Assert.AreEqual(0, ship.Speed);
    }

    [TestMethod]
    public void ALargeStepRunsInSubStepsOfTheGamesRule()
    {
        // speed 20 along the bow: h = max(0.05, min(0.1 / 0, 4 / 20)) = 0.2, so 0.5 s runs 3 sub-steps of 1/6 s
        var one = Ship(new Vector3(0, 38.5f, 0), MathF.PI);
        one.SetVelocity(new Vector3(0, 0, -20));
        one.Steer(new Vector3(0, 0, -1), 1);
        var many = Ship(new Vector3(0, 38.5f, 0), MathF.PI);
        many.SetVelocity(new Vector3(0, 0, -20));
        many.Steer(new Vector3(0, 0, -1), 1);
        one.Step(0.5, false, true);
        for (var i = 0; i < 3; i++)
        {
            many.Step(0.5 / 3, false, true);
        }

        Near(many.Pose.Position, one.Pose.Position, 1e-4);
        Near(many.Velocity, one.Velocity, 1e-4);
    }

    [TestMethod]
    public void ThePitchClampHoldsTheAngleWithinOnePointFive()
    {
        // a pitch rate comes from the controls: the steering direction is straight up, then the ship loses its thrust
        var spinning = Moving(Vector3.Zero);
        for (var i = 0; i < 50; i++)
        {
            spinning.Steer(new Vector3(0, 1, -0.01f), 1);
            spinning.Step(Tick, false, true);
        }

        var wasPitch = spinning.Rates.Pitch;
        Assert.IsTrue(Math.Abs(wasPitch) > 0.01f, "the ship pitches");
        for (var i = 0; i < 2000; i++)
        {
            spinning.Step(Tick, false, false); // no restoring term: the rate stays and the angle runs into the clamp
            Assert.IsTrue(Math.Abs(spinning.Pose.Pitch) <= 1.5f);
        }

        Assert.AreEqual(1.5f, Math.Abs(spinning.Pose.Pitch));
        Near(wasPitch, spinning.Rates.Pitch, 1e-5);
    }

    // ----- the no-thrust branch

    [TestMethod]
    public void AShipWithoutThrustBrakesAtTwoUntilItsSpeedIsBelowATenth()
    {
        var ship = Moving(new Vector3(0, 0, -10));
        for (var i = 0; i < 100; i++)
        {
            ship.Step(Tick, false, false); // 2 seconds
        }

        Near(6, ship.Speed, 1e-3);
        for (var i = 0; i < 400; i++)
        {
            ship.Step(Tick, false, false);
        }

        Assert.IsTrue(ship.Speed < 0.1f, "the brake runs until the speed is below 0.1");
        Assert.IsTrue(ship.Speed > 0.05f, "and no further: the residual speed stays");
        var rest = ship.Speed;
        ship.Step(Tick, false, false);
        Assert.AreEqual(rest, ship.Speed);
    }

    [TestMethod]
    public void AShipWithoutThrustBrakesAlongItsOwnVelocityAndKeepsItsStraightLine()
    {
        var ship = Moving(new Vector3(6, 0, 8)); // speed 10
        ship.Step(1, false, false);
        Near(new Vector3(6, 0, 8) * 0.8f, ship.Velocity, 1e-3);
        Near(new Vector3(6, 0, 8) * 0.9f, ship.Pose.Position - new Vector3(0, 38.5f, 0), 1e-3);
    }

    [TestMethod]
    public void AShipWithoutThrustKeepsItsRatesUndampedAndRollsOver()
    {
        var ship = Ship(new Vector3(0, 38.5f, 0), 0, Values(Fast, Fast));
        // a roll rate from the controls, then the engines go
        for (var i = 0; i < 30; i++)
        {
            ship.Steer(new Vector3(1, 0, 0.3f), 1);
            ship.Step(Tick, false, true);
        }

        var rate = ship.Rates.Roll;
        Assert.IsTrue(Math.Abs(rate) > 0.01f);
        var rolled = 0f;
        var last = ship.Pose.Roll;
        for (var i = 0; i < 1000; i++)
        {
            ship.Step(Tick, true, false); // dying, engines gone: no bank, no damping
            var roll = ship.Pose.Roll;
            if (Math.Abs(roll - last) > 3)
                rolled++;
            last = roll;
        }

        Near(rate, ship.Rates.Roll, 1e-5);
        Assert.IsTrue(rolled >= 1, "the roll wrapped past ±π: the ship rolled over");
    }

    [TestMethod]
    public void ADyingShipWithWorkingEnginesStillTurnsFromItsBank()
    {
        var ship = Ship(new Vector3(0, 38.5f, 0), 0, Values(Fast, Fast));
        ship.SetAngles(0, ship.Pose.Yaw, 0.3f);
        ship.Step(Tick, true, true); // dying, engines on
        Near(-ship.Coefficients.BankTurn * 0.3f, ship.Rates.Yaw, 1e-3);
        var without = Ship(new Vector3(0, 38.5f, 0), 0, Values(Fast, Fast));
        without.SetAngles(0, without.Pose.Yaw, 0.3f);
        without.Step(Tick, true, false); // engines gone
        Assert.AreEqual(0, without.Rates.Yaw);
    }

    [TestMethod]
    public void AShipThatRegainsThrustKeepsItsVelocityAndRates()
    {
        // a ship coasting along its bow at 5, with a roll rate it had
        var ship = Ship(new Vector3(0, 38.5f, 0), MathF.PI);
        ship.SetVelocity(new Vector3(0, 0, -5));
        for (var i = 0; i < 10; i++)
        {
            ship.Steer(new Vector3(1, 0, -1), 1);
            ship.Step(Tick, false, true);
        }

        for (var i = 0; i < 10; i++)
        {
            ship.Step(Tick, false, false);
        }

        var (velocity, rates) = (ship.Velocity, ship.Rates);
        // the step with thrust back starts from the velocity and rates the coast left: nothing is reset
        ship.Step(Tick, false, true);
        Assert.IsTrue(ship.Speed > velocity.Length() * 0.9f, $"{ship.Speed} from {velocity.Length()}");
        Assert.IsTrue(Vector3.Dot(Vector3.Normalize(ship.Velocity), Vector3.Normalize(velocity)) > 0.99f);
        Assert.IsTrue(Math.Abs(rates.Roll) > 1e-4f && Math.Abs(ship.Rates.Roll - rates.Roll) < Math.Abs(rates.Roll));
    }

    [TestMethod]
    public void AShipMovingBackwardAlongItsBowIsStoppedThereWithinOneSubStep()
    {
        // the zero-crossing guard of the forward force (0x459440): the forward speed never crosses zero in a sub-step, so
        // a velocity backward along the bow is taken to exactly 0 in one tick
        var ship = Ship(new Vector3(0, 38.5f, 0), MathF.PI);
        ship.SetVelocity(new Vector3(0, 0, 4)); // astern
        ship.Step(Tick, false, true);
        Assert.AreEqual(0, ship.Velocity.Z, 1e-4);
    }

    // ----- the steering

    [TestMethod]
    public void TheSteeringDirectionIsThePointOverAtLeastTwenty()
    {
        var ship = Ship(new Vector3(0, 38.5f, 0));
        Near(new Vector3(0.5f, 0, 0), ship.SteeringDirection(new Vector3(10, 38.5f, 0), Band)); // 10 / 20
        Near(new Vector3(1, 0, 0), ship.SteeringDirection(new Vector3(100, 38.5f, 0), Band)); // 100 / 100
        Near(new Vector3(0, 0, -1), ship.SteeringDirection(new Vector3(0, 38.5f, -30), Band)); // 30 / 30
    }

    [TestMethod]
    public void ThePointWithinOneThousandthGivesNoDirection()
    {
        var ship = Ship(new Vector3(0, 38.5f, 0));
        Assert.AreEqual(Vector3.Zero, ship.SteeringDirection(new Vector3(0.0005f, 38.5f, 0), Band));
        Assert.AreEqual(Vector3.Zero, ship.SteeringDirection(new Vector3(0, 38.5f, 0), Band));
    }

    [TestMethod]
    public void ATooCloseShipsDirectionIsNotNormalizedAndItsThrottleFallsOff()
    {
        // a point 10 away gives |direction| 0.5, so the throttle is 0.5: half the thrust
        var ship = Ship(new Vector3(0, 38.5f, 0), MathF.PI);
        ship.Steer(ship.SteeringDirection(new Vector3(0, 38.5f, -10), Band), 1);
        Assert.AreEqual(0.5f, ship.Controls.Throttle, 1e-6f);
        Assert.AreEqual(0, ship.Controls.Lift);
    }

    [TestMethod]
    public void AShipAboveTheCeilingNeverClimbs()
    {
        var ship = Ship(new Vector3(0, 70, 0));
        Assert.AreEqual(0, ship.SteeringDirection(new Vector3(0, 200, -100), Band).Y); // rising: zeroed
        Assert.IsTrue(ship.SteeringDirection(new Vector3(0, 10, -100), Band).Y < 0); // sinking: kept
    }

    [TestMethod]
    public void AShipBelowTheFloorNeverSinks()
    {
        var ship = Ship(new Vector3(0, 5, 0));
        Assert.AreEqual(0, ship.SteeringDirection(new Vector3(0, -50, -100), Band).Y); // sinking: zeroed
        Assert.IsTrue(ship.SteeringDirection(new Vector3(0, 40, -100), Band).Y > 0); // rising: kept
    }

    [TestMethod]
    public void TheClampReadsTheBandItIsGiven()
    {
        var ship = Ship(new Vector3(0, 70, 0));
        var higher = new CombatBand(10, 90);
        Assert.IsTrue(ship.SteeringDirection(new Vector3(0, 200, -100), higher).Y > 0);
    }

    [TestMethod]
    public void TheAvoidanceSeamRewritesTheDirectionBeforeTheHeightClamp()
    {
        var ship = Ship(new Vector3(0, 70, 0));
        ship.Avoidance = (_, _, direction) => direction with { X = 0.25f, Y = 0.5f };
        var direction = ship.SteeringDirection(new Vector3(0, 70, -100), Band);
        Assert.AreEqual(0.25f, direction.X);
        Assert.AreEqual(0, direction.Y); // above the ceiling and rising
    }

    [TestMethod]
    public void TheAvoidanceSeamGetsTheOrdersGoalAsTheGameDoesNotTheCarrot()
    {
        // 0x42ebb0 hands 0x42ef70 the goal that 0x42e4a0 passed in, while the direction it rewrites points at the carrot
        var ship = Ship(new Vector3(0, 38.5f, 0), MathF.PI);
        var goal = new Vector3(0, 38.5f, -1000);
        Vector3? given = null;
        ship.Avoidance = (_, point, direction) =>
        {
            given = point;

            return direction;
        };
        ship.StartMove([ship.Pose.Position, goal]);
        ship.Follow(Band);
        Assert.AreEqual(goal, given);
    }

    [TestMethod]
    public void AShipSteersTowardItsPointClimbingAndBanking()
    {
        var ship = Ship(new Vector3(0, 20, 0), MathF.PI);
        var goal = new Vector3(60, 60, -80); // to the right and up
        var leastRoll = 0f;
        for (var i = 0; i < 100; i++)
        {
            ship.Steer(ship.SteeringDirection(goal, Band), 1);
            ship.Step(Tick, false, true);
            leastRoll = Math.Min(leastRoll, ship.Pose.Roll);
        }

        var pose = ship.Pose;
        Assert.IsTrue(pose.Position.Y > 20.5f, "it climbs toward the point");
        Assert.IsTrue(pose.Pitch < 0, "its nose is raised (a positive pitch lowers it)");
        Assert.IsTrue(pose.Position.X > 0.5f, "it turns to the +X side");
        Assert.IsTrue(leastRoll < -0.1f, "it banks into the turn (a negative roll lowers the right side)");
    }

    // ----- the move maneuver and the move order

    // Flies `ship` along a move order to `goal` for up to `ticks`, with the arrival test; true when it arrived.
    static bool Fly(FlightModel ship, Vector3 goal, int ticks, Action<int>? each = null)
    {
        ship.StartMove([ship.Pose.Position, goal]);
        for (var i = 0; i < ticks; i++)
        {
            if (ship.HasArrived)
            {
                ship.EndMove();

                return true;
            }

            ship.Follow(Band);
            ship.Step(Tick, false, true);
            each?.Invoke(i);
        }

        return false;
    }

    [TestMethod]
    public void AShipOrderedToAPointAcceleratesBanksClimbsAndEndsItsOrderNearItSlow()
    {
        var ship = Ship(new Vector3(0, 20, 0), MathF.PI);
        var goal = new Vector3(120, 38.5f, -300);
        float maxRoll = 0,
            maxSpeed = 0;
        var arrived = Fly(
            ship,
            goal,
            4000,
            _ =>
            {
                maxRoll = Math.Max(maxRoll, Math.Abs(ship.Pose.Roll));
                maxSpeed = Math.Max(maxSpeed, ship.Speed);
            }
        );
        Assert.IsTrue(arrived);
        Assert.IsTrue(maxRoll > 0.1f, "it banked into its turn");
        Assert.IsTrue(maxSpeed > 10, "it accelerated");
        Assert.IsTrue(Vector3.Distance(ship.Pose.Position, goal) < 40, "the order ends inside tooCloseToTurn");
        Assert.IsTrue(ship.Speed < 10, "and below half the impulse speed");
        Assert.IsTrue(Math.Abs(ship.Pose.Position.Y - 38.5f) < 15, "it climbed toward the combat plane");
    }

    [TestMethod]
    [DataRow(0.6, 0.5)]
    [DataRow(0.8, 0.5)]
    [DataRow(0.95, 0.3)]
    public void AStillCoefficientBetweenHalfAndOneKeepsTheFlightFinite(double still, double half)
    {
        // the gain's slope is negative there: its denominator crosses zero at a speed the ship can reach
        var keys = Fast.With(("velocCoeffStill", still), ("velocWhenCoeffHalf", half));
        var ship = Ship(new Vector3(0, 38.5f, 0), MathF.PI, Values(keys, keys));
        Fly(ship, new Vector3(0, 38.5f, -3000), 3000);
        var pose = ship.Pose;
        Assert.IsTrue(float.IsFinite(pose.Position.X + pose.Position.Y + pose.Position.Z + pose.Yaw + pose.Roll));
    }

    [TestMethod]
    public void AShipFliesTheOrderToItsGoalAlongTheCarrot()
    {
        // the first thing the order does is steer at the carrot, `pathLeadDistance` ahead along the path
        var ship = Ship(new Vector3(0, 38.5f, 0), MathF.PI);
        ship.StartMove([ship.Pose.Position, new Vector3(0, 38.5f, -1000)]);
        ship.Follow(Band);
        // the carrot stands 150 ahead: dir = 150 / 150 = 1, so the throttle is full
        Assert.AreEqual(1f, ship.Controls.Throttle, 1e-3f);
    }

    [TestMethod]
    public void ANearGoalHoldsTheShipByTheSpringWithNoSteering()
    {
        // inside tooCloseToTurn of the goal: roll, pitch, and lift are 0 and the throttle brakes the ship
        var ship = Moving(new Vector3(0, 0, -10), Values(Fast, Fast));
        ship.StartMove([ship.Pose.Position, new Vector3(0, 38.5f, -30)]);
        ship.Steer(new Vector3(1, 1, 0), 1); // some controls to clear
        ship.Follow(Band);
        var (roll, pitch, lift, throttle) = ship.Controls;
        Assert.AreEqual(0, roll);
        Assert.AreEqual(0, pitch);
        Assert.AreEqual(0, lift);
        Assert.IsTrue(throttle < 0, "the spring brakes the ship: its throttle is reverse");
    }

    [TestMethod]
    public void TheArrivalTestNeedsBothTheDistanceAndTheSlowness()
    {
        var goal = new Vector3(0, 38.5f, -30);
        var fast = Moving(new Vector3(0, 0, -15)); // 15 > 0.5 * 20
        fast.StartMove([fast.Pose.Position, goal]);
        Assert.IsFalse(fast.HasArrived);
        var slow = Moving(new Vector3(0, 0, -9.9f));
        slow.StartMove([slow.Pose.Position, goal]);
        Assert.IsTrue(slow.HasArrived);
        var far = Moving(Vector3.Zero);
        far.StartMove([far.Pose.Position, new Vector3(0, 38.5f, -41)]);
        Assert.IsFalse(far.HasArrived, "40 or more away is not near");
        Assert.IsFalse(Ship(Vector3.Zero).HasArrived, "no order, no arrival");
    }

    [TestMethod]
    public void AGoalInsideTheTurningCircleLeavesTheOrderOpenWithAFiniteSteadyPose()
    {
        // tooCloseToTurn 2 is under the turning radius 7.5, so a point 5 to the side and just ahead is inside the circle
        // and not near: the ship brakes toward its projection and never loops
        var ship = Ship(new Vector3(0, 38.5f, 0), MathF.PI, Values(Fast, Fast, tooClose: 2));
        var goal = new Vector3(5, 38.5f, -2);
        var arrived = Fly(ship, goal, 3000);
        var pose = ship.Pose;
        Assert.IsTrue(float.IsFinite(pose.Position.X + pose.Position.Y + pose.Position.Z + pose.Yaw + pose.Roll));
        Assert.IsFalse(arrived, "the goal cannot be reached by the game's rules, so the order stays open");
        Assert.IsTrue(ship.Speed < 0.5f, "the ship is at rest, not circling");
        Assert.IsTrue(Math.Abs(pose.Roll) < 0.01f);
        Assert.IsTrue(Vector3.Distance(pose.Position, new Vector3(0, 38.5f, 0)) < 8, "it did not wander off");
        Assert.AreEqual(-2, pose.Position.Z, 0.01, "it rests at the goal's projection on its heading");
    }

    [TestMethod]
    public void AGoalAtTheShipLeavesAFinitePose()
    {
        var ship = Ship(new Vector3(0, 38.5f, 0));
        ship.StartMove([ship.Pose.Position, ship.Pose.Position]);
        for (var i = 0; i < 100; i++)
        {
            ship.Follow(Band);
            ship.Step(Tick, false, true);
        }

        Assert.IsTrue(float.IsFinite(ship.Pose.Position.X + ship.Pose.Yaw + ship.Pose.Roll));
        Assert.IsTrue(ship.HasArrived);
    }

    // ----- the hold

    [TestMethod]
    public void AHeldShipBrakesToRestAndLevelsOut()
    {
        var ship = Moving(new Vector3(0, 0, -15));
        ship.SetAngles(0.4f, ship.Pose.Yaw, 0.3f);
        for (var i = 0; i < 1500; i++)
        {
            ship.Brake(Band);
            ship.Step(Tick, false, true);
        }

        Assert.IsTrue(ship.Speed < 0.1f, "it brakes to rest");
        Assert.IsTrue(Math.Abs(ship.Pose.Pitch) < 0.01f && Math.Abs(ship.Pose.Roll) < 0.01f, "and levels out");
        Assert.AreEqual(0, ship.Controls.Roll);
        Assert.AreEqual(0, ship.Controls.Pitch);
        Assert.AreEqual(0, ship.Controls.Lift);
    }

    [TestMethod]
    public void AShipApproachingAFarPointFliesToItAndStopsNearIt()
    {
        var ship = Ship(new Vector3(0, 38.5f, 0), MathF.PI);
        var point = new Vector3(0, 38.5f, -200);
        for (var i = 0; i < 3000; i++)
        {
            ship.Approach(point, Band);
            ship.Step(Tick, false, true);
        }

        Assert.IsTrue(Vector3.Distance(ship.Pose.Position, point) < 40);
        Assert.IsTrue(ship.Speed < 10);
    }

    // ----- the swap to fastphys and the seams

    [TestMethod]
    public void AShipTakesFastphysCoefficientsAtItsFirstMoveAndKeepsThem()
    {
        var values = Values(Own, Fast);
        var ship = Ship(Vector3.Zero, 0, values);
        Near(20, ship.Coefficients.TurnRadius, 1e-6);
        ship.StartMove([Vector3.Zero, new Vector3(0, 0, 100)]);
        Near(7.5, ship.Coefficients.TurnRadius, 1e-6);
        ship.EndMove();
        Near(7.5, ship.Coefficients.TurnRadius, 1e-6);
        Assert.AreEqual(values.ImpulseSpeed, ship.ImpulseSpeed, "the speed stays the ship's own");
        Assert.AreEqual(40, ship.TooCloseToTurn, "and so do the class distances");
    }

    [TestMethod]
    public void TheTurningCircleReadsTheActiveCoefficientsRadius()
    {
        // a point 18 to the right of a ship facing +Z (the right side is -X in the sandbox): inside the turning circle of
        // radius 20 (own) and outside the one of radius 7.5 (fastphys), which reaches 15
        var point = new Vector3(-18, 0, 0.5f);
        var own = Ship(Vector3.Zero, 0, Values(Own, null, tooClose: 1) with { Fast = null });
        own.Approach(point, Band);
        Assert.AreEqual(0, own.Controls.Roll, "inside its own circle the ship does not steer");
        var swapped = Ship(Vector3.Zero, 0, Values(Own, Fast, tooClose: 1));
        swapped.Approach(point, Band); // the swap happens at the first approach, before the circle test
        Assert.AreNotEqual(0, swapped.Controls.Roll, "outside the swapped radius 7.5 the ship steers");
    }

    [TestMethod]
    public void AShipWithoutFastphysKeepsItsOwnCoefficients()
    {
        var values = PhysicsValues.From(Own);
        var ship = Ship(Vector3.Zero, 0, values);
        ship.StartMove([Vector3.Zero, new Vector3(0, 0, 100)]);
        Near(20, ship.Coefficients.TurnRadius, 1e-6);
    }

    [TestMethod]
    public void TheCollisionTurnWritesAllThreeAnglesAndKeepsTheRates()
    {
        var ship = Moving(new Vector3(0, 0, -10));
        ship.SetAngles(0, 0.5f, 0.2f);
        ship.Step(Tick, true, false);
        var rates = ship.Rates;
        var before = ship.Pose;
        ship.SetAngles(0.15f, 0.4f, -0.1f);
        Assert.AreEqual((0.15f, 0.4f, -0.1f), (ship.Pose.Pitch, ship.Pose.Yaw, ship.Pose.Roll));
        Assert.AreEqual(rates, ship.Rates);
        Assert.AreEqual(before.Position, ship.Pose.Position);
    }

    [TestMethod]
    public void ADrawnPoseAdvancesByTheVelocityAndTheRates()
    {
        var ship = Moving(new Vector3(10, 0, 0));
        var drawn = ship.Drawn(0.01);
        Near(ship.Pose.Position + new Vector3(0.1f, 0, 0), drawn.Position, 1e-5);
        Assert.AreEqual(ship.Pose.Position, ship.Pose.Position);
    }

    [TestMethod]
    public void AStepThatLeavesANonFiniteStateRestoresTheShipAndThrows()
    {
        var ship = Moving(new Vector3(0, 0, -10));
        ship.SetVelocity(new Vector3(float.NaN, 0, 0));
        Assert.ThrowsExactly<InvalidOperationException>(() => ship.Step(Tick, false, true));
        Assert.IsTrue(float.IsFinite(ship.Pose.Position.X));
        Assert.AreEqual(Vector3.Zero, ship.Velocity);
    }

    // Steps `ship` for `ticks` ticks of `control`; a step that reports a fault restores the ship, and the pose stays finite.
    static int StepThrough(FlightModel ship, int ticks, Action<FlightModel> control)
    {
        var faults = 0;
        for (var i = 0; i < ticks; i++)
        {
            control(ship);
            try
            {
                ship.Step(Tick, false, true);
            }
            catch (InvalidOperationException)
            {
                faults++;
            }

            var pose = ship.Pose;
            Assert.IsTrue(float.IsFinite(pose.Position.X + pose.Position.Y + pose.Position.Z + pose.Yaw + pose.Roll));
        }

        return faults;
    }

    [TestMethod]
    [Timeout(20000)]
    [DataRow("turnRadius")]
    [DataRow("maxRoll")]
    public void AnAbsurdBankTurnGivesAFinitePoseNotAHang(string key)
    {
        var keys = Own.With((key, 1e-30));
        var ship = Ship(new Vector3(0, 38.5f, 0), 0, Values(keys, keys));
        ship.StartMove([ship.Pose.Position, new Vector3(300, 38.5f, 300)]);
        StepThrough(ship, 200, s => s.Follow(Band));
    }

    [TestMethod]
    [Timeout(20000)]
    [DataRow(1000.0)]
    [DataRow(1e5)]
    public void AnUnstableRollIntegrationGivesAFinitePoseNotAHang(double rollRotationSpeed)
    {
        var keys = Fast.With(("impulseSpeed", 30), ("rollRotationSpeed", rollRotationSpeed));
        var ship = Ship(new Vector3(0, 38.5f, 0), 0, Values(keys, keys));
        ship.SetAngles(0, 0, 0.3f);
        StepThrough(ship, 400, s => s.Brake(Band));
    }

    [TestMethod]
    [Timeout(20000)]
    [DataRow(-1e6)]
    [DataRow(-1e12)]
    public void AVelocCoeffStillFarBelowZeroGivesAFinitePoseNotAHang(double velocCoeffStill)
    {
        var keys = Fast.With(("velocCoeffStill", velocCoeffStill));
        var ship = Ship(new Vector3(0, 38.5f, 0), 0, Values(keys, keys));
        ship.StartMove([ship.Pose.Position, new Vector3(300, 38.5f, 300)]);
        StepThrough(ship, 400, s => s.Follow(Band));
    }

    [TestMethod]
    public void AGainDenominatorThatReachesZeroBetween120AndTheResetsLargestSpeedIsUnusable()
    {
        // gain denominator 1 / (1 - 0.7) + slope x speed reaches 0 at 2.5 x velocWhenCoeffHalf x impulseSpeed = 160
        var keys = Fast.With(("velocCoeffStill", 0.7), ("velocWhenCoeffHalf", 3.2));
        var c = FlightCoefficients.Derive(keys);
        Near(0, c.GainBase + c.GainSlope * 160, 1e-3);
        Assert.IsTrue(c.GainBase + c.GainSlope * FlightModel.VelocityLimit > 0);
        Assert.IsFalse(c.IsUsable);
    }

    [TestMethod]
    public void AMovePathWalksTheCarrotAlongItsPoints()
    {
        var path = new MovePath([Vector3.Zero, new Vector3(0, 0, 100)]);
        Assert.AreEqual(new Vector3(0, 0, 100), path.Goal);
        Assert.ThrowsExactly<ArgumentException>(() => new MovePath([]));
    }

    static List<float> Speeds(FlightModel ship, int ticks)
    {
        var speeds = new List<float>();
        for (var i = 0; i < ticks; i++)
        {
            ship.Follow(Band);
            ship.Step(Tick, false, true);
            speeds.Add(ship.Speed);
        }

        return speeds;
    }

    // ----- the Borg branch (0x459a99) and the Borg path of the steering (0x42f740) and the move maneuver (0x42e4a0)

    // The stock Borg physics definition's yaw and distance keys (bbatphys.odf) on fastphys's coefficients, which the ship
    // flies by once it has moved: tooCloseToTurn 50, tooSlowToTurn 5, rotationAcceleration 2, maxRotationSpeed 0.75,
    // rotationStiffness 1.
    static PhysicsValues BorgValues(PhysicsKeys? keys = null) =>
        Values(keys ?? Fast, keys ?? Fast, 50) with
        {
            BorgPhysics = true,
            TooSlowToTurn = 5,
            RotationAcceleration = 2,
            MaxRotationSpeed = 0.75,
            RotationStiffness = 1,
        };

    // The brake spring's damping of fastphys's braccelControlStiffness 1.5: 2 x sqrt(1.5).
    static readonly double SpringDamping = 2 * Math.Sqrt(1.5);

    // A Borg ship at `at` with the game's yaw 0 (its bow toward the sandbox's -Z), level.
    static FlightModel BorgShip(Vector3 at, float yaw = 0, PhysicsKeys? keys = null) =>
        new(
            BorgValues(keys),
            new ShipPose
            {
                Position = at,
                Pitch = 0,
                Yaw = yaw,
                Roll = 0,
            }
        );

    // The sandbox point 300 units from the origin at the game's bearing `bearing` (0 is the game's +Z).
    static Vector3 AtBearing(float bearing) => new(300 * MathF.Sin(bearing), 38.5f, -300 * MathF.Cos(bearing));

    [TestMethod]
    public void ABorgShipSteeredAlongADirectionAcceleratesToThe3DVelocityCommand()
    {
        // from rest the command u = 2sqrt(k) x vd / c6fc gives the acceleration 2sqrt(k) x vd along the direction, whatever
        // the ship faces: vd is the direction's unit vector times the speed cap 20, the direction's Y included
        var ship = BorgShip(new Vector3(0, 38.5f, 0));
        var direction = new Vector3(0.6f, 0.8f, 0);
        ship.Steer(direction, 1);
        ship.Step(Tick, false, true);
        Near(direction * (float)(SpringDamping * 20 * Tick), ship.Velocity, 1e-4);
        Assert.AreEqual(0f, ship.Pose.Pitch);
        Assert.AreEqual(0f, ship.Pose.Roll);
    }

    [TestMethod]
    public void ABorgShipComputesItsAccelerationOnTheFirstSubStepOnly()
    {
        // at speed 10 the step splits in two sub-steps of 0.25: the second reuses the first's acceleration
        // 2sqrt(k) x (20 - 10) along the direction, so the speed gains it over the whole 0.5 s
        var ship = BorgShip(new Vector3(0, 38.5f, 0));
        var direction = Vector3.Normalize(new Vector3(0.6f, 0.8f, 0));
        ship.SetVelocity(direction * 10);
        ship.Steer(direction, 1);
        ship.Step(0.5, false, true);
        Near(10 + SpringDamping * 10 * 0.5, ship.Speed, 1e-3);
    }

    [TestMethod]
    public void ABorgShipTakesNoTerrainOrNebulaCap()
    {
        var ship = BorgShip(new Vector3(0, 38.5f, 0));
        var direction = new Vector3(0, 0, -1);
        ship.CapSpeed(0.1f, 0.5f);
        ship.Steer(direction, 1);
        ship.Step(Tick, false, true);
        Near(direction * (float)(SpringDamping * 20 * Tick), ship.Velocity, 1e-4);
    }

    [TestMethod]
    public void ABorgShipTurnsItsYawByTheSpringTowardTheBearingPlusItsFacingOffset()
    {
        // a goal dead ahead and an offset of 90 degrees: the yaw error is -pi/2, the spring saturates, and the yaw rate
        // gains rotationAcceleration x maxRotationSpeed = 1.5 per second; the ship does not bank
        var ship = BorgShip(new Vector3(0, 38.5f, 0));
        ship.FacingOffset = MathF.PI / 2;
        ship.StartMove([ship.Pose.Position, AtBearing(0)]);
        ship.Follow(Band);
        Near(MathF.PI / 2, ship.HeadingTarget, 1e-5);
        ship.Step(Tick, false, true);
        Near(1.5 * Tick, ship.Rates.Yaw, 1e-6);
        Near(1.5 * Tick * Tick / 2, ship.Pose.Yaw, 1e-6);
        Assert.AreEqual(0f, ship.Pose.Roll);
    }

    [TestMethod]
    public void TheBorgYawSpringIsCriticallyDampedBelowSaturation()
    {
        // an error of -0.1 and no rate: the acceleration is -K x e = 0.1
        var ship = BorgShip(new Vector3(0, 38.5f, 0));
        ship.StartMove([ship.Pose.Position, AtBearing(0.1f)]);
        ship.Follow(Band);
        ship.Step(Tick, false, true);
        Near(0.1 * Tick, ship.Rates.Yaw, 1e-5);
    }

    [TestMethod]
    public void TheBorgYawErrorWrapsOnceEachWay()
    {
        // yaw 3.0 and target -3.0 differ by 6.0: the wrapped error is -0.2832, so the ship turns on through the half circle
        var ship = BorgShip(new Vector3(0, 38.5f, 0), 3.0f);
        ship.HeadingTarget = -3.0f;
        ship.Step(Tick, false, true);
        Near((6.0 - 2 * Math.PI) * -1 * Tick, ship.Rates.Yaw, 1e-5);
    }

    [TestMethod]
    public void TheFacingOffsetWrapsAboveHalfACircleOnly()
    {
        // the bearing 2.5 plus 270 degrees is 7.2124, which wraps once to 0.9292
        var ship = BorgShip(new Vector3(0, 38.5f, 0));
        ship.FacingOffset = 3 * MathF.PI / 2;
        ship.StartMove([ship.Pose.Position, AtBearing(2.5f)]);
        ship.Follow(Band);
        Near(2.5 + 3 * Math.PI / 2 - 2 * Math.PI, ship.HeadingTarget, 1e-4);
    }

    [TestMethod]
    public void EveryPhysicsStepLeavesTheHeadingTargetAtTheYaw()
    {
        // the matrix setter's decomposition writes the target after each integration (0x4718e0, 0x458ea0)
        foreach (var ship in new[] { BorgShip(Vector3.Zero), Ship(Vector3.Zero) })
        {
            ship.HeadingTarget = 1;
            ship.Step(Tick, false, true);
            Assert.AreEqual(ship.Pose.Yaw, ship.HeadingTarget);
        }
    }

    [TestMethod]
    public void ABorgShipOrderedToAPointFliesItIn3DWithoutPitchOrRoll()
    {
        var ship = BorgShip(new Vector3(0, 38.5f, 0));
        var goal = new Vector3(200, 66, -200);
        ship.FacingOffset = MathF.PI;
        ship.StartMove([ship.Pose.Position, goal]);
        var climb = 0f;
        var along = 0f;
        for (var i = 0; i < 3000; i++)
        {
            ship.Follow(Band);
            ship.Step(Tick, false, true);
            Assert.AreEqual(0f, ship.Pose.Pitch);
            Assert.AreEqual(0f, ship.Pose.Roll);
            Assert.AreEqual(0f, ship.Rates.Pitch);
            Assert.AreEqual(0f, ship.Rates.Roll);
            climb = MathF.Max(climb, ship.Velocity.Y);
            along = MathF.Max(along, ship.Speed);
        }

        Assert.IsTrue(climb > 1, $"the ship climbs with its velocity: {climb}");
        Assert.IsTrue(along > 10 && along <= 20.01f, $"the speed rises toward the cap: {along}");
        Assert.IsTrue(Vector3.Distance(ship.Pose.Position, goal) < 5, "the 3D spring brings it to the point");
        Assert.IsTrue(ship.Speed < 1);
    }

    [TestMethod]
    public void InsideTooCloseToTurnABorgShipBrakesBy3DSpringAndRestsItsHeadingTargetWhenSlow()
    {
        // the hold's point is the ship's own: the command is the spring's, the acceleration -2sqrt(k) x v
        var ship = BorgShip(new Vector3(0, 38.5f, 0));
        ship.SetVelocity(new Vector3(0, 0, -2));
        ship.HeadingTarget = 1;
        ship.Brake(Band);
        Assert.AreEqual(ship.Pose.Yaw, ship.HeadingTarget, "slower than tooSlowToTurn the target is the yaw");
        ship.Step(Tick, false, true);
        Near(2 * (1 - SpringDamping * Tick), ship.Speed, 1e-4);
    }

    [TestMethod]
    public void ABorgShipThatAttacksDoesNotTurnToFaceItsTarget()
    {
        // the dogfight's steering sets no heading target (0x42c9d0, 0x42f740): the yaw only damps, whatever the offset;
        // the target-location pivot commands scale 0, so it neither thrusts nor turns
        var ship = BorgShip(new Vector3(0, 38.5f, 0));
        ship.FacingOffset = MathF.PI / 2;
        for (var i = 0; i < 500; i++)
        {
            ship.Dogfight(new Vector3(200, 38.5f, -50), 100, Band);
            ship.Step(Tick, false, true);
        }

        Assert.AreEqual(0f, ship.Pose.Yaw);
        Assert.AreEqual(0f, ship.Rates.Yaw);
        var pivoting = BorgShip(new Vector3(0, 38.5f, 0));
        pivoting.FacingOffset = MathF.PI / 2;
        for (var i = 0; i < 100; i++)
        {
            pivoting.Pivot(new Vector3(200, 38.5f, -50));
            pivoting.Step(Tick, false, true);
        }

        Assert.AreEqual(0f, pivoting.Pose.Yaw);
        Assert.AreEqual(0f, pivoting.Speed);
        var closing = BorgShip(new Vector3(0, 38.5f, 0));
        closing.FacingOffset = MathF.PI / 2;
        for (var i = 0; i < 300; i++)
        {
            closing.Approach(new Vector3(300, 38.5f, 0), Band);
            closing.Step(Tick, false, true);
        }

        Assert.IsTrue(closing.Velocity.X > 1, "it closes by the velocity command");
        Assert.AreEqual(0f, closing.Pose.Yaw);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ABorgShipWithoutThrustCoastsAndKeepsItsYawRate(bool dying)
    {
        var ship = BorgShip(new Vector3(0, 38.5f, 0));
        ship.HeadingTarget = 1;
        ship.Step(Tick, false, true);
        var rate = ship.Rates.Yaw;
        Assert.IsTrue(rate > 0);
        ship.SetVelocity(new Vector3(0, 0, -10));
        var yaw = ship.Pose.Yaw;
        for (var i = 0; i < 50; i++)
        {
            ship.Step(Tick, dying, dying); // dying with working engines, or without engines
        }

        Assert.AreEqual(rate, ship.Rates.Yaw);
        Near(yaw + rate * 50 * Tick, ship.Pose.Yaw, 1e-5);
        Near(8, ship.Speed, 0.01); // braking at 2.0 for 1 s
        Assert.AreEqual(0f, ship.Pose.Roll);
    }

    // The stock Borg physics' thrust inertia (bbatphys.odf frontBackInertia 30) on impulse speed 20: the drag c6f0 is
    // 1 / (30 / 20) = 2/3 and the thrust c6fc is c6f0 x 20 = 40/3, so a command of length 1 gives 13.33 at most.
    static readonly PhysicsKeys StockBorg = Fast.With(("frontBackInertia", 30));
    const float StockThrust = 40f / 3;
    const float StockDrag = 2f / 3;

    [TestMethod]
    [DataRow(0f, 1f)]
    [DataRow(5f, 1f)]
    [DataRow(0f, 0.5f)]
    public void TheBorgSteeringCommandStaysWithinTheScaleOnStockCoefficients(float speed, float scale)
    {
        // direction d of length 1 and speed v along it: the wanted velocity is d x 20 x scale, so the command
        // u = (2sqrt(1.5) x (20 x scale - v) + c6f0 x v) / c6fc is 3.67 (v 0), 3.0 (v 5), 1.84 (v 0, scale 0.5): over
        // its length limit `scale` in each, so it is cut to length `scale` along d and the acceleration is
        // c6fc x scale x d - c6f0 x v. Without the cut the first tick would gain 0.98, 0.73, and 0.49 instead.
        var direction = Vector3.Normalize(new Vector3(0.6f, 0.8f, 0));
        var ship = BorgShip(new Vector3(0, 38.5f, 0), 0, StockBorg);
        ship.SetVelocity(direction * speed);
        ship.Steer(direction, scale);
        ship.Step(Tick, false, true);
        Near(speed + (StockThrust * scale - StockDrag * speed) * Tick, ship.Speed, 1e-4);
        Near(direction, Vector3.Normalize(ship.Velocity), 1e-4);
    }

    [TestMethod]
    [DataRow(-10f, -10.13333f)]
    [DataRow(10f, 9.6f)]
    public void TheBorgArrivalSpringsCommandHasLengthOneWhichWayItPointsAgainstTheVelocity(
        float velocityZ,
        float expectedZ
    )
    {
        // 45 units from the point on the axis, inside tooCloseToTurn 50: the spring acceleration is -k x (pos - P) - d x v
        // along z (sandbox frame) with k 1.5 and d 2.449, and the command u = (a + c6f0 x v) / c6fc points at the point.
        // v -10 (u.v > 0): a -43.0, u -3.73. v +10 (u.v < 0, flying away): a -92.0, u -6.4. Both are cut to length 1 (the
        // stand-in's one limit), so the acceleration is -c6fc - c6f0 x v: -6.667 and -20. Uncut it would be -43.0 and -92.0.
        var ship = BorgShip(new Vector3(0, 38.5f, 0), 0, StockBorg);
        ship.SetVelocity(new Vector3(0, 0, velocityZ));
        ship.Approach(new Vector3(0, 38.5f, -45), Band);
        ship.Step(Tick, false, true);
        Near(expectedZ, ship.Velocity.Z, 1e-4);
        Near(0, ship.Velocity.X, 1e-6);
        Near(0, ship.Velocity.Y, 1e-6);
    }

    [TestMethod]
    public void ABorgShipRecoversFromAFaultThatLeftANonFiniteHeadingTarget()
    {
        // a non-finite bearing writes a NaN heading target; the step's yaw error is NaN and the step faults, restoring
        // the finite yaw. The target is restored with it: the next step, under Approach (no target written), runs.
        var ship = BorgShip(new Vector3(0, 38.5f, 0));
        ship.HeadingTarget = float.NaN;
        Assert.ThrowsExactly<InvalidOperationException>(() => ship.Step(Tick, false, true));
        Assert.IsTrue(float.IsFinite(ship.HeadingTarget));
        ship.Approach(new Vector3(300, 38.5f, 0), Band);
        ship.Step(Tick, false, true);
        Assert.IsTrue(ship.Speed > 0, "the ship flies on after the fault");
    }

    [TestMethod]
    public void TheCarrotSlowsTheShipAtThePathsEnd()
    {
        // the carrot's velocity is 0 at the last node: the spring matches it and the ship slows
        var ship = Ship(new Vector3(0, 38.5f, 0), MathF.PI);
        ship.StartMove([ship.Pose.Position, new Vector3(0, 38.5f, -300)]);
        var speeds = Speeds(ship, 1500);
        var peak = speeds.Max();
        Assert.IsTrue(peak > 10);
        Assert.IsTrue(speeds[^1] < peak * 0.1f, "the ship slows toward the end of its path");
    }

    [TestMethod]
    public void TheDogfightDrivesAtFullThrottleBeyondTheRangeAndAtOneThousandthWithinIt()
    {
        var far = Ship(new Vector3(0, 38.5f, 0), 0, Values(Fast, Fast));
        far.Dogfight(new Vector3(0, 38.5f, 150), 100, Band);
        Near(1, far.Controls.Throttle, 1e-6);
        var near = Ship(new Vector3(0, 38.5f, 0), 0, Values(Fast, Fast));
        near.Dogfight(new Vector3(0, 38.5f, 100), 100, Band); // |d| = R is within the range
        Near(0.001, near.Controls.Throttle, 1e-6);
        Assert.AreEqual(0, near.Controls.Pitch, "within the range the ship levels");
        Assert.AreEqual(0, near.Controls.Lift);
    }

    [TestMethod]
    public void TheDogfightAimsTheYawServoAtTheTargetsBearingEvenWithinTheRange()
    {
        // the target lies 90 off to the +X side of a ship facing +Z: the second yaw servo call steers there
        var ship = Ship(new Vector3(0, 38.5f, 0), 0, Values(Fast, Fast));
        ship.Dogfight(new Vector3(50, 38.5f, 0), 100, Band);
        var other = Ship(new Vector3(0, 38.5f, 0), 0, Values(Fast, Fast));
        other.Dogfight(new Vector3(-50, 38.5f, 0), 100, Band);
        Assert.IsTrue(ship.Controls.Roll * other.Controls.Roll < 0, "opposite sides give opposite roll controls");
        Assert.AreNotEqual(0, ship.Controls.Roll);
    }

    [TestMethod]
    public void TheDogfightZeroesTheAimsHeightAtTheBandsCeilingAndFloor()
    {
        var high = Ship(new Vector3(0, Band.Ceiling + 1, 0), 0, Values(Fast, Fast));
        high.Dogfight(new Vector3(0, Band.Ceiling + 101, 200), 100, Band); // rising above the ceiling
        Assert.AreEqual(0, high.Controls.Pitch, "no climb above the ceiling");
        var low = Ship(new Vector3(0, Band.Floor - 1, 0), 0, Values(Fast, Fast));
        low.Dogfight(new Vector3(0, Band.Floor - 101, 200), 100, Band); // sinking below the floor
        Assert.AreEqual(0, low.Controls.Pitch, "no dive below the floor");
        var free = Ship(new Vector3(0, 38.5f, 0), 0, Values(Fast, Fast));
        free.Dogfight(new Vector3(0, 138.5f, 200), 100, Band);
        Assert.AreNotEqual(0, free.Controls.Pitch, "inside the band the ship climbs");
    }

    [TestMethod]
    public void ABorgShipInTheDogfightKeepsItsHeadingAndDrivesAlongTheAimByTheVelocityCommand()
    {
        var borg = BorgShip(new Vector3(0, 38.5f, 0));
        var target = new Vector3(50, 38.5f, 300);
        borg.HeadingTarget = 1.25f;
        borg.Dogfight(target, 100, Band);
        Assert.AreEqual(1.25f, borg.HeadingTarget, "the dogfight writes no yaw target");
        borg.Step(Tick, false, true);
        Assert.AreEqual(borg.Pose.Yaw, borg.HeadingTarget, "each step resets the heading target to the yaw");
        Assert.AreEqual(0, borg.Controls.Roll);
        Assert.IsTrue(borg.Velocity.Length() > 0, "the velocity command moves the ship");
        var start = new Vector3(0, 38.5f, 0);
        Assert.IsTrue(
            Vector3.Dot(Vector3.Normalize(borg.Pose.Position - start), Vector3.Normalize(target - start)) > 0.999f,
            "along the aim"
        );
        Near(0, borg.Pose.Yaw, 1e-3); // only the stale target's first-sub-step spring turns it; the dogfight adds none
    }

    [TestMethod]
    public void ThePivotTurnsInPlaceAtZeroThrottleAndKeepsTheHeadingNearTheShip()
    {
        var ship = Ship(new Vector3(0, 38.5f, 0), 0, Values(Fast, Fast));
        ship.Pivot(new Vector3(300, 38.5f, 0));
        Assert.AreEqual(0, ship.Controls.Throttle);
        Assert.AreNotEqual(0, ship.Controls.Roll, "it turns toward the point");
        var held = Ship(new Vector3(0, 38.5f, 0), 0, Values(Fast, Fast));
        held.Pivot(new Vector3(5, 38.5f, 0)); // within 10: the forward axis, no turn
        Assert.AreEqual(0, held.Controls.Throttle);
        Near(0, held.Controls.Roll, 1e-4);
    }
}
