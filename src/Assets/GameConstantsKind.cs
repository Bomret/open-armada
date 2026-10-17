using System;
using System.Collections.Generic;
using OpenArmada.Formats;

namespace OpenArmada.Assets;

/// <summary>
/// Game constants: the install's RTS_CFG.h, found as GameFileSystem finds it: the root file, or an override
/// copy in addon\ that wins like any asset's.
/// The other root headers are configuration headers no kind reads. Load returns GameConstants.
/// </summary>
public sealed class GameConstantsKind : AssetKind
{
    /// <summary>The kind's <see cref="Name"/>: <c>game constants</c>.</summary>
    public const string KindName = "game constants";

    /// <summary>The name of the game constants file, the one asset of this kind.</summary>
    public const string FileName = "RTS_CFG.h";

    /// <inheritdoc/>
    public override string Name => KindName;

    /// <inheritdoc/>
    public override IEnumerable<string> Enumerate(GameFileSystem files) =>
        files.Entry(FileName) is null ? Array.Empty<string>() : new[] { FileName };

    /// <inheritdoc/>
    public override object Load(GameFileSystem files, string name) =>
        GameConstants.Read(name, files.ReadAllBytes(name));

    /// <summary>The install's game constants, for the camera and the movement to read by name.</summary>
    public static GameConstants Of(GameFileSystem files) =>
        (GameConstants)new GameConstantsKind().Load(files, FileName);

    /// <inheritdoc/>
    public override IReadOnlyList<string> Check(GameFileSystem files, string name) =>
        ((GameConstants)Load(files, name)).Notes;
}
