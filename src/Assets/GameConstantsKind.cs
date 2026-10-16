using System;
using System.Collections.Generic;
using OpenArmada.Formats;

namespace OpenArmada.Assets;

// Game constants: the install's RTS_CFG.h, found as GameFileSystem finds it: the root file, or an override
// copy in addon\ that wins like any asset's.
// The other root headers are configuration headers no kind reads. Load returns GameConstants.
public sealed class GameConstantsKind : AssetKind
{
    public const string KindName = "game constants";
    public const string FileName = "RTS_CFG.h";

    public override string Name => KindName;

    public override IEnumerable<string> Enumerate(GameFileSystem files) =>
        files.Entry(FileName) is null ? Array.Empty<string>() : new[] { FileName };

    public override object Load(GameFileSystem files, string name) => GameConstants.Read(name, files.ReadAllBytes(name));

    // The install's game constants, for the camera and the movement to read by name.
    public static GameConstants Of(GameFileSystem files) => (GameConstants)new GameConstantsKind().Load(files, FileName);

    public override IReadOnlyList<string> Check(GameFileSystem files, string name) => ((GameConstants)Load(files, name)).Notes;
}
