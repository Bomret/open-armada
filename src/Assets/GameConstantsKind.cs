using System;
using System.Collections.Generic;
using System.Linq;
using OpenArmada.Formats;

namespace OpenArmada.Assets;

// Game constants: the install's RTS_CFG.h, a loose root file whose override copy wins like any asset's.
// The other root headers are configuration headers no kind reads. Load returns GameConstants.
public sealed class GameConstantsKind : AssetKind
{
    public const string KindName = "game constants";
    public const string FileName = "RTS_CFG.h";

    public override string Name => KindName;

    public override IEnumerable<string> Enumerate(GameFileSystem files) =>
        RootFiles(files, ".h").Where(f => f.Equals(FileName, StringComparison.OrdinalIgnoreCase));

    public override object Load(GameFileSystem files, string name) => GameConstants.Read(name, files.ReadAllBytes(name));

    // The install's game constants, for the camera and the movement to read by name.
    public static GameConstants Of(GameFileSystem files) => (GameConstants)new GameConstantsKind().Load(files, FileName);

    public override IReadOnlyList<string> Check(GameFileSystem files, string name) => ((GameConstants)Load(files, name)).Notes;
}
