using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace OpenArmada.Formats;

// One cell of a sprite on its texture, in reference pixels.
public readonly record struct SpriteFrame(int U, int V, int W, int H);

// One sprite row. Reference sizes are 0 while no @reference is in force: the region is then in the
// texture's own pixels. Table and Line say where the row stands (the included table, not the root).
public sealed record Sprite(string Name, string Texture, int U, int V, int W, int H, int ReferenceWidth, int ReferenceHeight,
    string? Material, string? Skip, string? Anim, bool Cursor, (int X, int Y)? Origin, string Table, int Line);

// An @animation: Field is what it animates (offset, colour, draw, width), Count its keyframes and
// Length its seconds. Auto ("square", "row", "column") generates the keyframes of an offset animation
// as a flipbook over the texture grid; otherwise Keyframes holds the explicit lines, split on blanks.
public sealed record SpriteAnimation(string Name, string Field, int Count, double Length, string Method, string? Auto, IReadOnlyList<string[]> Keyframes)
{
    public double FrameSeconds => Count > 0 ? Length / Count : 0;
}

// A sprite table (.spr): "sprite_table" on the first line, '#' comments, @include of other tables,
// directives and rows "Name Texture U V W H" with trailing directives. An include's sprites and
// animations come where the @include stands. Anything not recognized, an include cycle, and a missing
// table throw InvalidDataException naming the table and line (INV-0002).
public sealed class SpriteTable
{
    static readonly string[] Fields = { "offset", "colour", "draw", "width" };
    static readonly string[] Methods = { "step", "linear" };
    static readonly string[] AutoShapes = { "square", "row", "column" };

    readonly List<Sprite> _sprites = new();
    readonly Dictionary<string, SpriteAnimation> _animations = new(StringComparer.OrdinalIgnoreCase);
    readonly List<string> _emitters = new(), _nodes = new(), _notes = new();

    public string Name { get; }
    public IReadOnlyList<Sprite> Sprites => _sprites;
    public IReadOnlyDictionary<string, SpriteAnimation> Animations => _animations;
    public IReadOnlyList<string> Emitters => _emitters;
    public IReadOnlyList<string> SpriteNodes => _nodes;
    // What does not fail the table: a sprite naming an animation no read table defines.
    public IReadOnlyList<string> Notes => _notes;

    SpriteTable(string name) => Name = name;

    // `open` returns the text of a table by name (an include's name as written), null when there is none.
    // `ambient` are other tables of the install: the game loads them all into one namespace, so a
    // sprite may use an animation one of them defines (the table's own definitions win).
    public static SpriteTable Read(string name, Func<string, string?> open, IEnumerable<SpriteTable>? ambient = null)
    {
        var table = new SpriteTable(name);
        table.Parse(name, open, new List<string>());
        foreach (var a in ambient ?? Array.Empty<SpriteTable>())
            foreach (var (key, anim) in a._animations) table._animations.TryAdd(key, anim);
        foreach (var s in table._sprites)
            if (s.Anim != null && !table._animations.ContainsKey(s.Anim))
                table._notes.Add($"{s.Table}:{s.Line}: sprite '{s.Name}' uses animation '{s.Anim}', which no table read defines");
        return table;
    }

    // The frames of a sprite in play order: the cells of its flipbook animation, else its own region.
    public IReadOnlyList<SpriteFrame> Frames(Sprite s)
    {
        var own = new SpriteFrame(s.U, s.V, s.W, s.H);
        if (s.Anim == null || !_animations.TryGetValue(s.Anim, out var a) || a.Auto == null || a.Count < 1) return new[] { own };
        var columns = a.Auto switch { "row" => a.Count, "column" => 1, _ => (int)Math.Ceiling(Math.Sqrt(a.Count)) };
        return Enumerable.Range(0, a.Count).Select(i => own with { U = s.U + i % columns * s.W, V = s.V + i / columns * s.H }).ToList();
    }

    static InvalidDataException Bad(string table, int line, string what) => new($"{table}:{line}: {what}");

    static string[] Tokens(string line)
    {
        var cut = line.IndexOf('#');
        if (cut >= 0) line = line[..cut];
        cut = line.IndexOf("//", StringComparison.Ordinal);
        if (cut >= 0) line = line[..cut];
        return line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
    }

    // "@name=value" or "@name value" -> (name, value); a row's "@anim=x" has no space variant.
    static (string Name, string Value) Split(string[] t)
    {
        var eq = t[0].IndexOf('=');
        return eq >= 0 ? (t[0][..eq], t[0][(eq + 1)..] + string.Concat(t.Skip(1))) : (t[0], string.Join(" ", t.Skip(1)));
    }

