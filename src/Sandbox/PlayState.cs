using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
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
    readonly Dictionary<PlacedObject, UnitCombatState> _states = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// <c>names</c> holds the addressable units; <c>pose</c> reads a unit's current pose; <c>states</c> are the
    /// combat states of the units in the combat world at the start of the run.
    /// </summary>
    public PlayState(UnitNames names, Func<PlacedObject, ShipPose> pose, IEnumerable<UnitCombatState> states)
    {
        _names = names;
        _pose = pose;
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
    /// The state dump: one line per addressable unit in plan order, <c>\n</c>-terminated, invariant culture, two
    /// decimals, the systems in a fixed order, and no time of day. A unit without combat state shows
    /// <c>state=none</c> after its position. Fields, space-separated: name, team, pos (x,y,z), shields, crew, one
    /// <c>System=hit points/maximum/intact|destroyed</c> per system the unit has, derelict, immobile, destroyed,
    /// alert, order, target.
    /// </summary>
    public string Dump()
    {
        var text = new StringBuilder();
        foreach (var unit in _names.Units)
        {
            var p = _pose(unit).Position;
            text.Append(
                CultureInfo.InvariantCulture,
                $"{_names.Name(unit)} team={unit.Team} pos={p.X:F2},{p.Y:F2},{p.Z:F2}"
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
                    $" derelict={Flag(s.Derelict)} immobile={Flag(s.Immobile)} destroyed={Flag(s.Destroyed)}"
                        + $" alert={s.Alert} order={Of(s.Order)} target={Of(s.Target)}"
                );
            }
            else
            {
                text.Append(" state=none");
            }

            text.Append('\n');
        }

        return text.ToString();
    }

    /// <summary>
    /// Checks one <c>expect</c> step. Returns null when it holds, else the problem without script or line:
    /// an unresolvable unit, a unit without combat state, a unit that was destroyed and removed (only
    /// <c>destroyed</c> can be asked of it), a system the unit lacks, or the expected and the actual value.
    /// </summary>
    public string? Expect(ExpectStep step)
    {
        var (unit, problem) = _names.Resolve(step.Unit);
        if (unit == null)
            return problem;
        if (!_states.TryGetValue(unit, out var state))
            return $"unit '{step.Unit}' has no combat state";
        if (state.Destroyed && step.Field != "destroyed")
            return $"unit '{step.Unit}' was destroyed and removed";
        string actual;
        bool holds;
        switch (step.Field)
        {
            case "destroyed":
                actual = Flag(state.Destroyed);
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

    string Of(UnitCombatState? state) => state == null ? "none" : _names.Name(state.Unit);
}
