using System.Numerics;
using OpenArmada.Sandbox;

namespace OpenArmada.Tests;

// Flight values built in code (INV-0001), shared by the flight, world, and play-state tests. Heading 0 faces +Z; a positive
// heading turns toward +X.
static class FlightFixtures
{
    // The values of the stock fastphys.odf, as the notes recover them (impulse speed 20): the keys a ship swaps to.
    public static readonly PhysicsKeys Fast = PhysicsKeys.Game.With(
        ("impulseSpeed", 20),
        ("velocCoeffStill", 0.3),
        ("velocWhenCoeffHalf", 0.7),
        ("pitchRotationSpeed", 0.0667),
        ("maxPitch", 45),
        ("rollRotationSpeed", 0.5),
        ("maxRoll", 40),
        ("turnRadius", 7.5),
        ("frontBackInertia", 7.5),
        ("maxReverseSpeed", 4),
        ("upDownInertia", 5),
        ("maxVerticalSpeed", 0.667),
        ("leftRightInertia", 5),
        ("braccelControlStiffness", 1.5),
        ("sy_yawAngleCoeff", 1),
        ("sy_yawVelocCoeff", 0.15),
        ("sy_rollVelocCoeff", 0),
        ("sy_rollAngleCoeff", 0),
        ("sp_pitchVelocCoeff", 0),
        ("sp_pitchAngleCoeff", -1.197),
        ("sp_velocAngleCoeff", 0.06)
    );

    // A ship's own definition, different from fastphys in the turning, the thrust, and the brake spring.
    public static readonly PhysicsKeys Own = Fast.With(
        ("turnRadius", 20),
        ("maxRoll", 30),
        ("frontBackInertia", 2),
        ("braccelControlStiffness", 10)
    );

    // Movement values on the ship's own keys with fastphys to swap to, tooCloseToTurn 40 and pathLeadDistance 150.
    public static PhysicsValues Values(PhysicsKeys? own = null, PhysicsKeys? fast = null, double tooClose = 40) =>
        PhysicsValues.From(own ?? Own, fast ?? Fast) with
        {
            TooCloseToTurn = tooClose,
            PathLeadDistance = 150,
        };

    // The pose of a ship at `at` with the sandbox bearing `heading`, level.
    public static ShipPose Pose(Vector3 at, float heading = 0) =>
        new()
        {
            Position = at,
            Pitch = 0,
            Yaw = MoveGeometry.YawOfHeading(heading),
            Roll = 0,
        };

    // A ship at `at` facing `heading`, with fastphys's keys for both (so the swap changes nothing) unless given.
    public static FlightModel Ship(Vector3 at, float heading = 0, PhysicsValues? values = null) =>
        new(values ?? Values(Fast, Fast), Pose(at, heading));

    // A ship at `at` facing `heading` whose impulse speed is `impulse` (the keys scale with it) and whose
    // tooCloseToTurn is `tooClose`, on fastphys's keys.
    public static FlightModel Cruiser(Vector3 at, float heading = 0, double impulse = 10, double tooClose = 40) =>
        Ship(at, heading, Values(Fast.With(("impulseSpeed", impulse)), Fast.With(("impulseSpeed", impulse)), tooClose));
}
