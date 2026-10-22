using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;

namespace OpenArmada.Formats;

/// <summary>One cell of a sprite on its texture, in reference pixels.</summary>
public readonly record struct SpriteFrame
{
    /// <summary>The cell's horizontal offset on the texture.</summary>
    public required int U { get; init; }

    /// <summary>The cell's vertical offset on the texture.</summary>
    public required int V { get; init; }

    /// <summary>The cell's width.</summary>
    public required int W { get; init; }

    /// <summary>The cell's height.</summary>
    public required int H { get; init; }
}

/// <summary>
/// One sprite row. Reference sizes are 0 while no @reference is in force: the region is then in the
/// texture's own pixels. Table and Line say where the row stands (the included table, not the root).
/// </summary>
public sealed record Sprite
{
    /// <summary>The sprite's name, the first token of the row.</summary>
    public required string Name { get; init; }

    /// <summary>The texture name, the second token of the row, as written.</summary>
    public required string Texture { get; init; }

    /// <summary>The region's horizontal offset on the texture, a whole number of 0 or more.</summary>
    public required int U { get; init; }

    /// <summary>The region's vertical offset on the texture, a whole number of 0 or more.</summary>
    public required int V { get; init; }

    /// <summary>The region's width, a whole number of 0 or more.</summary>
    public required int W { get; init; }

    /// <summary>The region's height, a whole number of 0 or more.</summary>
    public required int H { get; init; }

    /// <summary>The reference width in force at the row (from @reference or @referenceWidth); 0 when none is.</summary>
    public required int ReferenceWidth { get; init; }

    /// <summary>
    /// The reference height in force at the row (from @reference or @referenceHeight); 0 when none is.
    /// </summary>
    public required int ReferenceHeight { get; init; }

    /// <summary>
    /// The value of the last @tmaterial directive above the row in its table; null when there is none.
    /// </summary>
    public required string? Material { get; init; }

    /// <summary>
    /// The value of the last @skip directive above the row in its table; null when there is none. The reader does not
    /// interpret it.
    /// </summary>
    public required string? Skip { get; init; }

    /// <summary>The animation name of the row's <c>@anim=</c> directive; null when the row has none.</summary>
    public required string? Anim { get; init; }

    /// <summary>True when the row carries the <c>@cursor</c> directive.</summary>
    public required bool Cursor { get; init; }

    /// <summary>
    /// The (x,y) of the row's <c>@origin=</c> directive, whole numbers of 0 or more; null when the row has none.
    /// </summary>
    public required (int X, int Y)? Origin { get; init; }

    /// <summary>The name of the table the row is written in, which is the included table for an included row.</summary>
    public required string Table { get; init; }

    /// <summary>The 1-based line of the row in <see cref="Table"/>.</summary>
    public required int Line { get; init; }
}

/// <summary>
/// An @animation: Field is what it animates (offset, colour, draw, width), Count its keyframes and
/// Length its seconds. Auto ("square", "row", "column") generates the keyframes of an offset animation
/// as a flipbook over the texture grid; otherwise Keyframes holds the explicit lines, split on blanks.
/// </summary>
public sealed record SpriteAnimation
{
    /// <summary>The name after <c>@animation</c>; sprites refer to it with <c>@anim=</c>.</summary>
    public required string Name { get; init; }

    /// <summary>What the animation animates: <c>offset</c>, <c>colour</c>, <c>draw</c> or <c>width</c>.</summary>
    public required string Field { get; init; }

    /// <summary>
    /// The keyframe count the field line declares; it is at most the character count of the table's text.
    /// </summary>
    public required int Count { get; init; }

    /// <summary>The animation's length in seconds.</summary>
    public required double Length { get; init; }

    /// <summary>The method of the field line: <c>step</c> or <c>linear</c>.</summary>
    public required string Method { get; init; }

    /// <summary>
    /// The <c>@auto</c> flipbook shape: <c>square</c>, <c>row</c> or <c>column</c>; null for explicit keyframes.
    /// </summary>
    public required string? Auto { get; init; }

    /// <summary>
    /// The explicit keyframe lines in file order, each split into its tokens. The read checks their number against
    /// <see cref="Count"/> only when <see cref="Auto"/> is null.
    /// </summary>
    public required IReadOnlyList<string[]> Keyframes { get; init; }

