using System;
using System.Collections.Generic;
using System.Linq;
using OpenArmada.Formats;

namespace OpenArmada.Sandbox;

/// <summary>
/// The class load's check of each system's <c>&lt;system&gt;TargetHardpoints</c> against the unit's model (0x460f30): a
/// name the model holds no node of is left out of the list, so a repair spark never picks it.
/// </summary>
public static class TargetHardpoints
{
    /// <summary>
    /// The profile with each system's target hardpoints cut to the ones <paramref name="model"/> has a node of (the
    /// match ignores case), and the names left out, each once in the order first met. A null model leaves the profile
    /// as it is.
    /// </summary>
    public static (CombatProfile Profile, IReadOnlyList<string> Missing) Keep(CombatProfile profile, Sod? model)
    {
        if (model == null)
            return (profile, []);
        var nodes = model.Nodes.Select(n => n.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = profile
            .Systems.SelectMany(s => s.TargetHardpoints)
            .Where(h => !nodes.Contains(h))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (missing.Count == 0)
            return (profile, missing);
        var systems = profile
            .Systems.Select(s => s with { TargetHardpoints = s.TargetHardpoints.Where(nodes.Contains).ToList() })
            .ToList();

        return (profile with { Systems = systems }, missing);
    }
}
