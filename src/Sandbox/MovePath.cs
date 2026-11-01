using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace OpenArmada.Sandbox;

/// <summary>
/// The path of a move order as a list of points and the carrot that walks along it (0x42ded0, 0x434a50): the carrot
/// advances along the path's arc length 10 units a pass until it lies at least the lead distance from the ship or
/// stands on the last point, and carries the velocity the ship should match there: the unit tangent of its segment times
/// the ship's speed, zero on the last point. Points are given in the sandbox's frame; the path keeps them in the game's.
/// </summary>
public sealed class MovePath
{
    // the carrot's pace along the path in units per pass (0x5ca7a8) and the least segment the walk steps into (0x5caa88)
    const float Pace = 10f;
    const float LeastSegment = 0.001f;

    readonly Vector3[] _points;
    readonly float[] _lengths;
    readonly Vector3 _final;
    int _segment = -1;
    float _distance;
    Vector3 _carrot;
    Vector3 _carrotVelocity;

    /// <summary>
    /// A path through <c>points</c> in the sandbox's frame, the first at the ship and the last the goal; at least one.
    /// </summary>
    public MovePath(IReadOnlyList<Vector3> points, Vector3? final = null)
    {
        if (points.Count == 0)
            throw new ArgumentException("a path has at least one point", nameof(points));
        _points = points.Select(FlightModel.Game).ToArray();
        _final = final is { } f ? FlightModel.Game(f) : _points[^1];
        _lengths = new float[_points.Length];
        for (var i = 1; i < _points.Length; i++)
        {
            _lengths[i] = _lengths[i - 1] + Vector3.Distance(_points[i - 1], _points[i]);
        }
    }

    /// <summary>The path's last point in the sandbox's frame: where the carrot ends.</summary>
    public Vector3 Goal => FlightModel.Game(_points[^1]);

    /// <summary>
    /// The order's final point (game frame): the point the arrival test and the move maneuver aim at (0x42e4a0's third
    /// argument is the order list's last point). It is the path's last point unless the path was planned to an earlier
    /// point of the order, as the stuck replan does (0x42ded0).
    /// </summary>
    internal Vector3 FinalGame => _final;

    /// <summary>
    /// Advances the carrot for a ship at <c>center</c> (game frame) and returns it with its velocity: the carrot steps
    /// 10 units along the path a pass until its squared distance from the ship reaches <c>leadSquared</c> or it stands on the
    /// last point's segment. <c>speed</c> is the ship's own speed cap.
    /// </summary>
    internal (Vector3 Carrot, Vector3 Velocity) Lead(Vector3 center, float leadSquared, float speed)
    {
        while (true)
        {
            if (_segment < 0)
            {
                _segment = 0;
            }
            else if (_segment >= _points.Length - 1 || Vector3.DistanceSquared(_carrot, center) >= leadSquared)
            {
                return (_carrot, _carrotVelocity);
            }

            _distance += Pace;
            _carrot = PointAt(_distance);
            var along = _segment < _points.Length - 1 ? _points[_segment + 1] - _points[_segment] : Vector3.Zero;
            // stand-in: two coinciding points give no tangent, so the carrot's velocity is 0 there (the game divides by 0)
            _carrotVelocity = along.LengthSquared() > 0 ? Vector3.Normalize(along) * speed : Vector3.Zero;
        }
    }

    // The point at arc length `s`, moving the carrot's segment forward past the segments it has run through (0x434a50).
    Vector3 PointAt(float s)
    {
        var last = _points.Length - 1;
        if (_lengths[_segment] < s && _segment + 1 < _points.Length)
        {
            while (true)
            {
                if (s < _lengths[_segment + 1] && _lengths[_segment + 1] - _lengths[_segment] >= LeastSegment)
                    break;
                _segment++;
                if (_segment + 1 >= _points.Length)
                    break;
            }
        }

        if (_segment != last && _lengths[_segment] < s)
        {
            var t = (s - _lengths[_segment]) / (_lengths[_segment + 1] - _lengths[_segment]);

            return (1 - t) * _points[_segment] + t * _points[_segment + 1];
        }

        return _points[_segment];
    }
}
