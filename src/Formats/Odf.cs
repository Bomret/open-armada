using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace OpenArmada.Formats;

// One `key = value` pair of an object definition. Tokens are the value split on whitespace; a quoted
// string is one token with its quotes removed. Numbers stay text: the typed lookups of the resolved
// definition convert them.
public sealed record OdfEntry(string Key, IReadOnlyList<string> Tokens, string? Section, int Line);

// The text of one object definition (.odf), parsed without resolving includes. Lines are
// `key = value`, `[Section]`, `#include "parent.odf"`, `//` comments (full-line or trailing) and
// continuations of the previous value (indented lines, or lines that start with a quoted name). A line that is none of these, or that holds an
// unterminated quote, is listed in Problems as "line N: reason" and its entry is left out.
public sealed class Odf
{
    static readonly Regex KeyLine = new(@"^\s*([A-Za-z_][A-Za-z0-9_]*)\s*=(.*)$", RegexOptions.Compiled);

    readonly List<string> _includes = new();
    readonly List<OdfEntry> _entries = new();
    readonly List<string> _problems = new();

    public IReadOnlyList<string> Includes => _includes;
    public IReadOnlyList<OdfEntry> Entries => _entries;
    public IReadOnlyList<string> Problems => _problems;

    public static Odf Parse(string text)
    {
        var odf = new Odf();
        string? section = null;
        List<string>? current = null; // tokens of the last entry, grown by continuation lines
        var n = 0;
        foreach (var raw in text.Split('\n'))
        {
            n++;
            var line = StripComment(raw.TrimEnd('\r')).TrimEnd();
            if (line.Length == 0) continue;
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("#include", StringComparison.Ordinal))
            {
                if (Tokenize(trimmed[8..], out var inc, out var why) && inc.Count == 1) odf._includes.Add(inc[0]);
                else odf._problems.Add($"line {n}: bad include ({why ?? "expected one name"})");
                current = null;
            }
            else if (trimmed[0] == '[' && trimmed[^1] == ']')
            {
                section = trimmed[1..^1].Trim();
                current = null;
            }
            else if (KeyLine.Match(line) is { Success: true } m)
            {
                current = null;
                if (!Tokenize(m.Groups[2].Value, out var tokens, out var why)) { odf._problems.Add($"line {n}: {why}"); continue; }
                odf._entries.Add(new OdfEntry(m.Groups[1].Value, tokens, section, n));
                current = tokens;
            }
            else if (current != null && (char.IsWhiteSpace(line[0]) || trimmed[0] == '"')) // the game also writes quoted names flush left
            {
                if (Tokenize(trimmed, out var more, out var why)) current.AddRange(more);
                else odf._problems.Add($"line {n}: {why}");
            }
            else odf._problems.Add($"line {n}: not a key = value line: {Clip(trimmed)}");
        }
        return odf;
    }

    static string Clip(string s) => s.Length <= 40 ? s : s[..40] + "...";

    // Cuts the line at the first `//` outside quotes.
    static string StripComment(string line)
    {
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] == '"') quoted = !quoted;
            else if (!quoted && line[i] == '/' && i + 1 < line.Length && line[i + 1] == '/') return line[..i];
        }
        return line;
    }

    // Splits on whitespace; a quoted string is one token. False (and the reason) on an unterminated quote.
    static bool Tokenize(string s, out List<string> tokens, out string? problem)
    {
        tokens = new List<string>();
        problem = null;
        var i = 0;
        while (i < s.Length)
        {
            if (char.IsWhiteSpace(s[i])) { i++; continue; }
            var sb = new StringBuilder();
            if (s[i] == '"')
            {
                var end = s.IndexOf('"', i + 1);
                if (end < 0) { problem = "unterminated quote"; return false; }
                tokens.Add(s[(i + 1)..end]);
                i = end + 1;
                continue;
            }
            while (i < s.Length && !char.IsWhiteSpace(s[i])) sb.Append(s[i++]);
            tokens.Add(sb.ToString());
        }
        return true;
    }
}
