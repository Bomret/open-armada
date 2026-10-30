using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using OpenArmada.Diagnostics;

namespace OpenArmada.Sandbox;

/// <summary>
/// The RTS_CFG.h keys of the avoidance: <c>OBSTACLE_AVOIDANCE</c> (0x5fbc30), <c>COLLISION_LOOK_AHEAD_TIME</c>
/// (0x5fbc34), and <c>COLLISION_SAFETY_MARGIN</c> (0x5fbc38), the last two for the asteroid-field hit prediction.
/// </summary>
public sealed record AvoidanceKeys(bool Enabled, float LookAheadTime, float SafetyMargin)
{
    /// <summary>The game's compiled defaults (the statics before RTS_CFG.h is read): on, 4.0, and 1.5.</summary>
    public static readonly AvoidanceKeys Game = new(true, 4f, 1.5f);

    /// <summary>The stock install's RTS_CFG.h (lines 272, 276, 280): off, 1.4, and 1.2.</summary>
    public static readonly AvoidanceKeys Stock = new(false, 1.4f, 1.2f);
}

/// <summary>
/// The class values the avoidance reads of an object: <c>avoidMe</c> (+0xd8; class default 1, 0x46a0d0),
/// <c>avoidanceClass</c> (+0x1ac; class default 1000), which ranks the objects a ship yields to, and
/// <c>collisionRadius</c> (+0x170 where above 0; 0 where the definition sets none above 0).
/// </summary>
public readonly record struct AvoidanceTraits(bool AvoidMe, int AvoidanceClass, float CollisionRadius = 0)
{
    /// <summary>The class defaults of the object class constructor (0x46a0d0): avoided, class 1000, no collision radius.</summary>
    public static readonly AvoidanceTraits Default = new(true, 1000);

    /// <summary>
    /// The traits of a definition: a key it does not set takes the class default; one that is no number (a whole number
    /// for the first two) takes the default and is reported through <c>report</c>; a <c>collisionRadius</c> that is
    /// not finite (infinity, or beyond a float's range) is no number to the definition reader and is reported so
    /// (<b>stand-in</b>: the game accepts infinity; flight must stay finite). A <c>collisionRadius</c> that is not above
    /// 0 is no radius and counts as 0, without a report.
    /// </summary>
    public static AvoidanceTraits Read(OpenArmada.Assets.ResolvedDefinition definition, Action<string> report)
    {
        var traits = Default;
        foreach (var key in new[] { "avoidMe", "avoidanceClass", "collisionRadius" })
        {
            if (!definition.Has(key))
                continue;
            try
            {
                traits = key switch
                {
                    "avoidMe" => traits with { AvoidMe = definition.GetInt(key) != 0 },
                    "avoidanceClass" => traits with { AvoidanceClass = definition.GetInt(key) },
                    _ => traits with { CollisionRadius = Usable(definition.GetFloat(key)) },
                };
            }
            catch (OpenArmada.Assets.DefinitionException e)
            {
                report($"{definition.Name}: {key} is no number, the class default used ({e.Message})");
            }
        }

        return traits;
    }

    /// <summary>
    /// The traits of a class by name, read once per class: a bad value is logged as one WARN entry of area combat
    /// (INV-0002) and reported through <c>report</c>, both on the class's first lookup only. A class with no definition
    /// takes the class defaults.
    /// </summary>
    public static Func<string, AvoidanceTraits> ByClass(
        IReadOnlyDictionary<string, OpenArmada.Assets.ResolvedDefinition> definitions,
        Action<string> report
    )
    {
        var read = new Dictionary<string, AvoidanceTraits>(StringComparer.OrdinalIgnoreCase);

        return name =>
        {
            if (!read.TryGetValue(name, out var traits))
            {
                read[name] = traits = definitions.TryGetValue(name, out var definition)
                    ? Read(
                        definition,
                        text =>
                        {
                            Log.Warn(Area.Combat, text);
                            report(text);
                        }
                    )
                    : Default;
            }

            return traits;
        };
    }

    // a collisionRadius (class +0x170, read by 0x46a470) counts where it is above 0; else the radius falls back to the
    // model's, which is what a 0 here stands for. stand-in: the finite check. The game takes any value above 0,
    // infinity included; flight must stay finite, so a non-finite value is no radius. The definition reader takes only
    // finite numbers, so a non-finite value reaches here as a read that failed and is reported as no number; the check
    // is the guard for a caller that hands one over.
    internal static float Usable(float value) => value > 0 && float.IsFinite(value) ? value : 0;
}

