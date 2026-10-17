using System.Collections.Generic;
using System.Linq;

namespace OpenArmada.Sandbox;

/// <summary>
/// The names a play script addresses units by. A unit is named by its map label, or by its class when the label
/// is empty. When several units share a name, <c>name#k</c> is the k-th of them (from 1) in the order the units
/// were given, and the bare name is ambiguous. A label that itself reads like such a name (<c>ship#2</c> beside
/// two units named <c>ship</c>) is ambiguous too, and <see cref="Name"/> gives it in quotes.
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

        // A label that equals another unit's generated name would repeat it in the dump: show the label quoted.
        // Resolve reports that name as a problem, so no script addresses either unit by it.
        foreach (var unit in Units)
        {
            if (unit.Label.Length > 0 && _names[unit] == unit.Label && _names.Count(n => n.Value == unit.Label) > 1)
            {
                _names[unit] = $"\"{unit.Label}\"";
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
        var hash = name.LastIndexOf('#');
        List<PlacedObject>? several = null;
        var k = 0;
        var generated =
            hash > 0
            && _byName.TryGetValue(name[..hash], out several)
            && int.TryParse(name[(hash + 1)..], out k)
            && k >= 1
            && k <= several.Count
            && several.Count > 1
                ? several[k - 1]
                : null;
        if (_byName.TryGetValue(name, out var same))
        {
            if (generated != null)
                return (null, $"'{name}' is both a label and the {Ordinal(k)} '{name[..hash]}'");

            return same.Count == 1
                ? (same[0], null)
                : (null, $"'{name}' names {same.Count} units, use {name}#1 to {name}#{same.Count}");
        }

        return generated != null ? (generated, null) : (null, $"unknown unit '{name}'");

        static string Ordinal(int n) =>
            n switch
            {
                1 => "1st",
                2 => "2nd",
                3 => "3rd",
                _ => $"{n}th",
            };
    }
}
