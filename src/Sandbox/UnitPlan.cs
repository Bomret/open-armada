using System;
using System.Collections.Generic;
using System.Linq;
using OpenArmada.Assets;

namespace OpenArmada.Sandbox;

// A unit of the scene: the placed object, what it is, and whether it can move. Movable is false when the
// definition's physics definition is missing (the unit stays selectable; orders skip it).
public sealed record SelectableUnit(PlacedObject Object, Unit Unit, bool Movable);

// The units among a scene plan's objects, with the gaps found while telling them apart (INV-0002).
public sealed class UnitPlan
{
    const string MissingPhysics = "physics definition missing, units cannot move";
    const string BadStarbase = "is_starbase is no whole number, units taken as ships";

    public IReadOnlyList<SelectableUnit> Units { get; }
    public IReadOnlyList<ReportEntry> Reports { get; }

    UnitPlan(IReadOnlyList<SelectableUnit> units, IReadOnlyList<ReportEntry> reports)
    {
        Units = units;
        Reports = reports;
    }

    public static UnitPlan Build(GameFileSystem files, SandboxPlan plan)
    {
        var units = new List<SelectableUnit>();
        // cause -> distinct names and object count, in order of first sight
        var gaps = new Dictionary<string, (List<string> Names, int Count)>();
        void Gap(string cause, string name)
        {
            if (!gaps.TryGetValue(cause, out var g)) gaps[cause] = g = (new List<string>(), 0);
            if (!g.Names.Contains(name)) g.Names.Add(name);
            gaps[cause] = (g.Names, g.Count + 1);
        }
        var byClass = new Dictionary<string, ResolvedDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var o in plan.Objects)
        {
            if (!byClass.TryGetValue(o.Class, out var definition)) byClass[o.Class] = definition = ResolvedDefinition.Resolve(files, o.Class + ".odf");
            Unit? unit;
            try { unit = UnitRules.Classify(definition); }
            catch (DefinitionException)
            {
                Gap(BadStarbase, o.Class);
                unit = new Ship(definition.Has("unitName") ? definition.GetString("unitName") : o.Class);
            }
            if (unit == null) continue;
            var movable = files.Find(definition.GetString("physicsFile")) != null;
            if (!movable) Gap(MissingPhysics, o.Class);
            units.Add(new SelectableUnit(o, unit, movable));
        }
        return new UnitPlan(units, gaps.Select(g => new ReportEntry(g.Key, g.Value.Names, g.Value.Count)).ToList());
    }
}
