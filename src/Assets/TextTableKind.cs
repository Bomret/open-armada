using System;
using System.Collections.Generic;
using System.Linq;
using OpenArmada.Formats;

namespace OpenArmada.Assets;

// Text tables: the .ldl files of the install root (default, language, string, string_e). They are the
// launcher's (splash.exe) localization data, and every one is encrypted: all four open with the same 11 bytes,
// then look random (entropy 7.9 bits per byte, a third of the bytes printable), and no cipher is established.
// Load therefore always throws UnsupportedFormatException; a decoder replaces it once the cipher is known.
public sealed class TextTableKind : AssetKind
{
    public const string KindName = "texttables";

    public override string Name => KindName;

    public override IEnumerable<string> Enumerate(GameFileSystem files) =>
        files.Inventory().Where(f => f.EndsWith(".ldl", StringComparison.OrdinalIgnoreCase) && !f.Contains('/') && !f.Contains("::"));

    public override object Load(GameFileSystem files, string name) =>
        throw new UnsupportedFormatException($"{name}: encrypted launcher data read by splash.exe; its cipher is not established");
}
