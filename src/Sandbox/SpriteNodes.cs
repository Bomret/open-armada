using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using OpenArmada.Assets;
using OpenArmada.Formats;

namespace OpenArmada.Sandbox;

/// <summary>A sprite node resolved onto a model node.</summary>
public sealed record SpriteNode
{
    /// <summary>The model node the sprite node decorates.</summary>
    public required Sod.Node Node { get; init; }

    /// <summary>The sprite: its region, its flipbook, and its blend.</summary>
    public required SpriteArt Sprite { get; init; }

    /// <summary>The sprite's width in model units.</summary>
    public required float Width { get; init; }

    /// <summary>The sprite's height in model units.</summary>
    public required float Height { get; init; }

    /// <summary>The line's colour; the node animation multiplies it.</summary>
    public required Vector3 Colour { get; init; }

    /// <summary>Whether the sprite faces the camera.</summary>
    public required bool Billboard { get; init; }

    /// <summary>The node animation; null when the line names none.</summary>
    public required NodeAnimation? Animation { get; init; }

    /// <summary>
    /// Whether the sprite is drawn <paramref name="seconds"/> after it appeared, and its colour: the line's colour
    /// times the animation's. Without an animation it is always drawn in the line's colour.
    /// </summary>
    public (bool Draw, Vector3 Colour) At(double seconds, bool loop)
    {
        var (draw, colour) = Animation?.At(seconds, loop) ?? (true, Vector3.One);

        return (draw, Colour * colour);
    }
}

/// <summary>
/// The sprite nodes the install's sprite tables attach to a model, and the gaps on the way, once per cause with the
/// names and the number of nodes each touches (INV-0002). A sprite node no model node names is silent, and so is an
/// unreadable line naming no node of the model or naming a node a readable line draws. Unreadable lines match nodes by
/// the same rule as readable ones.
/// </summary>
/// <param name="nodes">The resolved sprite nodes.</param>
/// <param name="reports">The gaps.</param>
public sealed class SpriteNodes(IReadOnlyList<SpriteNode> nodes, IReadOnlyList<ReportEntry> reports)
{
    const string Unread = "sprite node line cannot be read, it is not drawn";
    const string MissingSprite = "sprite node names a sprite no table defines, it is not drawn";
    const string MissingAnimation = "sprite node names an animation no table defines, its sprite is not animated";

    // stand-in: `const` (87 install lines) is defined by no table; it reads as no animation.
    const string NoAnimation = "const";

    /// <summary>The resolved sprite nodes, in the model's node order.</summary>
    public IReadOnlyList<SpriteNode> Nodes { get; } = nodes;

    /// <summary>The gaps, one entry per cause.</summary>
    public IReadOnlyList<ReportEntry> Reports { get; } = reports;

    /// <summary>
    /// Resolves the sprite nodes of <paramref name="model"/> from the sprite tables of <paramref name="files"/>,
    /// read once per install. Names match ignoring case, through the name the built node carries (NodeName), as
    /// hardpoints do; when two tables define a sprite node of one name, the first table in name order wins, as
    /// shot-sprite rows do. A sprite or an animation no table defines, and an unreadable line, are gaps.
    /// stand-in: a node with no sprite node of its own name takes the one of its name without a trailing
    /// "_&lt;digits&gt;": ship models number the instances of one light ("greenconst", "greenconst_2", "greenconst_3").
    /// </summary>
    public static SpriteNodes For(GameFileSystem files, Sod model)
    {
        var gaps = new Gaps();
        var lines = new Dictionary<string, (SpriteTable Table, SpriteNodeLine Line)>(StringComparer.OrdinalIgnoreCase);
        var unread = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var table in new SpriteTableKind().Loaded(files))
        {
            foreach (var line in table.SpriteNodes)
            {
                lines.TryAdd(NodeName.Of(line.Node), (table, line));
            }

            foreach (var (name, note) in table.UnreadSpriteNodes.Where(u => u.Node.Length > 0))
            {
                if (!unread.TryGetValue(NodeName.Of(name), out var notes))
                {
                    unread[NodeName.Of(name)] = notes = new List<string>();
                }

                notes.Add(note);
            }
        }

        var nodes = new List<SpriteNode>();
        Dictionary<string, (SpriteTable Table, Sprite Sprite)>? rows = null;
        foreach (var node in model.Nodes)
        {
            var name = NodeName.Of(node.Name);
            if (!lines.TryGetValue(name, out var hit) && !lines.TryGetValue(Unnumbered(name), out hit))
            {
                if (unread.TryGetValue(name, out var notes) || unread.TryGetValue(Unnumbered(name), out notes))
                {
                    foreach (var note in notes)
                    {
                        gaps.Add(Unread, note, node);
                    }
                }

                continue;
            }

            rows ??= SpriteArt.Rows(files);
            if (Resolve(node, hit.Table, hit.Line, rows, gaps) is { } resolved)
            {
                nodes.Add(resolved);
            }
        }

        return new SpriteNodes(nodes, gaps.Entries);
    }

    // The sprite node `line` of `table` drawn on `node`; null (a gap) when no table defines its sprite. A missing
    // animation is a gap too, and the sprite then plays none.
    internal static SpriteNode? Resolve(
        Sod.Node node,
        SpriteTable table,
        SpriteNodeLine line,
        Dictionary<string, (SpriteTable Table, Sprite Sprite)> rows,
        Gaps gaps
    )
    {
        if (!rows.TryGetValue(line.Sprite, out var row))
        {
            gaps.Add(MissingSprite, line.Sprite, node);

            return null;
        }

        NodeAnimation? animation = null;
        if (table.Animations.TryGetValue(line.Animation, out var a))
        {
            animation = new NodeAnimation(a);
        }
        else if (!line.Animation.Equals(NoAnimation, StringComparison.OrdinalIgnoreCase))
        {
            gaps.Add(MissingAnimation, line.Animation, node);
        }

        return new SpriteNode
        {
            Node = node,
            Sprite = SpriteArt.Of(row.Table, row.Sprite),
            Width = line.Width,
            Height = line.Height,
            Colour = line.Colour,
            Billboard = line.Billboard,
            Animation = animation,
        };
    }

    static string Unnumbered(string name) => Regex.Replace(name, @"_[0-9]+$", "");
}
