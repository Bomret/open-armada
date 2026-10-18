using System;
using System.Collections.Generic;
using System.IO;
using OpenArmada.Diagnostics;
using OpenArmada.Formats;

namespace OpenArmada.Assets;

/// <summary>
/// The sprites an interface configuration can name: those of its race's tables and of the shared
/// <c>gui_global.spr</c>, by name, and the tables that failed to load.
/// </summary>
public sealed record InterfaceSprites
{
    /// <summary>The sprites by name, ignoring case; of a repeated name, the first in table listing order.</summary>
    public required IReadOnlyDictionary<string, Sprite> Sprites { get; init; }

    /// <summary>The tables the configuration needs that failed to load, in listing order; they add no sprite.</summary>
    public required IReadOnlyList<string> FailedTables { get; init; }

    /// <summary>
    /// The sprites of configuration <c>cfgName</c>: the tables whose file name starts with its stem
    /// (Sprites/gui_&lt;first letters of the cfg&gt;*.spr, so gui_fed.cfg reads gui_federation.spr) and
    /// gui_global.spr. A table that fails to load (an include not found, unreadable data, access denied) is
    /// left out and reported through <see cref="ViewerLog.SpriteTableFailed"/>.
    /// </summary>
    public static InterfaceSprites Of(GameFileSystem files, string cfgName)
    {
        var stem = Path.GetFileNameWithoutExtension(cfgName);
        var kind = new SpriteTableKind();
        var sprites = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);
        var failed = new List<string>();
        foreach (var table in kind.Enumerate(files))
        {
            var file = Path.GetFileName(table);
            if (
                !file.StartsWith(stem, StringComparison.OrdinalIgnoreCase)
                && !file.Equals("gui_global.spr", StringComparison.OrdinalIgnoreCase)
            )
                continue;
            try
            {
                foreach (var s in ((SpriteTable)kind.Load(files, table)).Sprites)
                {
                    sprites.TryAdd(s.Name, s);
                }
            }
            catch (Exception e)
                when (e
                        is IOException
                            or InvalidDataException
                            or UnauthorizedAccessException
                            or UnsupportedFormatException
                )
            {
                failed.Add(table);

                ViewerLog.SpriteTableFailed(table, e, cfgName);
            }
        }

        return new InterfaceSprites { Sprites = sprites, FailedTables = failed };
    }
}
