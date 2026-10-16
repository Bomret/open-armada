using System.Text;

namespace OpenArmada.Formats;

// The text encoding of the game's files: definitions, tech trees, sprite tables, interface, key map and
// settings files, texts, the names inside models, and the names in archives and the archive index. Every
// decoder of game text or names uses this one, so a name decodes the same wherever it appears.
public static class Windows1252
{
    public static readonly Encoding Encoding = CodePagesEncodingProvider.Instance.GetEncoding(1252)!;
}
