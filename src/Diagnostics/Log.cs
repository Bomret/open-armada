using System;
using System.Globalization;

namespace OpenArmada.Diagnostics;

/// <summary>The fixed set of areas a log entry belongs to; the log writes each as its lowercase name.</summary>
public enum Area
{
    /// <summary>The run itself: start, end, launch arguments.</summary>
    Run,

    /// <summary>The asset check.</summary>
    Check,

    /// <summary>The asset browser.</summary>
    Browser,

    /// <summary>The map sandbox.</summary>
    Sandbox,

    /// <summary>The combat world.</summary>
    Combat,

    /// <summary>Asset loading and decoding.</summary>
    Assets,

    /// <summary>Rendering of models, textures, and the camera.</summary>
    Render,
}

/// <summary>The level of a log entry.</summary>
public enum Level
{
    /// <summary>Context.</summary>
    Info,

    /// <summary>The engine went on by design.</summary>
    Warn,

    /// <summary>Something broke.</summary>
    Error,
}

/// <summary>
/// The log: formats each entry as <c>&lt;local time&gt; &lt;LEVEL&gt; &lt;area&gt;: &lt;text&gt;</c> and hands each
/// line to the sink. Free of Godot (ADR 0005); the Godot side installs a sink that prints. Never throws
/// (INV-0002): a sink that throws loses that entry. State is process-wide and guarded by one lock.
/// </summary>
public static class Log
{
    static readonly object _lock = new();
    static readonly string[] _breaks = ["\r\n", "\n", "\r"];
    static Action<string> _sink = Console.Out.WriteLine;
    static int _warns,
        _errors;

    /// <summary>Entries written at <see cref="Level.Warn"/> since the start or the last <see cref="Redirect"/>.</summary>
    public static int WarnCount
    {
        get
        {
            lock (_lock)
            {
                return _warns;
            }
        }
    }

    /// <summary>Entries written at <see cref="Level.Error"/> since the start or the last <see cref="Redirect"/>.</summary>
    public static int ErrorCount
    {
        get
        {
            lock (_lock)
            {
                return _errors;
            }
        }
    }

    /// <summary>
    /// Receives each formatted line; the default writes it to standard output. Set to null to restore the default.
    /// </summary>
    public static Action<string>? Sink
    {
        set
        {
            lock (_lock)
            {
                _sink = value ?? Console.Out.WriteLine;
            }
        }
    }

    /// <summary>The lowercase word of an area.</summary>
    public static string AreaName(Area area) => area.ToString().ToLowerInvariant();

    /// <summary>Writes an <see cref="Level.Error"/> entry: something broke.</summary>
    public static void Error(Area area, string text) => Write(Level.Error, area, text);

    /// <summary>Writes an <see cref="Level.Info"/> entry: context.</summary>
    public static void Info(Area area, string text) => Write(Level.Info, area, text);

    /// <summary>
    /// Replaces the sink and resets the counts; disposing restores the previous sink. For tests and the
    /// run's own use.
    /// </summary>
    public static IDisposable Redirect(Action<string> sink)
    {
        Action<string> previous;
        lock (_lock)
        {
            previous = _sink;
            _sink = sink;
            _warns = 0;
            _errors = 0;
        }

        return new Restore(previous);
    }

    /// <summary>Writes a <see cref="Level.Warn"/> entry: the engine went on by design.</summary>
    public static void Warn(Area area, string text) => Write(Level.Warn, area, text);

    static void Write(Level level, Area area, string text)
    {
        try
        {
            var prefix =
                $"{DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz", CultureInfo.InvariantCulture)} "
                + $"{level.ToString().ToUpperInvariant()} {AreaName(area)}: ";
            var lines = text.Split(_breaks, StringSplitOptions.None);
            var count = lines.Length > 1 && lines[^1] == "" ? lines.Length - 1 : lines.Length;
            lock (_lock)
            {
                if (level == Level.Warn)
                {
                    _warns++;
                }
                else if (level == Level.Error)
                {
                    _errors++;
                }

                for (var i = 0; i < count; i++)
                {
                    _sink(prefix + lines[i]);
                }
            }
        }
        catch (Exception)
        {
            // A log entry that cannot be written is lost; the run goes on (INV-0002).
        }
    }

    sealed class Restore(Action<string> previous) : IDisposable
    {
        public void Dispose()
        {
            lock (_lock)
            {
                _sink = previous;
            }
        }
    }
}