/// <summary>
/// One object as the avoidance sees it, in the sandbox's frame: a ship or station (<c>Craft</c>), an asteroid
/// field (<c>Field</c>), or another terrain object such as a planet.
/// </summary>
public sealed record AvoidanceBody
{
    /// <summary>The world center (+0x12c).</summary>
    public required Vector3 Center { get; init; }

    /// <summary>The model bounding radius (+0x60).</summary>
    public required float Radius { get; init; }

    /// <summary>The world velocity (+0x140); its length is the speed (+0x138).</summary>
    public Vector3 Velocity { get; init; }

    /// <summary>The definition's <c>avoidMe</c>.</summary>
    public bool AvoidMe { get; init; } = true;

    /// <summary>The definition's <c>avoidanceClass</c>.</summary>
    public int AvoidanceClass { get; init; } = 1000;

    /// <summary>A ship or station (a <c>Craft</c>, +0xbc bit 8).</summary>
    public bool Craft { get; init; }

    /// <summary>A station (class <c>is_starbase</c>, +0x235): treated as still by the closest approach.</summary>
    public bool Starbase { get; init; }

    /// <summary>
    /// The point the object's move process heads for; null for an object without a move process or with none
    /// active. inferred: the game's test is the process's byte +0xc0 and the point is +0xb4.
    /// </summary>
    public Vector3? MoveTarget { get; init; }

    /// <summary>An asteroid field (+0xbc bit 0x8000): not scored, but its predicted hit stops the ship.</summary>
    public bool Field { get; init; }

    /// <summary>The definition's <c>collisionRadius</c>; the radius of the hit prediction where above 0 (+0x170).</summary>
    public float CollisionRadius { get; init; }

    /// <summary>The radius the hit prediction reads (+0x170): <c>collisionRadius</c> where above 0, else the model radius.</summary>
    public float Reach => CollisionRadius > 0 ? CollisionRadius : Radius;
}

/// <summary>
/// One ship's obstacle avoidance, the game's scorer 0x42ef70 with <c>OBSTACLE_AVOIDANCE</c> on, called by the steering
/// direction between building it and the height clamp (<see cref="FlightModel.Avoidance"/>). It looks
/// <c>4 x speed</c> seconds ahead for the objects the ship would pass too close to, builds ten candidate directions
/// from the wanted one, scores each against every threat, favours the last pick, and steers by the best. The wanted
/// direction passes unchanged when no object threatens. docs/rules/obstacle-avoidance.md states the rule.
/// </summary>
public sealed class ObstacleAvoidance
{
    // Each static tunable below is the game's own value at the address named; none is a stand-in.

    // the look-ahead is this many seconds of the ship's speed, and a threat meets the ship within this many seconds
    // (0x5f8478)
    const float LookAheadSeconds = 4f;

    // the radii add up to this times their sum for the keep-away distance (0x5f845c)
    const float RadiusScale = 1.1f;

    // a neighbour slower than SlowSpeed weighs this much (0x5f8458); the speed (0x5ca238)
    const float SlowWeight = 2f;
    const float SlowSpeed = 1f;

    // the penalty factor (0x5f8464), the base score factor (0x5f8468), the hysteresis factor (0x5f8470)
    const float PenaltyFactor = -100f;
    const float BaseScore = 100f;
    const float HysteresisFactor = 50f;

    // a negative best score scales the direction by this (0x5f846c)
    const float NegativeScale = 0.75f;

    // a neighbour with a move target counts this share of its velocity, the rest its speed along the way to the target
    // (0x5f8474)
    const float VelocityBlend = 0.2f;

    // the least speed a candidate is tested at (0x5ca55c)
    const float MinTestSpeed = 10f;

