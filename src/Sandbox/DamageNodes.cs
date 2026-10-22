using System;
using System.Collections.Generic;
using System.Linq;
using OpenArmada.Formats;

namespace OpenArmada.Sandbox;

/// <summary>
/// Which nodes of a ship model's <c>damage</c> group show (INV-0011: decided here, the view only toggles nodes). The
/// game recomputes this every frame from the ship's state and draws no random part (0x4677b0, called from 0x472120),
/// so a node turns off again on repair or recrew.
/// </summary>
public static class DamageNodes
{
    // The ship systems' damage nodes by name, matched whole and ignoring case (class init 0x45fea0, names at
    // 0x600518..0x600548). The stock models name the shield generator's node `shield`, which never matches, as in the game.
    // `borg` is the game's seventh name; assimilation is out of scope, so it stays hidden.
    static readonly (string Node, string System)[] SystemNodes =
    [
        ("engines", SystemNames.Engines),
        ("life", SystemNames.LifeSupport),
        ("sensors", SystemNames.Sensors),
        ("target", SystemNames.Weapons),
        ("shields", SystemNames.ShieldGenerator),
    ];

    // r + 0.2k below 1 shows the crew chain's node k (0x4677b0: fadds 0x5ccf30 per node, compare with 0x5ca758).
    const double CrewStep = 0.2f;

    /// <summary>
    /// The names of the nodes of <c>model</c>'s damage group that show for <c>unit</c>, in the model's node order; none
    /// for a model that is null or has no damage group. The crew node, the first-child chain below it, and each system
    /// node are looked up among the direct children of the damage group, which is stand-in (the game's search is not
    /// known beyond the stock models). The crew chain shows node k while
    /// crew / maximumCrew / crewYellowStatus + 0.2k is below 1 (0x444437, 0x4677b0), so a ratio of 0 shows five nodes; a
    /// race whose crewYellowStatus is 0 or less shows none. A system's whole subtree shows while the system is destroyed
    /// (0x4679b0). A chain that loops stops at the first repeat. The game compares the model with the ship's normal one
    /// before it shows anything (0x4677b0, first branch); the sandbox has only the normal model (stand-in).
    /// </summary>
    public static IReadOnlyList<string> Showing(Sod? model, UnitCombatState unit) =>
        ShowingNodes(model, unit).Select(n => n.Name).ToList();

    /// <summary>
    /// The nodes that <see cref="Showing"/> names, as the model's own node objects in the model's node order. Two
    /// nodes of one name under different parents are told apart here, which the names cannot do.
    /// </summary>
    public static IReadOnlyList<Sod.Node> ShowingNodes(Sod? model, UnitCombatState unit)
    {
        var nodes = model?.Nodes ?? [];
        var damage = nodes.FirstOrDefault(n => n.Name.Equals("damage", StringComparison.OrdinalIgnoreCase));
        if (model == null || damage == null)
            return [];

        var showing = new HashSet<Sod.Node>();
        // The children are the ones the builder hangs from a node, so the first node of a name owns its children.
        IEnumerable<Sod.Node> ChildrenOf(Sod.Node parent) => model.ChildrenOf(parent);
        Sod.Node? Named(string name) =>
            ChildrenOf(damage).FirstOrDefault(n => n.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        var yellow = unit.Profile.Race.CrewYellowStatus;
        if (yellow > 0 && Named("crew") is { } crew)
        {
            var defined = unit.Profile.DefinedMaximumCrew;
            var r = (defined > 0 ? unit.Crew / defined : 1) / yellow;
            var seen = new HashSet<Sod.Node>();
            var step = 0.0;
            for (var node = crew; node != null && seen.Add(node); node = ChildrenOf(node).FirstOrDefault())
            {
                if (r + step < 1)
                    showing.Add(node);
                step += CrewStep;
            }
        }

        foreach (var (name, system) in SystemNodes)
        {
            if (!unit.SystemDestroyed(system) || Named(name) is not { } root)
                continue;
            showing.Add(root);
            showing.UnionWith(model.Descendants(root));
        }

        return nodes.Where(showing.Contains).ToList();
    }
}
