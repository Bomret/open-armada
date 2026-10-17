using System;
using System.Collections.Generic;
using System.Linq;
using OpenArmada.Formats;

namespace OpenArmada.Assets;

/// <summary>
/// Bitmaps: the .bmp files below bitmaps/ (interface) and bzn/ (map previews), subfolders included,
/// and the root splash.bmp that Armada.exe names. Names carry their folder ("bitmaps/multiset/navmain1.bmp").
/// bmp/ holds launcher art and is not this kind's.
/// </summary>
public sealed class BitmapKind : AssetKind
{
    /// <summary>The kind's <see cref="Name"/>: <c>bitmaps</c>.</summary>
    public const string KindName = "bitmaps";
    static readonly string[] Folders = { "bitmaps/", "bzn/" };
    const string Splash = "splash.bmp"; // the one root bitmap Armada.exe references

    /// <inheritdoc/>
    public override string Name => KindName;

    /// <inheritdoc/>
    public override IEnumerable<string> Enumerate(GameFileSystem files) =>
        files
            .Inventory()
            .Where(f =>
                f.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase)
                && (
                    Folders.Any(p => f.StartsWith(p, StringComparison.OrdinalIgnoreCase))
                    || f.Equals(Splash, StringComparison.OrdinalIgnoreCase)
                )
            );

    /// <inheritdoc/>
    public override object Load(GameFileSystem files, string name) => Bmp.Read(files.ReadAllBytes(name));
}
