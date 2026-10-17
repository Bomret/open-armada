using System;
using System.Collections.Generic;
using System.Numerics;
using OpenArmada.Assets;
using OpenArmada.Formats;

namespace OpenArmada.Sandbox;

// A sprite node resolved onto a model node: its sprite (region, flipbook, blend), its size, its colour, whether it
// faces the camera, and its node animation (null: none).
public sealed record SpriteNode(
    Sod.Node Node,
    SpriteArt Sprite,
    float Width,
    float Height,
    Vector3 Colour,
    bool Billboard,
    NodeAnimation? Animation
)
{
    // Whether the sprite is drawn, and its colour: the line's colour times the animation's.
    public (bool Draw, Vector3 Colour) At(double seconds, bool loop)
    {
        var (draw, colour) = Animation?.At(seconds, loop) ?? (true, Vector3.One);
        return (draw, Colour * colour);
    }
}

// The sprite nodes the install's sprite tables attach to a model, and the gaps on the way, once per cause with the
// names and the number of nodes each touches (INV-0002). A sprite node no model node names is silent.
public sealed class SpriteNodes
{
    const string Unread = "sprite node line cannot be read, it is not drawn";
    const string MissingSprite = "sprite node names a sprite no table defines, it is not drawn";
    const string MissingAnimation = "sprite node names an animation no table defines, its sprite is not animated";

    // stand-in: `const` (87 install lines) is defined by no table; it reads as no animation.
    const string NoAnimation = "const";

    public IReadOnlyList<SpriteNode> Nodes { get; }
    public IReadOnlyList<ReportEntry> Reports { get; }

    SpriteNodes(IReadOnlyList<SpriteNode> nodes, IReadOnlyList<ReportEntry> reports) =>
        (Nodes, Reports) = (nodes, reports);

    // Names match ignoring case, through the name the built node carries (NodeName), as hardpoints do; when two
    // tables define a sprite node of one name, the first table in name order wins, as shot-sprite rows do.
    public static SpriteNodes For(GameFileSystem files, Sod model)
    {
        var gaps = new Gaps();
        var lines = new Dictionary<string, (SpriteTable Table, SpriteNodeLine Line)>(StringComparer.OrdinalIgnoreCase);
        foreach (var table in new SpriteTableKind().Loaded(files))
        {
            foreach (var line in table.SpriteNodes)
                lines.TryAdd(NodeName.Of(line.Node), (table, line));
            foreach (var note in table.UnreadSpriteNodes)
                gaps.Add(Unread, note);
        }
        var nodes = new List<SpriteNode>();
        Dictionary<string, (SpriteTable Table, Sprite Sprite)>? rows = null;
        foreach (var node in model.Nodes)
        {
            if (!lines.TryGetValue(NodeName.Of(node.Name), out var hit))
                continue;
            var line = hit.Line;
            rows ??= SpriteArt.Rows(files);
            if (!rows.TryGetValue(line.Sprite, out var row))
            {
                gaps.Add(MissingSprite, line.Sprite, node);
                continue;
            }
            NodeAnimation? animation = null;
            if (hit.Table.Animations.TryGetValue(line.Animation, out var a))
                animation = new NodeAnimation(a);
            else if (!line.Animation.Equals(NoAnimation, StringComparison.OrdinalIgnoreCase))
                gaps.Add(MissingAnimation, line.Animation, node);
            nodes.Add(
                new SpriteNode(
                    node,
                    SpriteArt.Of(row.Table, row.Sprite),
                    line.Width,
                    line.Height,
                    line.Colour,
                    line.Billboard,
                    animation
                )
            );
        }
        return new SpriteNodes(nodes, gaps.Entries);
    }
}
