using System;
using System.Collections.Generic;
using System.Linq;
using OpenArmada.Assets;

namespace OpenArmada.Sandbox;

// The movement values of the scene's ships, read from their physics definitions, and the gaps found
// while reading them (INV-0002). A ship with a gap has no entry: it stays selectable and never moves.
// A physics definition that is missing altogether is the unit plan's report.
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
        var gaps = new Dictionary<string, (List<string> Names, int Count)>();
        foreach (var unit in units)
        {
            if (unit.Unit is not Ship || !unit.Movable) continue;
            var o = unit.Object;
            ResolvedDefinition definition;
            try { definition = ResolvedDefinition.Resolve(files, plan.Resolved[o.Class].GetString("physicsFile")); }
            catch (Exception e) when (e is System.IO.FileNotFoundException or DefinitionException) { continue; } // the unit plan reported it
            var causes = new List<string>();
            var speed = Positive(definition, "impulseSpeed", causes);
            var yaw = Positive(definition, "yawRotationRate", causes);
            foreach (var cause in causes)
            {
                if (!gaps.TryGetValue(cause, out var g)) gaps[cause] = g = (new List<string>(), 0);
                if (!g.Names.Contains(o.Class)) g.Names.Add(o.Class);
                gaps[cause] = (g.Names, g.Count + 1);
            }
            if (speed == null || yaw == null) continue;
            // stand-in: no bank without maxRoll (the Borg physics definitions have none); a maxRoll that is no number is the same
            double? roll = null;
            try { if (definition.Has("maxRoll")) roll = definition.GetFloat("maxRoll"); }
            catch (DefinitionException) { }
            physics[o] = new PhysicsValues(speed.Value, yaw.Value, roll);
        }
        return new MovePlan(physics, gaps.Select(g => new ReportEntry(g.Key, g.Value.Names, g.Value.Count)).ToList());
    }

    // The key's value when it is a number above 0; otherwise null, and the cause is added.
    static double? Positive(ResolvedDefinition definition, string key, List<string> causes)
    {
        if (!definition.Has(key)) { causes.Add($"physics definition lacks {key}, ships cannot move"); return null; }
        try { if (definition.GetFloat(key) is var value and > 0) return value; }
        catch (DefinitionException) { }
        causes.Add($"physics definition's {key} is no number above 0, ships cannot move");
        return null;
    }
}