    /// <summary>
    /// The seconds of one keyframe: <see cref="Length"/> divided by <see cref="Count"/>, or 0 when the count is 0.
    /// </summary>
    public double FrameSeconds => Count > 0 ? Length / Count : 0;
}

/// <summary>How an emitter spawns its particles (<c>type=</c> of an <c>@emitter</c>, read at 0x599ab0).</summary>
public enum EmitterKind
{
    /// <summary>The block names no type, or one the reader does not know.</summary>
    Unknown = 0,

    /// <summary>A stream of particles from one point (<c>spray</c>).</summary>
    Spray = 1,

    /// <summary>A volume of particles (<c>cloud</c>).</summary>
    Cloud = 2,

    /// <summary>A beam (<c>beam</c>).</summary>
    Beam = 3,
}

/// <summary>
/// An @emitter block (read at 0x599ab0): its name and the fields of its keys up to @end_emitter. A key the block
/// leaves out keeps 0 (or "" for the sprite); the table's notes name an unknown key and a value that is no number.
/// </summary>
public sealed record SpriteEmitter
{
    /// <summary>The name after <c>@emitter</c>; an emitter node of a model names it.</summary>
    public required string Name { get; init; }

    /// <summary>The <c>type=</c> of the block.</summary>
    public EmitterKind Kind { get; init; }

    /// <summary>The <c>sprite=</c>: the name of a <c>@sprite_node</c> line, which gives the particle's sprite and size.</summary>
    public string Sprite { get; init; } = "";

    /// <summary>The <c>spread_angle=</c> in degrees: each of a particle's three start angles is within it of 0.</summary>
    public double SpreadAngle { get; init; }

    /// <summary>The <c>expansion_rate=</c> in world units per second, added to the sprite node's size.</summary>
    public double ExpansionRate { get; init; }

    /// <summary>The <c>velocity=</c> in world units per second along the node's +Z (0x5757b0).</summary>
    public double Velocity { get; init; }

    /// <summary>The <c>lifespan=</c> in seconds.</summary>
    public double Lifespan { get; init; }

    /// <summary>The <c>firing_period=</c> in seconds between spawns.</summary>
    public double FiringPeriod { get; init; }
}

/// <summary>
/// A @sprite_node line: the model node it decorates, its sprite and node animation, its size, its colour, and
/// whether it faces the camera.
/// </summary>
public sealed record SpriteNodeLine
{
    /// <summary>The name of the model node the line decorates, as written.</summary>
    public required string Node { get; init; }

    /// <summary>The name of the sprite row it draws.</summary>
    public required string Sprite { get; init; }

    /// <summary>The name of its node animation; <c>const</c> names none.</summary>
    public required string Animation { get; init; }

    /// <summary>The sprite's width in model units.</summary>
    public required float Width { get; init; }

    /// <summary>The sprite's height in model units.</summary>
    public required float Height { get; init; }

    /// <summary>The sprite's colour; a <c>colour</c> node animation multiplies it.</summary>
    public required Vector3 Colour { get; init; }

    /// <summary>Whether the sprite faces the camera (the line ends in <c>billboard</c>).</summary>
    public required bool Billboard { get; init; }

    /// <summary>The table the line stands in (the included table, not the root).</summary>
    public required string Table { get; init; }

    /// <summary>The line's number in that table, from 1.</summary>
    public required int Line { get; init; }
}

/// <summary>
/// A sprite table (.spr): "sprite_table" on the first line, '#' comments, @include of other tables,
/// directives and rows "Name Texture U V W H" with trailing directives. An include's sprites and
/// animations come where the @include stands; a table included again (by any path) adds nothing the
/// second time. Anything not recognized, an @animation, @emitter, or @include without its name, an include cycle, a missing table, an unterminated @emitter,
/// an animation without its field line or with more keyframes than its count (fewer is a note), and a flipbook
/// larger than its table's text or leaving its @reference area throw InvalidDataException naming the
/// table and line (INV-0002). A @sprite_node line that cannot be read is a note for that line alone.
/// </summary>
public sealed class SpriteTable
{
    static readonly string[] Fields = { "offset", "colour", "draw", "width" };
    static readonly string[] Methods = { "step", "linear" };
    static readonly string[] AutoShapes = { "square", "row", "column" };

    readonly List<Sprite> _sprites = new();
    readonly Dictionary<string, SpriteAnimation> _animations = new(StringComparer.OrdinalIgnoreCase);
    readonly List<SpriteEmitter> _emitters = new();
    readonly List<SpriteNodeLine> _nodes = new();
    readonly List<(string Node, string Note)> _unreadNodes = new();
    readonly List<string> _notes = new();