    static int Int(string table, int line, string s, string what) =>
        int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) && v >= 0 ? v : throw Bad(table, line, $"{what} '{s}' is not a whole number");

    void Parse(string table, Func<string, string?> open, List<string> stack)
    {
        var text = open(table) ?? throw new InvalidDataException($"{table}: sprite table not found");
        stack.Add(table);
        var lines = text.Replace("\r", "").Split('\n');
        var header = false;
        int refW = 0, refH = 0;
        string? material = null, skip = null;
        SpriteAnimation? anim = null;
        var inKeyframes = false;
        var inEmitter = false;

        for (var n = 1; n <= lines.Length; n++)
        {
            var t = Tokens(lines[n - 1]);
            if (t.Length == 0) continue;
            if (!header)
            {
                if (t.Length != 1 || t[0] != "sprite_table") throw Bad(table, n, "the first line must be 'sprite_table'");
                header = true;
                continue;
            }
            if (inEmitter)
            {
                inEmitter = t[0] != "@end_emitter";
                continue;
            }
            if (t[0][0] == '@')
            {
                var (name, value) = Split(t);
                inKeyframes = false;
                switch (name)
                {
                    case "@include":
                        if (value.Length == 0) throw Bad(table, n, "@include names no table");
                        if (stack.Contains(value, StringComparer.OrdinalIgnoreCase))
                            throw Bad(table, n, $"include cycle: {string.Join(" -> ", stack.Append(value))}");
                        if (open(value) == null) throw Bad(table, n, $"included table '{value}' not found");
                        Parse(value, open, stack);
                        break;
                    case "@reference": refW = refH = Int(table, n, value, "@reference"); break;
                    case "@referenceWidth": refW = Int(table, n, value, "@referenceWidth"); break;
                    case "@referenceHeight": refH = Int(table, n, value, "@referenceHeight"); break;
                    case "@tmaterial": material = value; break;
                    case "@skip": skip = value; break;
                    case "@animation":
                        if (value.Length == 0) throw Bad(table, n, "@animation names no animation");
                        anim = new SpriteAnimation(value, "", 0, 0, "", null, new List<string[]>());
                        break;
                    case "@auto":
                        if (anim == null || !AutoShapes.Contains(value)) throw Bad(table, n, $"@auto '{value}' is not square, row, or column of an @animation");
                        _animations[anim.Name] = anim = anim with { Auto = value };
                        break;
                    case "@keyframes":
                        if (anim == null) throw Bad(table, n, "@keyframes outside an @animation");
                        inKeyframes = true;
                        break;
                    case "@emitter": _emitters.Add(value); inEmitter = true; break;
                    case "@sprite_node": _nodes.Add(t.Length > 1 ? t[1] : throw Bad(table, n, "@sprite_node names no node")); break;
                    default: throw Bad(table, n, $"unrecognized directive {name}");
                }
                continue;
            }
            if (inKeyframes && t.Length < 6)
            {
                ((List<string[]>)anim!.Keyframes).Add(t);
                continue;
            }
            inKeyframes = false;
            if (anim != null && anim.Field.Length == 0)
            {
                if (t.Length != 4 || !Fields.Contains(t[0]) || !Methods.Contains(t[3]))
                    throw Bad(table, n, $"animation '{anim.Name}': expected '<offset|colour|draw|width> <count> <seconds> <step|linear>', got '{string.Join(" ", t)}'");
                var ok = double.TryParse(t[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var length);
                _animations[anim.Name] = anim = anim with { Field = t[0], Count = Int(table, n, t[1], "keyframe count"), Length = ok ? length : throw Bad(table, n, $"length '{t[2]}' is not a number"), Method = t[3] };
                inKeyframes = true; // the game takes keyframe lines without the @keyframes directive too
                continue;
            }
            if (t.Length < 6) throw Bad(table, n, $"a sprite row needs Name Texture U V W H, got '{string.Join(" ", t)}'");
            string? spriteAnim = null;
            var cursor = false;
            (int, int)? origin = null;
            foreach (var extra in t.Skip(6))
            {
                var (xn, xv) = Split(new[] { extra });
                switch (xn)
                {
                    case "@anim": spriteAnim = xv; break;
                    case "@cursor": cursor = true; break;
                    case "@origin":
                        var xy = xv.Trim('(', ')').Split(',');
                        origin = xy.Length == 2 ? (Int(table, n, xy[0], "@origin"), Int(table, n, xy[1], "@origin")) : throw Bad(table, n, $"@origin '{xv}' is not (x,y)");
                        break;
                    default: throw Bad(table, n, $"unrecognized row directive {extra}");
                }
            }
            _sprites.Add(new Sprite(t[0], t[1], Int(table, n, t[2], "U"), Int(table, n, t[3], "V"), Int(table, n, t[4], "W"), Int(table, n, t[5], "H"),
                refW, refH, material, skip, spriteAnim, cursor, origin, table, n));
        }
        if (!header) throw new InvalidDataException($"{table}: the first line must be 'sprite_table'");
        stack.RemoveAt(stack.Count - 1);
    }
}
