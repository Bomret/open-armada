using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace OpenArmada.Formats;

/// <summary>
/// A block of a text table: Path is the names of its enclosing blocks and its own, dot separated
/// ("switch.Default"). Parent is the name after ':' in its header, if any.
/// </summary>
public sealed record TextTableBlock
{
    /// <summary>
    /// The dot-separated names of the enclosing blocks and the block's own name, such as "switch.Default".
    /// </summary>
    public required string Path { get; init; }

    /// <summary>The name after ':' in the block's first header; null when it has none.</summary>
    public required string? Parent { get; init; }
}

/// <summary>One <c>string key "value"</c> entry of a block; File and Line say where it was written.</summary>
public sealed record TextTableEntry
{
    /// <summary>The <see cref="TextTableBlock.Path"/> of the block the entry is in.</summary>
    public required string Block { get; init; }

    /// <summary>The key token after <c>string</c>.</summary>
    public required string Key { get; init; }

    /// <summary>The quoted value without its quotes, with escapes applied.</summary>
    public required string Value { get; init; }

    /// <summary>
    /// The file the entry was written in: the name given to <see cref="TextTable.Read"/> or the path of an import.
    /// </summary>
    public required string File { get; init; }

    /// <summary>The 1-based line of the <c>string</c> token in <see cref="File"/>.</summary>
    public required int Line { get; init; }
}

/// <summary>
/// A text table (.ldl): the launcher's (splash.exe) configuration script, per the REA report of splash.exe.
/// The file is each byte XOR a fixed 400-byte key (plain[i] = file[i] ^ key[i % 400], restarting for every
/// file) and holds Windows-1252 text. The key lies in splash.exe at KeyOffset; the engine ships none, and the
/// caller passes the key it read from the user's own splash.exe (INV-0001).
///
/// Grammar: whitespace-separated tokens; "Name[:Parent] {" opens a block (blocks nest), "}" closes it,
/// <c>string key "value"</c> adds an entry, <c>import "file"</c> at top level reads another file (decoded on its own),
/// <c>template</c> is ignored, <c>$</c> ends the file, '#' outside quotes starts a comment, a quoted token may span
/// lines. In a value <c>\"</c> <c>\\</c> <c>\a</c> <c>\b</c> <c>\f</c> <c>\n</c> <c>\r</c> <c>\t</c> <c>\v</c> are the C escapes, a backslash followed by
/// whitespace drops everything through the end of the line, and a backslash before any other character drops
/// both. A statement that fits none of this, a value that is not quoted, a block left open, an import that
/// cannot be read and an import cycle are Notes naming file:line (INV-0002); the rest still loads.
///
/// A block reopened merges with the first (its entries add up); a different <c>:Parent</c> on the reopening is a
/// Note and the first parent stays. A header written <c>Name{</c> is one token that is no keyword, so it is a Note.
///
/// Not established by the REA report, so not interpreted here: what Name:Parent inherits, which of two
/// entries with one block path and key wins (both are kept, in reading order), and how splash.exe matches a
/// <c>switch</c> child to the system language. <c>open</c> receives an import's name joined to the folder of the
/// importing file (as the launcher resolves it), relative to the root the caller reads from.
/// </summary>
public sealed class TextTable
{
    /// <summary>
    /// The XOR key's place in splash.exe: <c>KeyOffset</c> is its file offset (0x2ee40), <c>KeyLength</c> its
    /// length in bytes (400).
    /// </summary>
    public const int KeyOffset = 0x2ee40,
        KeyLength = 400;

    readonly List<TextTableBlock> _blocks = new();
    readonly List<TextTableEntry> _entries = new();
    readonly List<string> _notes = new();

    /// <summary>The file name given to <see cref="Read"/>, the top file of the import tree.</summary>
    public string Name { get; }

    /// <summary>Each block path once, in the order it first opens; a reopened block keeps its first entry.</summary>
    public IReadOnlyList<TextTableBlock> Blocks => _blocks;

    /// <summary>
    /// The entries of all files in reading order, imports expanded where they stand; duplicates are kept.
    /// </summary>
    public IReadOnlyList<TextTableEntry> Entries => _entries;

