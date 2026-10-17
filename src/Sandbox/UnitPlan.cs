using System;
using System.Collections.Generic;
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
    const string BadName = "unitName is no single value, class name used";
    const string BadPhysics = "physicsFile is no single value, units cannot move";

    public IReadOnlyList<SelectableUnit> Units { get; }
    public IReadOnlyList<ReportEntry> Reports { get; }

    UnitPlan(IReadOnlyList<SelectableUnit> units, IReadOnlyList<ReportEntry> reports)
    {
        Units = units;
        Reports = reports;
    }

    // `selectable` tells whether an object can be selected (its model is on screen); the others are no units.
    public static UnitPlan Build(GameFileSystem files, SandboxPlan plan, Func<PlacedObject, bool> selectable)
    {
        var units = new List<SelectableUnit>();
        var gaps = new Gaps();
        foreach (var o in plan.Objects)
        {
            if (!selectable(o) || !plan.Resolved.TryGetValue(o.Class, out var definition))
                continue;
            Unit? unit;
            try
            {
                unit = UnitRules.Classify(definition);
            }
            catch (DefinitionException)
            {
                // a key of the definition is unreadable: report each one by its own cause and fall back
                // to the class name and to a ship
                var name = o.Class;
                if (definition.Has("unitName"))
                {
                    try
                    {
                        name = definition.GetString("unitName");
                    }
                    catch (DefinitionException)
                    {
                        gaps.Add(BadName, o.Class);
                    }
                }
                var station = false;
                if (definition.Has("is_starbase"))
                {
                    try
                    {
                        station = definition.GetInt("is_starbase") != 0;
                    }
                    catch (DefinitionException)
                    {
                        gaps.Add(BadStarbase, o.Class);
                    }
                }
                unit = station ? new Station(name) : new Ship(name);
            }
            if (unit == null)
                continue;
            bool movable;
            try
            {
                movable = files.Find(definition.GetString("physicsFile")) != null;
                if (!movable)
                {
                    gaps.Add(MissingPhysics, o.Class);
                }
            }
            catch (DefinitionException)
            {
                movable = false;
                gaps.Add(BadPhysics, o.Class);
            }
            units.Add(new SelectableUnit(o, unit, movable));
        }
        return new UnitPlan(units, gaps.Entries);
    }
}