    // the veto score of a candidate (the immediate 0xc7c35000), the start of the best search (0x5ca770)
    const float Veto = -1e5f;
    const float BestStart = -1e30f;

    // the closest approach takes the relative path as still below this squared length (0x5ca750)
    const float StillSquared = 0.001f;

    // a neighbour's move target counts above this squared distance (0x42ec99 reads 1.0 at 0x5ca758)
    const float TargetSquared = 1f;

    // the candidate angles: pi x 1/12 = 15 degrees, twice, and four times that (0x5ca740, 0x5ca768, 0x5ca760)
    const float Step = MathF.PI / 12;

    const int Candidates = 10;

    readonly AvoidanceKeys _keys;
    readonly AvoidanceBody _self;
    readonly Func<IEnumerable<AvoidanceBody>> _neighbours;

    // The last steering direction (+0x49c). stand-in: it starts at zero, which gives the hysteresis no bonus on the first
    // call (the unit of zero is NaN and a NaN does not compare above 0); the game's initial value is not recovered.
    Vector3 _last;

    bool _fieldHit;

    /// <summary>
    /// The avoidance of <c>self</c> (its radius and class; its center and velocity are the flight's) among the bodies
    /// <c>neighbours</c> returns at each call, which leaves <c>self</c> out.
    /// </summary>
    public ObstacleAvoidance(AvoidanceKeys keys, AvoidanceBody self, Func<IEnumerable<AvoidanceBody>> neighbours)
    {
        _keys = keys;
        _self = self;
        _neighbours = neighbours;
    }

    /// <summary>
    /// Whether the last steering predicted a hit on an asteroid field (the process byte +0x104), and clears it. The
    /// game's process slot 0x42e3f0 then zeroes the roll, pitch, and lift controls and holds the ship where it is.
    /// </summary>
    public bool TakeFieldHit()
    {
        var hit = _fieldHit;
        _fieldHit = false;

        return hit;
    }

    /// <summary>
    /// The direction to steer (sandbox frame): <c>wanted</c> where nothing threatens or a field hit is predicted, else the
    /// best candidate, scaled by 0.75 where its score is negative. <c>goal</c> is the move order's goal. The
    /// sandbox's orders name no object, so the game's skip of the object a ship heads for (and of objects heading for
    /// it) never applies.
    /// </summary>
    public Vector3 Steer(FlightModel ship, Vector3 goal, Vector3 wanted)
    {
        // The scoring runs in the game's frame, where the candidates' turn formulas hold as the game states them: the
        // sandbox mirrors Z only, so L(theta), a turn toward -X from a bow at +Z, is the ship's left as drawn.
        var d = FlightModel.Game(wanted);
        if (d.LengthSquared() <= 0)
        {
            // stand-in: the game divides by the zero length and gets NaN candidates; the wanted zero stays. The last
            // direction is stored as zero, as the game stores the wanted direction on every call past the gates.
            _last = d;

            return wanted;
        }

        var position = FlightModel.Game(ship.Pose.Position);
        var speed = ship.Speed;
        var look = speed * LookAheadSeconds;
        var forward = FlightModel.Game(ship.Forward);
        var movement = d * speed;

        // The grid registers each object by its center in one cell (0x470940) and keeps the largest radius of the objects
        // registered; the query box (0x40e990) is the ship's radius plus the look-ahead, widened by that radius on each side.
        // stand-in: the box is exact; the game's reaches whole cells.
        var all = new List<AvoidanceBody>(_neighbours());
        var half = _self.Radius + look + MathF.Max(_self.Radius, all.Count == 0 ? 0 : all.Max(b => b.Radius));
        Candidate[]? candidates = null;
        foreach (var o in all)
        {
            var center = FlightModel.Game(o.Center);
            if (MathF.Abs(center.X - position.X) > half || MathF.Abs(center.Z - position.Z) > half)
                continue;
            if (!o.AvoidMe)
                continue;
            if (o.Field)
            {
                if (FieldHits(ship, position, forward, d, FlightModel.Game(goal), o, center))
                {
                    _fieldHit = true;

                    return wanted;
                }

                continue; // the game scores a field only with a debug byte (0x632e76, 0) set
            }

            if (!(o.AvoidanceClass >= _self.AvoidanceClass || Speed(o) <= SlowSpeed))
                continue;
            if ((position - center).Length() - _self.Radius - o.Radius > look)
                continue;
            var (t, _) = Closest(position, movement, o);
            if (!(t > 0 && t <= LookAheadSeconds))
                continue;
            candidates ??= Build(d, forward);
            Score(candidates, position, MathF.Max(speed, MinTestSpeed), o);
        }

        if (candidates == null)
        {
            _last = d;

            return wanted;
        }

        var unit = _last * (1f / MathF.Sqrt(_last.LengthSquared()));
        foreach (var c in candidates)
        {
            var q = Vector3.Dot(c.Direction, unit);
            if (q > 0)
            {
                c.Score += HysteresisFactor * q;
            }
        }

        var best = 0;
        var top = BestStart;
        for (var k = 0; k < Candidates; k++)
        {
            if (candidates[k].Score > top)
            {
                top = candidates[k].Score;
                best = k;
            }
        }

        var result = candidates[best].Direction * (top < 0 ? NegativeScale : 1f);
        _last = result;

        return FlightModel.Game(result);
    }