    /// <summary>The name of the root table given to <see cref="Read"/>.</summary>
    public string Name { get; }

    /// <summary>The sprite rows in reading order; an include's rows come where its <c>@include</c> stands.</summary>
    public IReadOnlyList<Sprite> Sprites => _sprites;

    /// <summary>
    /// The animations by name, ignoring case: those of this table and its includes, then those of the ambient tables
    /// that this table does not define itself.
    /// </summary>
    public IReadOnlyDictionary<string, SpriteAnimation> Animations => _animations;

    /// <summary>The <c>@emitter</c> blocks of the table and its includes in reading order.</summary>
    public IReadOnlyList<SpriteEmitter> Emitters => _emitters;

    /// <summary>The readable <c>@sprite_node</c> lines of the table and its includes in reading order.</summary>
    public IReadOnlyList<SpriteNodeLine> SpriteNodes => _nodes;

    /// <summary>
    /// The <c>@sprite_node</c> lines that cannot be read: the node each names ("" when none) and a note naming the
    /// table and line. <see cref="Notes"/> holds the notes too.
    /// </summary>
    public IReadOnlyList<(string Node, string Note)> UnreadSpriteNodes => _unreadNodes;

    /// <summary>
    /// What does not fail the table: a sprite naming an animation no read table defines, an unreadable
    /// <c>@sprite_node</c> line.
    /// </summary>
    public IReadOnlyList<string> Notes => _notes;

    SpriteTable(string name) => Name = name;