    /// <summary>The problems that do not stop the read, each naming file:line.</summary>
    public IReadOnlyList<string> Notes => _notes;

    TextTable(string name) => Name = name;

    /// <summary>The key out of the bytes of splash.exe.</summary>
    public static byte[] KeyFrom(string name, byte[] exe) =>
        exe.Length >= KeyOffset + KeyLength
            ? exe[KeyOffset..(KeyOffset + KeyLength)]
            : throw new UnsupportedFormatException(
                $"{name} is too short ({exe.Length} bytes) to hold the text table key at offset 0x{KeyOffset:x}"
            );

    /// <summary>
    /// <c>open</c> returns the encoded bytes of an imported file by the name written after <c>import</c> joined to the importing file's folder, or null.
    /// Throws UnsupportedFormatException when <c>key</c> is not KeyLength bytes or <c>file</c> does not decode to text with it.
    /// </summary>
    public static TextTable Read(string name, byte[] file, byte[] key, Func<string, byte[]?> open)
    {
        if (key.Length != KeyLength)
            throw new UnsupportedFormatException($"{name}: the text table key is {key.Length} bytes, not {KeyLength}");
        var t = new TextTable(name);
        t.ReadFile(name, Decode(name, file, key), key, open, new List<string> { name });

        return t;
    }

    static string Decode(string file, byte[] raw, byte[] key)
    {
        var plain = new byte[raw.Length];
        for (var i = 0; i < raw.Length; i++)
        {
            plain[i] = (byte)(raw[i] ^ key[i % key.Length]);
            if (plain[i] < 32 && plain[i] is not (9 or 10 or 13))
                throw new UnsupportedFormatException(
                    $"{file}: does not decode to text with the key read from splash.exe (byte 0x{plain[i]:x2} at offset {i})"
                );
        }

        return Windows1252.Encoding.GetString(plain);
    }

    void ReadFile(string file, string text, byte[] key, Func<string, byte[]?> open, List<string> stack)
    {
        var tokens = Tokenize(file, text);
        var blocks = new List<(string Path, int Line)>();
        string At(int line) => $"{file}:{line}";
        for (var k = 0; k < tokens.Count; k++)
        {
            var (tok, line) = tokens[k];
            if (tok == "$")
                break;
            if (tok == "template")
                continue;
            if (tok == "}")
            {
                if (blocks.Count == 0)
                {
                    _notes.Add($"{At(line)}: '}}' without an open block");
                }
                else
                {
                    blocks.RemoveAt(blocks.Count - 1);
                }
            }
            else if (tok == "import")
            {
                var target = k + 1 < tokens.Count ? Unquote(tokens[++k].Text) : null;
                if (target == null)
                {
                    _notes.Add($"{At(line)}: import needs a quoted file name");
                }
                else if (blocks.Count > 0)
                {
                    _notes.Add($"{At(line)}: import '{target}' inside a block is ignored");
                }
                else
                {
                    Import(At(line), FolderOf(file) + target, key, open, stack);
                }
            }
            else if (tok == "string")
            {
                if (k + 2 >= tokens.Count)
                {
                    _notes.Add($"{At(line)}: 'string' entry is cut off");
                    k = tokens.Count;

                    continue;
                }

                var (name, value) = (tokens[++k].Text, Unquote(tokens[++k].Text));
                if (blocks.Count == 0)
                {
                    _notes.Add($"{At(line)}: string '{name}' outside a block");
                }
                else if (value == null)
                {
                    _notes.Add($"{At(line)}: value of '{name}' is not quoted; the entry is dropped");
                }
                else
                {
                    _entries.Add(
                        new TextTableEntry
                        {
                            Block = blocks[^1].Path,
                            Key = name,
                            Value = value,
                            File = file,
                            Line = line,
                        }
                    );
                }
            }
            else if (k + 1 < tokens.Count && tokens[k + 1].Text == "{")
            {
                var colon = tok.IndexOf(':');
                var (blockName, parent) = colon < 0 ? (tok, null) : (tok[..colon], tok[(colon + 1)..]);
                var path = blocks.Count == 0 ? blockName : blocks[^1].Path + "." + blockName;
                if (_blocks.FirstOrDefault(b => b.Path == path) is not { } first)
                {
                    _blocks.Add(new TextTableBlock { Path = path, Parent = parent });
                }
                else if (parent != null && parent != first.Parent)
                {
                    _notes.Add(
                        $"{At(line)}: block '{path}' is reopened with parent '{parent}'; it keeps its first parent '{first.Parent ?? "(none)"}'"
                    );
                }

                blocks.Add((path, line));
                k++;
            }
            else
            {
                _notes.Add($"{At(line)}: unrecognized statement '{tok}'");
            }
        }

        foreach (var (path, line) in blocks)
        {
            _notes.Add($"{At(line)}: block '{path}' is not closed");
        }
    }

