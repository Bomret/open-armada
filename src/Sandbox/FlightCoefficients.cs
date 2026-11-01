using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenArmada.Sandbox;

/// <summary>
/// The keys of a physics definition that set a ship's flight coefficients, as the game's loader reads them (0x455440),
/// each with the game's default (the hover class's constructor 0x455230) and the values the engine accepts.
/// <c>shp_*</c> and <c>spl_*</c> (the hover servos have no callers in the shipped game) are not read.
/// </summary>
public sealed class PhysicsKeys
{
    /// <summary>
    /// One key: its default, the rule a usable value follows, and that rule in words. <c>StandIn</c> marks a default
    /// that is the engine's own, not the game's, because the game's makes the flight diverge.
    /// </summary>
    public sealed record KeySpec(
        string Name,
        double Default,
        Func<double, bool> Usable,
        string Rule,
        bool StandIn = false
    );

    static KeySpec Any(string name, double fallback) => new(name, fallback, v => double.IsFinite(v), "no number");

    static KeySpec Positive(string name, double fallback) =>
        new(name, fallback, v => double.IsFinite(v) && v > 0, "no number above 0");

    /// <summary>The keys in the order the game reads them. <c>maxForwardSpeed</c> is read only without an impulse speed.</summary>
    public static readonly IReadOnlyList<KeySpec> Specs =
    [
        // the class default is -1 (0x45fbe0): a definition without impulseSpeed takes its forward speed from maxForwardSpeed
        Any("impulseSpeed", -1),
        // stand-in: the game divides by 1 - velocCoeffStill, so a value of 1 or more is no usable number
        new("velocCoeffStill", 0.1, v => double.IsFinite(v) && v < 1, "no number below 1"),
        Positive("velocWhenCoeffHalf", 7.5),
        Any("pitchRotationSpeed", 3.5),
        // stand-in: the game divides by maxPitch in the pitch servo's term, so 0 is no usable number
        Positive("maxPitch", 70),
        // stand-in: the loader's own default is -1 (0x455440), which gives the roll negative damping so that it grows
        // without bound; the engine takes the hover class's 5.5 (0x455230) for a missing or non-positive value
        new("rollRotationSpeed", 5.5, v => double.IsFinite(v) && v > 0, "no number above 0", StandIn: true),
        Positive("maxRoll", 30),
        Positive("turnRadius", 25),
        Positive("frontBackInertia", 2),
        Any("maxForwardSpeed", 20),
        Any("maxReverseSpeed", 15),
        Positive("upDownInertia", 0.5),
        Any("maxVerticalSpeed", 10),
        Positive("leftRightInertia", 0.5),
        Any("braccelControlStiffness", 10),
        Any("sy_rollVelocCoeff", 1.221),
        Any("sy_rollAngleCoeff", 0.174),
        Any("sy_yawVelocCoeff", -0.043),
        Any("sy_yawAngleCoeff", 0.726),
        Any("sp_pitchVelocCoeff", 2),
        Any("sp_pitchAngleCoeff", -1.774),
        Any("sp_velocAngleCoeff", -2.566),
    ];

    static readonly IReadOnlyDictionary<string, KeySpec> ByName = Specs.ToDictionary(s => s.Name);

    readonly IReadOnlyDictionary<string, double> _values;

    PhysicsKeys(IReadOnlyDictionary<string, double> values) => _values = values;

    /// <summary>Every key at the game's default.</summary>
    public static PhysicsKeys Game { get; } = new(Specs.ToDictionary(s => s.Name, s => s.Default));

    /// <summary>The value of a key.</summary>
    public double this[string key] => _values[key];

    /// <summary>These keys with the given ones changed; throws for a name that is no key.</summary>
    public PhysicsKeys With(params (string Key, double Value)[] changes)
    {
        var values = _values.ToDictionary(p => p.Key, p => p.Value);
        foreach (var (key, value) in changes)
        {
            if (!ByName.ContainsKey(key))
                throw new ArgumentException($"{key} is no physics key", nameof(changes));
            values[key] = value;
        }

        return new PhysicsKeys(values);
    }
}

