using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using OpenArmada.Formats;

namespace OpenArmada.Sandbox;

/// <summary>
/// The state of the units of a play run, read for the state dump and for <c>expect</c>. It holds the units' names,
/// their poses, and the combat state each unit had when the run began. A destroyed unit leaves the combat world
/// but keeps its state here with <see cref="UnitCombatState.Destroyed"/> set; a unit without a combat profile has
/// no state at all.
/// </summary>
public sealed class PlayState
{
    readonly UnitNames _names;
    readonly Func<PlacedObject, ShipPose> _pose;
    readonly Func<PlacedObject, Sod?> _model;
    readonly Func<int> _debris;
    readonly Dictionary<PlacedObject, UnitCombatState> _states = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// <c>names</c> holds the addressable units; <c>pose</c> reads a unit's current pose; <c>states</c> are the
    /// combat states of the units in the combat world at the start of the run; <c>model</c> reads a unit's model
    /// for the damage nodes of its dump line (null: no unit has one).
    /// </summary>
    public PlayState(
        UnitNames names,
        Func<PlacedObject, ShipPose> pose,
        IEnumerable<UnitCombatState> states,
        Func<PlacedObject, Sod?>? model = null,
        Func<int>? debris = null
    )
    {
        _debris = debris ?? (() => 0);
        _names = names;
        _pose = pose;
        _model = model ?? (_ => null);
        foreach (var state in states)
        {
            _states[state.Unit] = state;
        }
    }

    /// <summary>
    /// The unit that <c>name</c> addresses, or the problem: unknown, or a bare name several units share.
    /// </summary>
    public (PlacedObject? Unit, string? Problem) Resolve(string name) => _names.Resolve(name);

    /// <summary>
    /// The state dump: one line per addressable unit in plan order, then a last line <c>debris=</c> with the number of
    /// live debris pieces, <c>\n</c>-terminated, invariant culture, two
    /// decimals, the systems in a fixed order, and no time of day. A unit without combat state shows
    /// <c>state=none</c> after its position, velocity, and angles. Fields, space-separated: name, team, pos (x,y,z), vel
    /// (x,y,z, the unit's world velocity in the frame of pos; 0,0,0 for a unit that stands still or has no combat
    /// state), pitch, yaw, and roll (the unit's orientation as the game's angles in radians, see
    /// <see cref="MoveGeometry"/>), shields, crew, one
    /// <c>System=hit points/maximum/intact|destroyed</c> per system the unit has, derelict, immobile, coasting,
    /// destroyed (true once the unit has left the world), dying, alert, order,
    /// target, move (<c>x,z</c> of the player's open move order, or <c>none</c>), shieldDown (the state's shield-down overlay flag), damage (the names of the damage nodes the state
    /// selects, comma-separated, or <c>none</c>; see <see cref="DamageNodes"/>). A unit that has left the world keeps
    /// the values of its last state in every field.
    /// </summary>
    public string Dump()
    {
        var text = new StringBuilder();
        foreach (var unit in _names.Units)
        {
            var pose = _pose(unit);
            var p = pose.Position;
            var v = _states.TryGetValue(unit, out var moving) ? moving.Velocity : Vector3.Zero;
            text.Append(
                CultureInfo.InvariantCulture,
                $"{_names.Name(unit)} team={unit.Team} pos={p.X:F2},{p.Y:F2},{p.Z:F2} vel={v.X:F2},{v.Y:F2},{v.Z:F2}"
                    + $" pitch={Angle(pose.Pitch)} yaw={Angle(pose.Yaw)} roll={Angle(pose.Roll)}"
            );
            if (_states.TryGetValue(unit, out var s))
            {
                text.Append(CultureInfo.InvariantCulture, $" shields={s.Shields:F2} crew={s.Crew:F2}");
                foreach (var name in SystemNames.All.Where(s.Systems.ContainsKey))
                {
                    var system = s.Systems[name];
                    text.Append(
                        CultureInfo.InvariantCulture,
                        $" {name}={system.HitPoints:F2}/{system.Max:F2}/{(system.Destroyed ? "destroyed" : "intact")}"
                    );
                }

                text.Append(
                    CultureInfo.InvariantCulture,
                    $" derelict={Flag(s.Derelict)} immobile={Flag(s.Immobile)} coasting={Flag(s.Coasting)} destroyed={Flag(s.Destroyed)}"
                        + $" dying={Flag(s.Dying)}"
                        + $" alert={s.Alert} order={Of(s.Order)} target={Of(s.Target)}"
                        + $" move={(s.PlayerMove is { } move ? FormattableString.Invariant($"{move.X:F2},{move.Z:F2}") : "none")}"
                        + $" shieldDown={Flag(s.ShieldDownOverlay)}"
                );
                var damage = DamageNodes.Showing(_model(unit), s);
                text.Append(" damage=").Append(damage.Count == 0 ? "none" : string.Join(',', damage));
            }
            else
            {
                text.Append(" state=none");
            }

            text.Append('\n');
        }

        text.Append(CultureInfo.InvariantCulture, $"debris={_debris()}\n");

        return text.ToString();
    }