    sealed class Candidate(Vector3 direction, float score)
    {
        public Vector3 Direction { get; } = direction;
        public float Score { get; set; } = score;
    }

    static float Speed(AvoidanceBody o) => o.Velocity.Length();

    // The ten candidates (0x424ba0) of the wanted direction d (not normalized in, normalized here): d, the turns of 15,
    // 30, and 60 degrees left and right about the vertical (the height kept), the ship's forward, and d pitched up and
    // down by 15 degrees. Each starts at 100 x (its alignment with d); the forward is vetoed by nothing (0x5f8460 is 1).
    static Candidate[] Build(Vector3 wanted, Vector3 forward)
    {
        var d = wanted * (1f / MathF.Sqrt(wanted.LengthSquared()));
        Vector3 Left(float a)
        {
            var (s, c) = MathF.SinCos(a);

            return new Vector3(d.X * c - d.Z * s, d.Y, d.Z * c + d.X * s);
        }

        Vector3 Right(float a)
        {
            var (s, c) = MathF.SinCos(a);

            return new Vector3(d.X * c + d.Z * s, d.Y, d.Z * c - d.X * s);
        }

        var (sa, ca) = MathF.SinCos(Step);
        var h = new Vector3(-d.Z, 0, d.X);
        var cross = Vector3.Cross(h, d);
        var along = h * (Vector3.Dot(h, d) * (1 - ca));
        var directions = new[]
        {
            d,
            Left(Step),
            Right(Step),
            Left(2 * Step),
            Right(2 * Step),
            Left(4 * Step),
            Right(4 * Step),
            forward,
            d * ca + cross * sa + along,
            d * ca - cross * sa + along,
        };

        return Array.ConvertAll(directions, c => new Candidate(c, BaseScore * Vector3.Dot(c, d)));
    }

    // The scoring of one threat (0x4250e0): each candidate's closest approach to it, tested at the ship's speed or 10,
    // costs -100 x weight x R^2 / dmin^2 where it passes inside R, the radii's sum times 1.1; a slow neighbour weighs
    // double. The vertical candidates are vetoed against a station (0x5f8461 is 1, so against nothing else).
    void Score(Candidate[] candidates, Vector3 position, float testSpeed, AvoidanceBody o)
    {
        var reach = (o.Radius + _self.Radius) * RadiusScale;
        var weight = Speed(o) < SlowSpeed ? SlowWeight : 1f;
        for (var k = 0; k < Candidates; k++)
        {
            var (t, minSquared) = Closest(position, candidates[k].Direction * testSpeed, o);
            if (t >= 0 && minSquared < reach * reach)
            {
                candidates[k].Score += PenaltyFactor * weight * reach * reach / minSquared;
            }
        }

        if (o.Craft && o.Starbase)
        {
            candidates[8].Score = candidates[9].Score = Veto;
        }
    }

