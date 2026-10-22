using System;
using System.Collections.Generic;
using OpenArmada.Diagnostics;

namespace OpenArmada.Tests;

/// <summary>
/// Captures the lines <see cref="Log"/> writes while it lives, starting a fresh run of the textures and sprite tables
/// <see cref="ViewerLog.TextureFailed"/> and <see cref="ViewerLog.SpriteTableFailed"/> logged; disposing restores the previous sink and counts.
/// </summary>
sealed class CapturedLog : IDisposable
{
    readonly IDisposable _redirect;

    public CapturedLog()
    {
        ViewerLog.ResetRun();
        _redirect = Log.Redirect(Lines.Add);
    }

    /// <summary>Every formatted line written so far.</summary>
    public List<string> Lines { get; } = [];

    /// <summary>All lines joined by a line feed.</summary>
    public string Text => string.Join("\n", Lines);

    public override string ToString() => Text;

    public void Dispose() => _redirect.Dispose();
}
