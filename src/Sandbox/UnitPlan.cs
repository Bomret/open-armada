using System;
using System.Collections.Generic;
using OpenArmada.Assets;

namespace OpenArmada.Sandbox;

/// <summary>
/// A unit of the scene: the placed object, what it is, and whether it can move. Movable is false when the
/// definition's physics definition is missing (the unit stays selectable; orders skip it).
/// </summary>
public sealed record SelectableUnit
{
    /// <summary>The placed object of the unit.</summary>
    public required PlacedObject Object { get; init; }

    /// <summary>What the unit is: a <see cref="Ship"/> or a <see cref="Station"/>, with its name.</summary>
    public required Unit Unit { get; init; }

    /// <summary>Whether the install holds the unit's physics definition; false when it is missing or its physicsFile is no single value.</summary>
    public required bool Movable { get; init; }
}

/// <summary>The units among a scene plan's objects, with the gaps found while telling them apart (INV-0002).</summary>
public sealed class UnitPlan
{
    const string MissingPhysics = "physics definition missing, units cannot move";
    const string BadStarbase = "is_starbase is no whole number, units taken as ships";
    const string BadName = "unitName is no single value, class name used";
    const string BadPhysics = "physicsFile is no single value, units cannot move";

    /// <summary>The units among the selectable objects, in the plan's order.</summary>
    public IReadOnlyList<SelectableUnit> Units { get; }

    /// <summary>The gaps found while telling the units apart.</summary>
    public IReadOnlyList<ReportEntry> Reports { get; }

    UnitPlan(IReadOnlyList<SelectableUnit> units, IReadOnlyList<ReportEntry> reports)
    {
        Units = units;
        Reports = reports;
    }

    /// <summary><c>selectable</c> tells whether an object can be selected (its model is on screen); the others are no units.</summary>
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

                unit = station ? new Station { Name = name } : new Ship { Name = name };
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

            units.Add(
                new SelectableUnit
                {
                    Object = o,
                    Unit = unit,
                    Movable = movable,
                }
            );
        }

        return new UnitPlan(units, Gaps.Logged(gaps.Entries));
    }
}