    // The folder of a file name, with its trailing separator ("" for a file in the root).
    static string FolderOf(string file) => file[..(file.LastIndexOfAny(new[] { '/', '\\' }) + 1)];

    void Import(string at, string target, byte[] key, Func<string, byte[]?> open, List<string> stack)
    {
        if (stack.Contains(target, StringComparer.OrdinalIgnoreCase))
        {
            _notes.Add($"{at}: import cycle through '{target}'");

            return;
        }

        var raw = open(target);
        if (raw == null)
        {
            _notes.Add($"{at}: import '{target}' not found");

            return;
        }

        string decoded;
        try
        {
            decoded = Decode(target, raw, key);
        }
        catch (UnsupportedFormatException e)
        {
            _notes.Add($"{at}: import '{target}': {e.Message}");

            return;
        }

        stack.Add(target);
        ReadFile(target, decoded, key, open, stack);
        stack.RemoveAt(stack.Count - 1);
    }

    // Tokens with their line numbers. A quoted token keeps its quotes; any other token ends at whitespace or
    // at '#', which also skips the rest of the line.
    List<(string Text, int Line)> Tokenize(string file, string s)
    {
        var tokens = new List<(string, int)>();
        int i = 0,
            line = 1;
        while (i < s.Length)
        {
            if (char.IsWhiteSpace(s[i]))
            {
                if (s[i++] == '\n')
                {
                    line++;
                }

                continue;
            }

            var (start, startLine) = (i, line);
            if (s[i] == '"')
            {
                for (i++; i < s.Length && s[i] != '"'; i++)
                {
                    if (s[i] == '\\' && i + 1 < s.Length)
                    {
                        i++; // the next character cannot end the token
                    }

                    if (s[i] == '\n')
                    {
                        line++;
                    }
                }

                if (i >= s.Length)
                {
                    _notes.Add($"{file}:{startLine}: text is not closed");
                }
                else
                {
                    i++;
                }

                tokens.Add((s[start..i], startLine));

                continue;
            }

            while (i < s.Length && !char.IsWhiteSpace(s[i]) && s[i] != '#')
            {
                i++;
            }

            if (i > start)
            {
                tokens.Add((s[start..i], startLine));
            }

            if (i < s.Length && s[i] == '#')
            {
                while (i < s.Length && s[i] != '\n')
                {
                    i++;
                }
            }
        }

        return tokens;
    }

    // The text of a quoted token with its escapes decoded; null when the token is not quoted.
    static string? Unquote(string token)
    {
        if (token.Length == 0 || token[0] != '"')
            return null;
        var sb = new StringBuilder();
        for (var i = 1; i < token.Length && token[i] != '"'; i++)
        {
            if (token[i] != '\\')
            {
                sb.Append(token[i]);

                continue;
            }

            if (++i >= token.Length)
                break;
            switch (token[i])
            {
                case '"' or '\\':
                    sb.Append(token[i]);

                    break;
                case 'a':
                    sb.Append('\a');

                    break;
                case 'b':
                    sb.Append('\b');

                    break;
                case 'f':
                    sb.Append('\f');

                    break;
                case 'n':
                    sb.Append('\n');

                    break;
                case 'r':
                    sb.Append('\r');

                    break;
                case 't':
                    sb.Append('\t');

                    break;
                case 'v':
                    sb.Append('\v');

                    break;
                case '\t' or '\n' or '\r' or ' ':
                    while (i < token.Length && token[i] != '\n')
                    {
                        i++;
                    }

                    break; // line continuation
            }
        }

        return sb.ToString();
    }
}
