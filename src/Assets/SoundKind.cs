using System;
using System.Collections.Generic;
using System.Linq;
using OpenArmada.Formats;

namespace OpenArmada.Assets;

/// <summary>
/// Sounds: the .wav files below sounds/, subfolders included. Names carry their folder
/// ("sounds/effects/x.wav"). wav/ holds launcher audio and is not this kind's.
/// </summary>
public sealed class SoundKind : AssetKind
{
    /// <summary>The kind's <see cref="Name"/>: <c>sounds</c>.</summary>
    public const string KindName = "sounds";

    /// <inheritdoc/>
    public override string Name => KindName;

    /// <inheritdoc/>
    public override IEnumerable<string> Enumerate(GameFileSystem files) =>
        files
            .Inventory()
            .Where(f =>
                f.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)
                && f.StartsWith("sounds/", StringComparison.OrdinalIgnoreCase)
            );

    /// <inheritdoc/>
    public override object Load(GameFileSystem files, string name) => Wav.Read(files.ReadAllBytes(name));
}
