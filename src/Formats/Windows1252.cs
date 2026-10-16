using System.Text;

namespace OpenArmada.Formats;

// The text encoding of the game's files: definitions, tech trees, texts, and the names inside models.
public static class Windows1252
{
    public static readonly Encoding Encoding = CodePagesEncodingProvider.Instance.GetEncoding(1252)!;
}
