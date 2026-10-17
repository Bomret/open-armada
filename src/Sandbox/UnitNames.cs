using System.Collections.Generic;
using System.Linq;

namespace OpenArmada.Sandbox;

/// <summary>
/// The names a play script addresses units by. A unit is named by its map label, or by its class when the label
/// is empty. When several units share a name, <c>name#k</c> is the k-th of them (from 1) in the order the units
/// were given, and the bare name is ambiguous.
/// </summary>
public sealed class UnitNames
{
    readonly Dictionary<string, List<PlacedObject>> _byName = new();
    readonly Dictionary<PlacedObject, string> _names = new(ReferenceEqualityComparer.Instance);

    /// <summary><c>units</c> are the addressable units in plan order, taken once at the start of the run.</summary>
    public UnitNames(IEnumerable<PlacedObject> units)
    {
        Units = units.ToList();
        foreach (var unit in Units)
        {
            var name = unit.Label.Length > 0 ? unit.Label : unit.Class;
            if (!_byName.TryGetValue(name, out var same))
            {
                _byName[name] = same = [];
            }

            same.Add(unit);
        }

        foreach (var (name, same) in _byName)
        {
            for (var k = 0; k < same.Count; k++)
            {
                _names[same[k]] = same.Count == 1 ? name : $"{name}#{k + 1}";
            }
        }
    }

    /// <summary>The addressable units in plan order.</summary>
    public IReadOnlyList<PlacedObject> Units { get; }

    /// <summary>The name a play script addresses the unit by; the unit must be one of <see cref="Units"/>.</summary>
    public string Name(PlacedObject unit) => _names[unit];

    /// <summary>The unit that <c>name</c> addresses, or the problem: unknown, or a bare name that several units share.</summary>
    public (PlacedObject? Unit, string? Problem) Resolve(string name)
    {
        if (_byName.TryGetValue(name, out var same))
        {
            return same.Count == 1
                ? (same[0], null)
                : (null, $"'{name}' names {same.Count} units, use {name}#1 to {name}#{same.Count}");
        }

        var hash = name.LastIndexOf('#');
        if (
            hash > 0
            && _byName.TryGetValue(name[..hash], out var several)
            && int.TryParse(name[(hash + 1)..], out var k)
            && k >= 1
            && k <= several.Count
            && several.Count > 1
        )
            return (several[k - 1], null);

        return (null, $"unknown unit '{name}'");
    }
}
