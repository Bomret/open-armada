using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace OpenArmada.Formats;

// One named value of a header: a number (a literal such as -20, +1.0, 4.0, 1e-3 or 0.5f, with a sign, an
// exponent and an f or F suffix allowed; an int takes only an integer literal, as in C) or Text. Text is the quoted string of a #define (IsString) or the unevaluated
// expression or constant name of a value that is not a literal. Type is "float", "int" or "define".
public sealed record GameConstant(string Name, string Type, double? Number, string? Text, bool IsString, int Line);

// The game constants: the named values of the install's RTS_CFG.h, in file order, with the headers it
// names by #include (named, not followed) and Notes. The grammar is the C-like subset of that file: "//"
// comments, #include "name", #define NAME "string" or number, and float/int NAME = value; declarations.
// A value that is an expression or names another constant is kept as text with a note; a line the reader
// does not recognize, a missing ';' and a duplicate name (the later value wins) are notes, each naming
// file:line (INV-0002). A file that is empty or binary throws InvalidDataException naming it.
public sealed class GameConstants
{
    static readonly Regex Include = new(@"^#include\s+""([^""]*)""$");
    static readonly Regex Define = new(@"^#define\s+(\w+)\s+(.+)$");
    static readonly Regex Declaration = new(@"^(float|int)\s+(\w+)\s*=\s*(.*?)\s*(;?)$");
    static readonly Regex Literal = new(@"^[-+]?(\d+\.?\d*|\.\d+)([eE][+-]?\d+)?[fF]?$");
    static readonly Regex IntLiteral = new(@"^[-+]?\d+$");
    static readonly Regex Quoted = new(@"^""([^""]*)""$");

    readonly List<GameConstant> _values = new();
    readonly List<string> _notes = new();
    readonly List<string> _includes = new();

    public string Name { get; }
    public IReadOnlyList<GameConstant> Values => _values;
    public IReadOnlyList<string> Notes => _notes;
    public IReadOnlyList<string> Includes => _includes;

    GameConstants(string name) => Name = name;

    public static GameConstants Read(string name, byte[] bytes)
    {
        if (bytes.Contains((byte)0))
            throw new InvalidDataException($"{name}: not a text header (it holds NUL bytes)");
        var text = Windows1252.Encoding.GetString(bytes);
        if (text.Trim().Length == 0)
            throw new InvalidDataException($"{name}: the file is empty");
        var c = new GameConstants(name);
        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            c.ReadLine(StripComment(lines[i]).Trim(), i + 1);
        }

        return c;
    }

    // The number of the constant, or false with the problem: missing, text (a #define string), or not a
    // literal (an expression or a reference to another constant, kept as text).
    public bool TryNumber(string name, out double value, out string problem)
    {
        value = 0;
        var c = _values.FirstOrDefault(v => v.Name == name);
        problem =
            c == null ? $"{Name}: constant {name} is missing"
            : c.IsString ? $"{Name}: constant {name} is text \"{c.Text}\", not a number"
            : c.Number == null ? $"{Name}: constant {name} is not a literal: {c.Text}"
            : "";
        if (c?.Number is { } n)
        {
            value = n;
        }

        return problem.Length == 0;
    }

    // The text before a "//" outside a quoted string.
    static string StripComment(string line)
    {
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] == '"')
            {
                quoted = !quoted;
            }
            else if (!quoted && line[i] == '/' && i + 1 < line.Length && line[i + 1] == '/')
                return line[..i];
        }

        return line;
    }

    void ReadLine(string line, int number)
    {
        var at = $"{Name}:{number}";
        if (line.Length == 0)
            return;
        if (Include.Match(line) is { Success: true } inc)
        {
            _includes.Add(inc.Groups[1].Value);

            return;
        }

        if (Define.Match(line) is { Success: true } def)
        {
            Add(def.Groups[1].Value, "define", def.Groups[2].Value.TrimEnd(), number);

            return;
        }

        if (Declaration.Match(line) is { Success: true } decl && decl.Groups[3].Value.Length > 0)
        {
            if (decl.Groups[4].Value.Length == 0)
            {
                _notes.Add($"{at}: {decl.Groups[2].Value} lacks the closing ';'");
            }

            Add(decl.Groups[2].Value, decl.Groups[1].Value, decl.Groups[3].Value, number);

            return;
        }

        _notes.Add($"{at}: unrecognized line '{line}'");
    }

    void Add(string name, string type, string value, int line)
    {
        var at = $"{Name}:{line}";
        GameConstant c;
        if (Quoted.Match(value) is { Success: true } q)
        {
            c = new(name, type, null, q.Groups[1].Value, true, line);
        }
        else if (Literal.IsMatch(value) && (type != "int" || IntLiteral.IsMatch(value)))
        {
            c = new(name, type, double.Parse(value.TrimEnd('f', 'F'), CultureInfo.InvariantCulture), null, false, line);
        }
        else
        {
            c = new(name, type, null, value, false, line);
            _notes.Add($"{at}: {name} is not a literal, kept as text: {value}");
        }

        var at0 = _values.FindIndex(v => v.Name == name);
        if (at0 < 0)
        {
            _values.Add(c);

            return;
        }

        _notes.Add($"{at}: {name} is declared again (line {_values[at0].Line}); the later value wins");
        _values[at0] = c;
    }
}