    /// <summary>
    /// <c>open</c> returns the text of a table by name (an include's name as written), null when there is none.
    /// <c>ambient</c> are other tables of the install: the game loads them all into one namespace, so a
    /// sprite may use an animation one of them defines (the table's own definitions win).
    /// </summary>
    public static SpriteTable Read(string name, Func<string, string?> open, IEnumerable<SpriteTable>? ambient = null)
    {
        var table = new SpriteTable(name);
        var text = open(name) ?? throw new InvalidDataException($"{name}: sprite table not found");
        table.Parse(name, text, open, new List<string>(), new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        foreach (var a in ambient ?? Array.Empty<SpriteTable>())
        {
            foreach (var (key, anim) in a._animations)
            {
                table._animations.TryAdd(key, anim);
            }
        }

        foreach (var s in table._sprites)
        {
            if (s.Anim == null)
                continue;
            if (!table._animations.TryGetValue(s.Anim, out var a))
            {
                table._notes.Add(
                    $"{s.Table}:{s.Line}: sprite '{s.Name}' uses animation '{s.Anim}', which no table read defines"
                );
            }
            else if (a.Auto != null && a.Count > 0)
            {
                CheckFlipbook(s, a);
            }
        }

        return table;
    }

    internal static (int Columns, int Rows) Grid(SpriteAnimation a)
    {
        var columns = a.Auto switch
        {
            "row" => a.Count,
            "column" => 1,
            _ => (int)Math.Ceiling(Math.Sqrt(a.Count)),
        };

        return (columns, (a.Count + columns - 1) / columns);
    }

    // A flipbook's cells must lie inside the @reference area (any int when there is none), so no cell overflows.
    static void CheckFlipbook(Sprite s, SpriteAnimation a)
    {
        var (columns, rows) = Grid(a);
        var width = s.ReferenceWidth > 0 ? s.ReferenceWidth : int.MaxValue;
        var height = s.ReferenceHeight > 0 ? s.ReferenceHeight : int.MaxValue;
        if (s.U + (long)columns * s.W > width || s.V + (long)rows * s.H > height)
            throw Bad(
                s.Table,
                s.Line,
                $"sprite '{s.Name}': the {columns}x{rows} cells of animation '{a.Name}' leave the reference area {width}x{height}"
            );
    }

    /// <summary>The frames of a sprite in play order: the cells of its flipbook animation, else its own region.</summary>
    public IReadOnlyList<SpriteFrame> Frames(Sprite s)
    {
        var own = new SpriteFrame
        {
            U = s.U,
            V = s.V,
            W = s.W,
            H = s.H,
        };
        if (s.Anim == null || !_animations.TryGetValue(s.Anim, out var a) || a.Auto == null || a.Count < 1)
            return new[] { own };
        var columns = Grid(a).Columns; // the cells lie inside the reference area: Read checked

        return Enumerable
            .Range(0, a.Count)
            .Select(i => own with { U = s.U + i % columns * s.W, V = s.V + i / columns * s.H })
            .ToList();
    }

    // "@sprite_node node sprite animation (w,h) (r,g,b) [billboard]"; null when the line does not say that.
    // stand-in: the install's spellings are read as the game evidently accepts them: an unclosed or doubled
    // parenthesis, leading-dot numbers, tabs; a "//billboard" is a comment, so that sprite does not face the camera.
    static SpriteNodeLine? SpriteNodeOf(string table, int line, string[] t)
    {
        if (t.Length < 6)
            return null;
        var billboard = t[^1] == "billboard";
        var groups = string.Concat(t[4..(billboard ? ^1 : ^0)])
            .Split('(', StringSplitOptions.RemoveEmptyEntries)
            .Select(g => g.TrimEnd(')').Split(','))
            .ToArray();
        if (groups.Length != 2 || groups[0].Length != 2 || groups[1].Length != 3)
            return null;
        var v = groups.SelectMany(g => g).Select(Float).ToArray();
        if (v.Any(x => x == null))
            return null;

        return new SpriteNodeLine
        {
            Node = t[1],
            Sprite = t[2],
            Animation = t[3],
            Width = v[0]!.Value,
            Height = v[1]!.Value,
            Colour = new Vector3(v[2]!.Value, v[3]!.Value, v[4]!.Value),
            Billboard = billboard,
            Table = table,
            Line = line,
        };
    }

    // The emitter of a block's "key=value" lines. The keys of a cloud (particle_count, cloud_radius) and pixel are read
    // and unused: this reader does not play clouds and the use of pixel is not recovered. A bad key or value is a note.
    SpriteEmitter EmitterOf(string table, string name, List<(int Line, string Key, string Value)> body)
    {
        var emitter = new SpriteEmitter { Name = name };
        foreach (var (line, key, value) in body)
        {
            void Number(Func<double, SpriteEmitter> set)
            {
                if (
                    double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
                    && double.IsFinite(v)
                )
                {
                    emitter = set(v);
                }
                else
                {
                    _notes.Add($"{table}:{line}: emitter '{name}' key '{key}' value '{value}' is not a number");
                }
            }

            switch (key)
            {
                case "type":
                    var kind = value switch
                    {
                        "spray" => EmitterKind.Spray,
                        "cloud" => EmitterKind.Cloud,
                        "beam" => EmitterKind.Beam,
                        _ => EmitterKind.Unknown,
                    };
                    if (kind == EmitterKind.Unknown)
                        _notes.Add($"{table}:{line}: emitter '{name}' has unknown type '{value}'");
                    emitter = emitter with { Kind = kind };

                    break;
                case "sprite":
                    emitter = emitter with { Sprite = value };

                    break;
                case "spread_angle":
                    Number(v => emitter with { SpreadAngle = v });

                    break;
                case "expansion_rate":
                    Number(v => emitter with { ExpansionRate = v });

                    break;
                case "velocity":
                    Number(v => emitter with { Velocity = v });

                    break;
                case "lifespan":
                    Number(v => emitter with { Lifespan = v });

                    break;
                case "firing_period":
                    Number(v => emitter with { FiringPeriod = v });

                    break;
                case "pixel":
                case "particle_count":
                case "cloud_radius":
                    break;
                default:
                    _notes.Add($"{table}:{line}: emitter '{name}' has an unknown key '{key}'");

                    break;
            }
        }

        // Only a spray needs its sprite: a cloud or a beam is not played.
        if (emitter.Kind == EmitterKind.Spray && emitter.Sprite.Length == 0)
            _notes.Add($"{table}: emitter '{name}' has no sprite key");

        return emitter;
    }

    static float? Float(string s) =>
        float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && float.IsFinite(v) ? v : null;

    static InvalidDataException Bad(string table, int line, string what) => new($"{table}:{line}: {what}");

    static string[] Tokens(string line)
    {
        var cut = line.IndexOf('#');
        if (cut >= 0)
        {
            line = line[..cut];
        }

        cut = line.IndexOf("//", StringComparison.Ordinal);
        if (cut >= 0)
        {
            line = line[..cut];
        }

        return line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
    }

    // "@name=value" or "@name value" -> (name, value); a row's "@anim=x" has no space variant.
    static (string Name, string Value) Split(string[] t)
    {
        var eq = t[0].IndexOf('=');

        return eq >= 0
            ? (t[0][..eq], t[0][(eq + 1)..] + string.Concat(t.Skip(1)))
            : (t[0], string.Join(" ", t.Skip(1)));
    }

    static int Int(string table, int line, string s, string what) =>
        int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) && v >= 0
            ? v
            : throw Bad(table, line, $"{what} '{s}' is not a whole number");