/// <summary>
/// The coefficients of a ship's flight, derived from a physics definition's keys as the game's class load (0x455440)
/// and instance copy (0x458750) derive them. Names follow the meaning; the instance offset is in each summary.
/// </summary>
public sealed record FlightCoefficients
{
    /// <summary>Drag of the control gain's speed term (+0x6a8).</summary>
    public required float GainSlope { get; init; }

    /// <summary>The control gain's constant term (+0x6ac): 1 / (1 - velocCoeffStill).</summary>
    public required float GainBase { get; init; }

    /// <summary>The pitch damping rate (+0x6b4): pitchRotationSpeed times impulseSpeed.</summary>
    public required float PitchRate { get; init; }

    /// <summary>The pitch restoring stiffness (+0x6b8).</summary>
    public required float PitchStiffness { get; init; }

    /// <summary>The pitch control's gain (+0x6c0): the stiffness times maxPitch in radians.</summary>
    public required float PitchControl { get; init; }

    /// <summary>The roll damping rate (+0x6c8): rollRotationSpeed times impulseSpeed.</summary>
    public required float RollRate { get; init; }

    /// <summary>The roll restoring stiffness (+0x6cc): a quarter of the rate squared.</summary>
    public required float RollStiffness { get; init; }

    /// <summary>The roll control's gain (+0x6d4).</summary>
    public required float RollControl { get; init; }

    /// <summary>The bank-to-turn gain (+0x6dc): the yaw rate is its negative times the roll angle.</summary>
    public required float BankTurn { get; init; }

    /// <summary>The forward drag (+0x6f0): 1 / (frontBackInertia / impulseSpeed).</summary>
    public required float ForwardDrag { get; init; }

    /// <summary>The forward thrust term (+0x6fc): the drag times the forward speed.</summary>
    public required float ForwardThrust { get; init; }

    /// <summary>The reverse thrust term (+0x700): the drag times maxReverseSpeed.</summary>
    public required float ReverseThrust { get; init; }

    /// <summary>The vertical damping (+0x708).</summary>
    public required float UpDownDrag { get; init; }

    /// <summary>The lift term (+0x710): the damping times maxVerticalSpeed.</summary>
    public required float Lift { get; init; }

    /// <summary>The sideways damping (+0x718).</summary>
    public required float SideDrag { get; init; }

    /// <summary>The turning radius of the turning-circle test (class +0x488); the active definition's value (0x458080).</summary>
    public required float TurnRadius { get; init; }

    /// <summary>The brake spring's stiffness (+0x4b4): braccelControlStiffness.</summary>
    public required float BrakeStiffness { get; init; }

    /// <summary>The brake spring's damping (+0x4b8): twice the root of the stiffness.</summary>
    public required float BrakeDamping { get; init; }

    /// <summary>The yaw servo's roll-rate term (+0x734).</summary>
    public required float YawServoRollRate { get; init; }

    /// <summary>The yaw servo's roll-angle term (+0x738).</summary>
    public required float YawServoRollAngle { get; init; }

    /// <summary>The yaw servo's yaw-rate term (+0x73c).</summary>
    public required float YawServoYawRate { get; init; }

    /// <summary>The yaw servo's yaw-error term (+0x740).</summary>
    public required float YawServoError { get; init; }

    /// <summary>The pitch servo's pitch-rate term (+0x748).</summary>
    public required float PitchServoRate { get; init; }

    /// <summary>The pitch servo's pitch-error term (+0x74c).</summary>
    public required float PitchServoAngle { get; init; }

    /// <summary>The pitch servo's flight-path-angle term (+0x750).</summary>
    public required float PitchServoPath { get; init; }

    // The control gain at the forward speed: vref, which the pitch and roll stiffness divide by.
    float ReferenceGain { get; init; }