    /// <summary>
    /// Checks one <c>expect</c> step. Returns null when it holds, else the problem without script or line:
    /// an unresolvable unit, a unit without combat state, a unit that was destroyed and removed (only
    /// <c>destroyed</c> and <c>dying</c> can be asked of it), a system the unit lacks, or the expected and the actual value.
    /// </summary>
    public string? Expect(ExpectStep step)
    {
        var (unit, problem) = _names.Resolve(step.Unit);
        if (unit == null)
            return problem;
        if (!_states.TryGetValue(unit, out var state))
            return $"unit '{step.Unit}' has no combat state";
        if (state.Destroyed && step.Field is not ("destroyed" or "dying"))
            return $"unit '{step.Unit}' was destroyed and removed";
        string actual;
        bool holds;
        switch (step.Field)
        {
            case "destroyed":
                actual = Flag(state.Destroyed);
                holds = actual == step.Value;

                break;
            case "dying":
                actual = Flag(state.Dying);
                holds = actual == step.Value;

                break;
            case "target":
                var (expected, unknown) = step.Value == "none" ? (null, null) : _names.Resolve(step.Value);
                if (unknown != null)
                    return unknown;
                actual = Of(state.Target);
                holds = state.Target?.Unit == expected;

                break;
            default:
                double value;
                if (step.Field == "shields")
                {
                    value = state.Shields;
                }
                else if (step.Field == "crew")
                {
                    value = state.Crew;
                }
                else if (state.Systems.TryGetValue(step.Field, out var system))
                {
                    value = system.HitPoints;
                }
                else
                {
                    return $"unit '{step.Unit}' has no system '{step.Field}'";
                }

                actual = value.ToString("F2", CultureInfo.InvariantCulture);
                holds = Compare(value, step.Op, step.Number!.Value); // the parser accepts only a number here

                break;
        }

        return holds
            ? null
            : $"expectation failed: {step.Unit} {step.Field}: expected {step.Op} {step.Value}, actual {actual}";
    }

    /// <summary>
    /// Writes <c>text</c> to <c>&lt;folder&gt;/&lt;name&gt;.txt</c>, creating the folder when it is missing and
    /// replacing a file of that name. Returns the problem when the file cannot be written, else null.
    /// </summary>
    public static string? Write(string folder, string name, string text)
    {
        var file = System.IO.Path.Combine(folder, name + ".txt");
        try
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(file, text, new UTF8Encoding(false));

            return null;
        }
        catch (Exception e) // a file that cannot be written is a problem, whatever the reason (INV-0002, INV-0007)
        {
            return $"cannot write '{file}': {e.Message}";
        }
    }

    static bool Compare(double actual, string op, double expected) =>
        op switch
        {
            "<" => actual < expected,
            "<=" => actual <= expected,
            ">=" => actual >= expected,
            ">" => actual > expected,
            _ => actual == expected,
        };

    static string Flag(bool value) => value ? "true" : "false";

    // Two decimals; an angle that rounds to zero from below shows as 0.00, so a level unit never shows -0.00.
    static string Angle(float radians)
    {
        var text = radians.ToString("F2", CultureInfo.InvariantCulture);

        return text == "-0.00" ? "0.00" : text;
    }

    string Of(UnitCombatState? state) => state == null ? "none" : _names.Name(state.Unit);
}