    // A keyframe line starts with a number ("time value", "u v", or a lone width) and is never as long as
    // a sprite row; the field's line gives the count the lines are checked against.
    static bool IsKeyframe(string[] t) =>
        t.Length < 6 && double.TryParse(t[0], NumberStyles.Float, CultureInfo.InvariantCulture, out _);

    // `done` holds the tables already read: a table included again adds nothing, so a chain of includes
    // of the same table stays linear.
    void Parse(string table, string text, Func<string, string?> open, List<string> stack, HashSet<string> done)
    {
        stack.Add(table);
        done.Add(table);
        var lines = text.Replace("\r", "").Split('\n');
        var header = false;
        int refW = 0,
            refH = 0;
        string? material = null,
            skip = null;
        SpriteAnimation? anim = null;
        var animLine = 0;
        var inKeyframes = false;
        string? emitter = null;
        var emitterLine = 0;
        var emitterBody = new List<(int Line, string Key, string Value)>();

        // An animation is complete when the next one starts or the table ends.
        void CloseAnimation()
        {
            if (anim == null)
                return;
            if (anim.Field.Length == 0)
                throw Bad(table, animLine, $"animation '{anim.Name}' has no '<field> <count> <seconds> <method>' line");
            if (anim.Auto != null || anim.Keyframes.Count == anim.Count)
                return;
            var what =
                $"{table}:{animLine}: animation '{anim.Name}' says {anim.Count} keyframes and has {anim.Keyframes.Count}";
            // The shipped font tables hold one keyframe fewer than they declare, so a shortfall is a note.
            if (anim.Keyframes.Count > anim.Count)
                throw new InvalidDataException(what);
            _notes.Add(what);
        }

        for (var n = 1; n <= lines.Length; n++)
        {
            var t = Tokens(lines[n - 1]);
            if (t.Length == 0)
                continue;
            if (!header)
            {
                if (t.Length != 1 || t[0] != "sprite_table")
                    throw Bad(table, n, "the first line must be 'sprite_table'");
                header = true;

                continue;
            }

            if (emitter != null)
            {
                if (t[0] == "@end_emitter")
                {
                    _emitters.Add(EmitterOf(table, emitter, emitterBody));
                    emitter = null;
                    emitterBody.Clear();
                }
                else
                {
                    // "key=value", also with blanks around the '='; a line with no '=' is a key with no value.
                    var joined = string.Concat(t);
                    var eq = joined.IndexOf('=');
                    emitterBody.Add(eq < 0 ? (n, joined, "") : (n, joined[..eq], joined[(eq + 1)..]));
                }

                continue;
            }

            if (t[0][0] == '@')
            {
                var (name, value) = Split(t);
                inKeyframes = false;
                switch (name)
                {
                    case "@include":
                        if (value.Length == 0)
                            throw Bad(table, n, "@include names no table");
                        if (stack.Contains(value, StringComparer.OrdinalIgnoreCase))
                            throw Bad(table, n, $"include cycle: {string.Join(" -> ", stack.Append(value))}");
                        if (done.Contains(value))
                            break;
                        Parse(
                            value,
                            open(value) ?? throw Bad(table, n, $"included table '{value}' not found"),
                            open,
                            stack,
                            done
                        );

                        break;
                    case "@reference":
                        refW = refH = Int(table, n, value, "@reference");

                        break;
                    case "@referenceWidth":
                        refW = Int(table, n, value, "@referenceWidth");

                        break;
                    case "@referenceHeight":
                        refH = Int(table, n, value, "@referenceHeight");

                        break;
                    case "@tmaterial":
                        material = value;

                        break;
                    case "@skip":
                        skip = value;

                        break;
                    case "@animation":
                        if (value.Length == 0)
                            throw Bad(table, n, "@animation names no animation");
                        CloseAnimation();
                        animLine = n;
                        anim = new SpriteAnimation
                        {
                            Name = value,
                            Field = "",
                            Count = 0,
                            Length = 0,
                            Method = "",
                            Auto = null,
                            Keyframes = new List<string[]>(),
                        };

                        break;
                    case "@auto":
                        if (anim == null || !AutoShapes.Contains(value))
                            throw Bad(table, n, $"@auto '{value}' is not square, row, or column of an @animation");
                        _animations[anim.Name] = anim = anim with { Auto = value };

                        break;
                    case "@keyframes":
                        if (anim == null)
                            throw Bad(table, n, "@keyframes outside an @animation");
                        inKeyframes = true;

                        break;
                    case "@emitter":
                        if (value.Length == 0)
                            throw Bad(table, n, "@emitter names no emitter");
                        emitter = value;
                        emitterLine = n;

                        break;
                    case "@sprite_node":
                        if (SpriteNodeOf(table, n, t) is { } node)
                        {
                            _nodes.Add(node);
                        }
                        else
                        {
                            var note = $"{table}:{n}: @sprite_node line '{string.Join(" ", t)}' cannot be read";
                            _unreadNodes.Add((t.Length > 1 ? t[1] : "", note));
                            _notes.Add(note);
                        }

                        break;
                    default:
                        throw Bad(table, n, $"unrecognized directive {name}");
                }

                continue;
            }

            if (inKeyframes && IsKeyframe(t))
            {
                ((List<string[]>)anim!.Keyframes).Add(t);

                continue;
            }

            inKeyframes = false;
            if (anim != null && anim.Field.Length == 0)
            {
                if (t.Length != 4 || !Fields.Contains(t[0]) || !Methods.Contains(t[3]))
                    throw Bad(
                        table,
                        n,
                        $"animation '{anim.Name}': expected '<offset|colour|draw|width> <count> <seconds> <step|linear>', got '{string.Join(" ", t)}'"
                    );
                var ok = double.TryParse(t[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var length);
                var count = Int(table, n, t[1], "keyframe count");
                // The table's character count bounds the count, so Frames allocates within a small constant factor of the table's size.
                if (count > text.Length)
                    throw Bad(
                        table,
                        n,
                        $"animation '{anim.Name}': keyframe count {count} is more than the table has text for"
                    );
                _animations[anim.Name] = anim = anim with
                {
                    Field = t[0],
                    Count = count,
                    Length = ok ? length : throw Bad(table, n, $"length '{t[2]}' is not a number"),
                    Method = t[3],
                };
                inKeyframes = true; // the game takes keyframe lines without the @keyframes directive too

                continue;
            }

            if (t.Length < 6)
                throw Bad(table, n, $"a sprite row needs Name Texture U V W H, got '{string.Join(" ", t)}'");
            string? spriteAnim = null;
            var cursor = false;
            (int, int)? origin = null;
            foreach (var extra in t.Skip(6))
            {
                var (xn, xv) = Split(new[] { extra });
                switch (xn)
                {
                    case "@anim":
                        spriteAnim = xv;

                        break;
                    case "@cursor":
                        cursor = true;

                        break;
                    case "@origin":
                        var xy = xv.Trim('(', ')').Split(',');
                        origin =
                            xy.Length == 2
                                ? (Int(table, n, xy[0], "@origin"), Int(table, n, xy[1], "@origin"))
                                : throw Bad(table, n, $"@origin '{xv}' is not (x,y)");

                        break;
                    default:
                        throw Bad(table, n, $"unrecognized row directive {extra}");
                }
            }

            _sprites.Add(
                new Sprite
                {
                    Name = t[0],
                    Texture = t[1],
                    U = Int(table, n, t[2], "U"),
                    V = Int(table, n, t[3], "V"),
                    W = Int(table, n, t[4], "W"),
                    H = Int(table, n, t[5], "H"),
                    ReferenceWidth = refW,
                    ReferenceHeight = refH,
                    Material = material,
                    Skip = skip,
                    Anim = spriteAnim,
                    Cursor = cursor,
                    Origin = origin,
                    Table = table,
                    Line = n,
                }
            );
        }

        if (!header)
            throw new InvalidDataException($"{table}: the first line must be 'sprite_table'");
        if (emitter != null)
            throw Bad(table, emitterLine, $"@emitter '{emitter}' has no @end_emitter");
        CloseAnimation();
        stack.RemoveAt(stack.Count - 1);
    }
}