    // The closest approach (0x424860) of the ship at `position` moving by `movement` to the object: the time t of the least
    // distance and its square, over the relative path. A ship (not a station) moves by its velocity, or, with a move
    // target more than one unit away, by 0.2 x velocity + 0.8 x speed toward the target; a non-ship and a station stand
    // still. A relative path shorter than sqrt(0.001) gives t = 0 and the present distance.
    static (float T, float MinSquared) Closest(Vector3 position, Vector3 movement, AvoidanceBody o)
    {
        var d = position - FlightModel.Game(o.Center);
        var w = movement;
        if (o.Craft && !o.Starbase)
        {
            var velocity = FlightModel.Game(o.Velocity);
            if (o.MoveTarget is not { } target)
            {
                w -= velocity;
            }
            else
            {
                var g = FlightModel.Game(target) - FlightModel.Game(o.Center);
                if (g.LengthSquared() > TargetSquared)
                {
                    w -= velocity * VelocityBlend + Vector3.Normalize(g) * (Speed(o) * (1f - VelocityBlend));
                }
            }
        }

        var ww = w.LengthSquared();
        if (ww >= StillSquared)
        {
            var t = -Vector3.Dot(w, d) / ww;
            var at = d + w * t;

            return (t, at.LengthSquared());
        }

        return (0, d.LengthSquared());
    }

    // The asteroid-field hit prediction (0x42ed20): the ship at the impulse speed along the wanted direction, against
    // the field, by the swept sphere test 0x4353b0 over COLLISION_LOOK_AHEAD_TIME with COLLISION_SAFETY_MARGIN. A hit
    // counts only when the field lies ahead (within 90 degrees of the bow) and the goal lies on its side (within 90
    // degrees of the way to the field). stand-in: the field's enable word (+0x500) is not interpreted and counts as set;
    // the warp multiplier (+0x4ac) is 1.
    bool FieldHits(
        FlightModel ship,
        Vector3 position,
        Vector3 forward,
        Vector3 wanted,
        Vector3 goal,
        AvoidanceBody field,
        Vector3 center
    )
    {
        var v1 = Vector3.Normalize(wanted) * (float)ship.ImpulseSpeed;
        var v2 = FlightModel.Game(field.Velocity);
        if (!SweptHit(position, v1, _self.Reach, center, v2, field.Reach))
            return false;
        var to = center - position;
        const float Quarter = MathF.PI * 0.5f; // a quarter turn: pi (0x5ca788) x 0.5 (0x5ca224)
        if (
            Math.Abs(MathF.Acos(Math.Clamp(Vector3.Dot(forward, to) / (forward.Length() * to.Length()), -1f, 1f)))
            >= Quarter
        )
            return false;
        var g = goal - position;

        return !(Math.Abs(MathF.Acos(Math.Clamp(Vector3.Dot(g, to) / (g.Length() * to.Length()), -1f, 1f))) >= Quarter);
    }

    // The swept sphere test (0x4353b0): whether two spheres, the radii's sum scaled by the safety margin, meet within the
    // look-ahead time, by the quadratic of their relative motion.
    bool SweptHit(Vector3 p1, Vector3 v1, float r1, Vector3 p2, Vector3 v2, float r2)
    {
        var reach = (r1 + r2) * _keys.SafetyMargin;
        var to = p2 - p1;
        var distance = to.Length();
        if (distance < reach)
            return true;
        if (distance - (v1.Length() + v2.Length()) * _keys.LookAheadTime > reach)
            return false;
        var relative = v2 - v1;
        var b = Vector3.Dot(relative, to);
        if (b >= 0)
            return false;
        var a = relative.LengthSquared();
        var b2 = 2 * b;
        var c = to.LengthSquared() - reach * reach;
        var disc = b2 * b2 - 4f * a * c;
        if (a == 0 || disc < 0)
            return false;
        var q = 0.5f * (b2 >= 0 ? -b2 - MathF.Sqrt(disc) : MathF.Sqrt(disc) - b2);
        var (t1, t2) = (q / a, c / q);
        var time = _keys.LookAheadTime;

        return (t1 >= 0 && t1 <= time) || (t2 >= 0 && t2 <= time);
    }
}
