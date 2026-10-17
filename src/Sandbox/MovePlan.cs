using System;
using System.Collections.Generic;
using OpenArmada.Assets;

namespace OpenArmada.Sandbox;

// The movement values of the scene's ships, read from their physics definitions, and the gaps found
// while reading them (INV-0002). A ship with a gap has no entry: it stays selectable and never moves.
// A physics definition that is missing altogether is the unit plan's report (such a ship is not Movable); one
// the install holds that cannot be read is a gap here.
public sealed class MovePlan
{
    public IReadOnlyDictionary<PlacedObject, PhysicsValues> Physics { get; }
    public IReadOnlyList<ReportEntry> Reports { get; }

    MovePlan(IReadOnlyDictionary<PlacedObject, PhysicsValues> physics, IReadOnlyList<ReportEntry> reports)
    {
        Physics = physics;
        Reports = reports;
    }

    public static MovePlan Build(SandboxPlan plan, IEnumerable<SelectableUnit> units, GameFileSystem files)
    {
        var physics = new Dictionary<PlacedObject, PhysicsValues>();
        var gaps = new Gaps();
        foreach (var unit in units)
        {
            if (unit.Unit is not Ship || !unit.Movable)
                continue;
            var o = unit.Object;
            ResolvedDefinition definition;
            try
            {
                definition = ResolvedDefinition.Resolve(files, plan.Resolved[o.Class].GetString("physicsFile"));
            }
            catch (Exception) // a physics definition the install holds but that cannot be read is a gap, whatever the reason (INV-0002)
            {
                gaps.Add("physics definition cannot be read, ships cannot move", o.Class);

                continue;
            }

            var speed = Positive(definition, "impulseSpeed", o.Class, gaps);
            var yaw = Positive(definition, "yawRotationRate", o.Class, gaps);
            if (speed == null || yaw == null)
                continue;
            // stand-in: no bank without maxRoll (the Borg physics definitions have none); a maxRoll of 0 is no bank
            // too. A maxRoll that is no number or is negative is the same, and a gap says so (a negative one would
            // bank the ship out of the turn)
            double? roll = null;
            if (definition.Has("maxRoll"))
            {
                try
                {
                    if (definition.GetFloat("maxRoll") is var value and >= 0)
                    {
                        roll = value;
                    }
                }
                catch (DefinitionException) { }

                if (roll == null)
                {
                    gaps.Add("physics definition's maxRoll is no number of 0 or more, ships do not bank", o.Class);
                }
            }

            physics[o] = new PhysicsValues(speed.Value, yaw.Value, roll);
        }

        return new MovePlan(physics, gaps.Entries);
    }

    // The key's value when it is a number above 0; otherwise null, and a gap of the cause and class is added.
    static double? Positive(ResolvedDefinition definition, string key, string unitClass, Gaps gaps)
    {
        if (!definition.Has(key))
        {
            gaps.Add($"physics definition lacks {key}, ships cannot move", unitClass);

            return null;
        }

        try
        {
            if (definition.GetFloat(key) is var value and > 0)
                return value;
        }
        catch (DefinitionException) { }

        gaps.Add($"physics definition's {key} is no number above 0, ships cannot move", unitClass);

        return null;
    }
}