    /// <summary>
    /// The coefficients of <c>keys</c>. With an impulse speed above 0 the loader scales the rates, speeds, and
    /// inertias by it and takes it as the forward speed; otherwise the keys stand as they are and
    /// <c>maxForwardSpeed</c> is the forward speed.
    /// </summary>
    public static FlightCoefficients Derive(PhysicsKeys keys)
    {
        double K(string key) => keys[key];
        var impulse = K("impulseSpeed");
        var scaled = impulse > 0;
        var maxForward = scaled ? impulse : K("maxForwardSpeed");
        var s = scaled ? impulse : 1;
        var half = K("velocWhenCoeffHalf") * s;
        var pitchRate = K("pitchRotationSpeed") * s;
        var rollRate = K("rollRotationSpeed") * s;
        var frontBack = K("frontBackInertia") / s;
        var maxReverse = K("maxReverseSpeed") * s;
        var upDown = K("upDownInertia") / s;
        var maxVertical = K("maxVerticalSpeed") * s;
        var leftRight = K("leftRightInertia") / s;

        var gainBase = (float)(1 / (1 - K("velocCoeffStill")));
        var gainSlope = (float)((2 - gainBase) / half);
        var reference = (float)(1 - 1 / (gainSlope * maxForward + gainBase));
        var pitchStiffness = (float)(0.25 * pitchRate * pitchRate / reference);
        var pitchControl = pitchStiffness * (float)(K("maxPitch") * Math.PI / 180);
        var maxRollRadians = (float)(Math.PI * K("maxRoll") / 180);
        var rollStiffness = (float)(0.25 * rollRate * rollRate);
        var drag = (float)(1 / frontBack);
        var upDownDrag = (float)(1 / upDown);
        var stiffness = K("braccelControlStiffness");

        return new FlightCoefficients
        {
            GainSlope = gainSlope,
            GainBase = gainBase,
            PitchRate = (float)pitchRate,
            PitchStiffness = pitchStiffness,
            PitchControl = pitchControl,
            RollRate = (float)rollRate,
            RollStiffness = rollStiffness,
            RollControl = rollStiffness * maxRollRadians / reference,
            BankTurn = (float)(maxForward / (K("turnRadius") * maxRollRadians)),
            ForwardDrag = drag,
            ForwardThrust = drag * (float)maxForward,
            ReverseThrust = drag * (float)maxReverse,
            UpDownDrag = upDownDrag,
            Lift = upDownDrag * (float)maxVertical,
            SideDrag = (float)(1 / leftRight),
            TurnRadius = (float)K("turnRadius"),
            BrakeStiffness = (float)stiffness,
            BrakeDamping = (float)(2 * Math.Sqrt(stiffness)),
            YawServoRollRate = (float)(K("sy_rollVelocCoeff") / K("maxRoll") / s),
            YawServoRollAngle = (float)(K("sy_rollAngleCoeff") / K("maxRoll")),
            YawServoYawRate = (float)K("sy_yawVelocCoeff"),
            YawServoError = (float)K("sy_yawAngleCoeff"),
            PitchServoRate = (float)(K("sp_pitchVelocCoeff") / s),
            PitchServoAngle = (float)K("sp_pitchAngleCoeff"),
            PitchServoPath = (float)K("sp_velocAngleCoeff"),
            ReferenceGain = reference,
        };
    }

    /// <summary>
    /// Whether the coefficients give a flight the step can integrate: every one is a finite number, the gain at the
    /// forward speed is above 0, and the gain's denominator stays above 0 up to 120 x sqrt(3), the longest velocity the
    /// end-of-step reset leaves (it clears a velocity with a component over the velocity limit, so each component stays
    /// within 120; a negative slope, from a <c>velocCoeffStill</c> over 0.5, can bring it to 0 at a speed the ship
    /// reaches).
    /// </summary>
    public bool IsUsable =>
        new[]
        {
            GainSlope,
            GainBase,
            PitchRate,
            PitchStiffness,
            PitchControl,
            RollRate,
            RollStiffness,
            RollControl,
            BankTurn,
            ForwardDrag,
            ForwardThrust,
            ReverseThrust,
            UpDownDrag,
            Lift,
            SideDrag,
            TurnRadius,
            BrakeStiffness,
            BrakeDamping,
            YawServoRollRate,
            YawServoRollAngle,
            YawServoYawRate,
            YawServoError,
            PitchServoRate,
            PitchServoAngle,
            PitchServoPath,
            ReferenceGain,
        }.All(float.IsFinite)
        && ReferenceGain > 0
        && GainBase + GainSlope * FlightModel.VelocityLimit * MathF.Sqrt(3) > 0;
}
