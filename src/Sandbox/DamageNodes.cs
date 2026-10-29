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
    // `borg` is the game's seventh name; it shows on a ship of the race named `borg` (0x601240). Assimilation is out of
    // scope for the sandbox, so a unit's `borg` node stays hidden; the asset browser shows it on request.
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
        var yellow = unit.Profile.Race.CrewYellowStatus;
        var defined = unit.Profile.DefinedMaximumCrew;
        var ratio = yellow > 0 ? (defined > 0 ? unit.Crew / defined : 1) / yellow : double.PositiveInfinity;

        return ShowingNodes(model, ratio, unit.SystemDestroyed, borg: false);
    }

    /// <summary>
    /// The nodes of <c>model</c>'s damage group that show for a crew ratio, the destroyed systems, and whether the ship
    /// is of the race named <c>borg</c>, in the model's node order. <c>crewRatio</c> is crew / maximumCrew /
    /// crewYellowStatus (<see cref="Showing"/>); positive infinity shows no crew node. The <c>borg</c> node's whole
    /// subtree shows while <c>borg</c> holds (0x4677b0).
    /// </summary>
    public static IReadOnlyList<Sod.Node> ShowingNodes(
        Sod? model,
        double crewRatio,
        Func<string, bool> systemDestroyed,
        bool borg
    )
    {
        var nodes = model?.Nodes ?? [];
        var damage = Group(model);
        if (model == null || damage == null)
            return [];

        var showing = new HashSet<Sod.Node>();
        if (Named(model, damage, "crew") is { } crew)
        {
            var step = 0.0;
            foreach (var node in Chain(model, crew))
            {
                if (crewRatio + step < 1)
                    showing.Add(node);
                step += CrewStep;
            }
        }

        foreach (var (name, system) in SystemNodes)
        {
            if (systemDestroyed(system) && Named(model, damage, name) is { } root)
                Subtree(model, root, showing);
        }

        if (borg && Named(model, damage, "borg") is { } borgNode)
            Subtree(model, borgNode, showing);

        return nodes.Where(showing.Contains).ToList();
    }

    /// <summary>
    /// What of <c>model</c>'s damage group can show art: the most crew chain nodes below the <c>crew</c> node that any
    /// crew ratio shows, the systems whose node has nodes below it, and whether the <c>borg</c> node has nodes below
    /// it. Nothing for a model without a damage group.
    /// </summary>
    public static DamageArt Art(Sod? model)
    {
        if (model == null || Group(model) is not { } damage)
            return new DamageArt(0, [], false);

        var shown = Named(model, damage, "crew") is { } crew
            ? ShowingNodes(model, 0, _ => false, false).Count(n => Chain(model, crew).Skip(1).Contains(n))
            : 0;
        var systems = SystemNodes
            .Where(s => Named(model, damage, s.Node) is { } node && model.ChildrenOf(node).Any())
            .Select(s => s.System)
            .ToList();
        var borg = Named(model, damage, "borg") is { } b && model.ChildrenOf(b).Any();

        return new DamageArt(shown, systems, borg);
    }

    /// <summary>
    /// A crew ratio at which the crew chain shows the <c>crew</c> node and <c>stages</c> nodes below it; 1, which
    /// shows none, for 0 stages.
    /// </summary>
    public static double CrewRatioShowing(int stages) => stages <= 0 ? 1 : 1 - CrewStep * (stages + 0.5);

    static Sod.Node? Group(Sod? model) =>
        model?.Nodes.FirstOrDefault(n => n.Name.Equals("damage", StringComparison.OrdinalIgnoreCase));

    // The children are the ones the builder hangs from a node, so the first node of a name owns its children.
    static Sod.Node? Named(Sod model, Sod.Node damage, string name) =>
        model.ChildrenOf(damage).FirstOrDefault(n => n.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    // The crew node and the first-child chain below it; a chain that loops stops at the first repeat.
    static IEnumerable<Sod.Node> Chain(Sod model, Sod.Node crew)
    {
        var seen = new HashSet<Sod.Node>();
        for (var node = crew; node != null && seen.Add(node); node = model.ChildrenOf(node).FirstOrDefault())
            yield return node;
    }

    static void Subtree(Sod model, Sod.Node root, HashSet<Sod.Node> showing)
    {
        showing.Add(root);
        showing.UnionWith(model.Descendants(root));
    }
}

/// <summary>
/// What a model's damage group can show (<see cref="DamageNodes.Art"/>): <c>CrewStages</c> crew chain nodes below the
/// <c>crew</c> node, the <c>Systems</c> (<see cref="SystemNames"/>) whose node holds art, and whether the <c>borg</c>
/// node holds art.
/// </summary>
public sealed record DamageArt(int CrewStages, IReadOnlyList<string> Systems, bool Borg);
