using System;
using System.Collections.Generic;
using System.Linq;
using OpenArmada.Assets;
using OpenArmada.Formats;

namespace OpenArmada.Sandbox;

/// <summary>A damage emitter: an emitter node of the model, its <c>@emitter</c> definition, and its particle's sprite node.</summary>
public sealed record DamageEmitter
{
    /// <summary>The model's emitter node (type 12), under its damage group.</summary>
    public required Sod.Node Node { get; init; }

    /// <summary>The <c>@emitter</c> block the node names; its type is spray.</summary>
    public required SpriteEmitter Definition { get; init; }

    /// <summary>
    /// The <c>@sprite_node</c> the definition names, resolved onto the emitter node: the particle's sprite, its size
    /// (width and height), its colour, and its node animation.
    /// </summary>
    public required SpriteNode Sprite { get; init; }
}

/// <summary>
/// The damage emitters of a ship model: its emitter nodes under the <c>damage</c> group (0x59b650, 0x58a960), each
/// with the <c>@emitter</c> block it names and that block's sprite node, and the gaps on the way, once per cause with
/// the names and the number of nodes each touches (INV-0002). An emitter whose definition or sprite node is missing,
/// or whose type is not spray, is reported and left out; the model still loads. stand-in: only spray emitters play
/// (cloud and beam are read and skipped). Which of the emitters spray is decided by <see cref="DamageNodes"/>: an
/// emitter node under a hidden damage node does not.
/// </summary>
/// <param name="emitters">The emitters that spray, in the model's node order.</param>
/// <param name="reports">The gaps.</param>
public sealed class DamageEmitters(IReadOnlyList<DamageEmitter> emitters, IReadOnlyList<ReportEntry> reports)
{
    const string MissingDefinition = "emitter missing from the sprite tables, it sprays nothing";
    const string MissingSprite = "emitter names a sprite node no table defines, it sprays nothing";
    const string NotPlayed = "emitter type is not played, it sprays nothing";

    /// <summary>The emitters that spray, in the model's node order.</summary>
    public IReadOnlyList<DamageEmitter> Emitters { get; } = emitters;

    /// <summary>The gaps, one entry per cause.</summary>
    public IReadOnlyList<ReportEntry> Reports { get; } = reports;

    /// <summary>
    /// Resolves the damage emitters of <paramref name="model"/> against the sprite tables of
    /// <paramref name="files"/>, read once per install. An emitter and its sprite node match by name ignoring case;
    /// when two tables define one name the first table in name order wins.
    /// </summary>
    public static DamageEmitters For(GameFileSystem files, Sod model)
    {
        var damage = model.Nodes.FirstOrDefault(n => n.Name.Equals("damage", StringComparison.OrdinalIgnoreCase));
        if (damage == null)
            return new DamageEmitters([], []);
        var under = new HashSet<Sod.Node>();
        var pending = new Stack<Sod.Node>(model.ChildrenOf(damage));
        while (pending.Count > 0)
        {
            var node = pending.Pop();
            if (!under.Add(node))
                continue;
            foreach (var child in model.ChildrenOf(node))
                pending.Push(child);
        }

        var definitions = new Dictionary<string, SpriteEmitter>(StringComparer.OrdinalIgnoreCase);
        var lines = new Dictionary<string, (SpriteTable Table, SpriteNodeLine Line)>(StringComparer.OrdinalIgnoreCase);
        foreach (var table in new SpriteTableKind().Loaded(files))
        {
            foreach (var emitter in table.Emitters)
                definitions.TryAdd(emitter.Name, emitter);
            foreach (var line in table.SpriteNodes)
                lines.TryAdd(line.Node, (table, line));
        }

        var gaps = new Gaps();
        var found = new List<DamageEmitter>();
        Dictionary<string, (SpriteTable Table, Sprite Sprite)>? rows = null;
        foreach (var node in model.Nodes.Where(n => n.Type == Sod.NodeType.Emitter && under.Contains(n)))
        {
            if (!definitions.TryGetValue(node.Emitter, out var definition))
            {
                gaps.Add(MissingDefinition, node.Emitter, node);

                continue;
            }

            if (definition.Kind != EmitterKind.Spray)
            {
                gaps.Add(NotPlayed, $"{definition.Name} ({definition.Kind.ToString().ToLowerInvariant()})", node);

                continue;
            }

            if (!lines.TryGetValue(definition.Sprite, out var hit))
            {
                gaps.Add(MissingSprite, definition.Sprite, node);

                continue;
            }

            rows ??= SpriteArt.Rows(files);
            if (SpriteNodes.Resolve(node, hit.Table, hit.Line, rows, gaps) is { } sprite)
            {
                found.Add(
                    new DamageEmitter
                    {
                        Node = node,
                        Definition = definition,
                        Sprite = sprite,
                    }
                );
            }
        }

        return new DamageEmitters(found, gaps.Entries);
    }
}
