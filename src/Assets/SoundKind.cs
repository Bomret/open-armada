using System;
using System.Collections.Generic;
using System.Linq;
using OpenArmada.Formats;

namespace OpenArmada.Assets;

// Sounds: the .wav files below sounds/, subfolders included. Names carry their folder
// ("sounds/effects/x.wav"). wav/ holds launcher audio and is not this kind's.
public sealed class SoundKind : AssetKind
{
    public const string KindName = "sounds";

    public override string Name => KindName;

    public override IEnumerable<string> Enumerate(GameFileSystem files) =>
        files.Inventory().Where(f => f.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)
            && f.StartsWith("sounds/", StringComparison.OrdinalIgnoreCase));

    public override object Load(GameFileSystem files, string name) => Wav.Read(files.ReadAllBytes(name));
}
