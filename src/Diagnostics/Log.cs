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

    /// <summary>The model builder: models and their textures.</summary>
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

    /// <summary>Entries written at <see cref="Level.Warn"/> since the start or the last <see cref="Redirect"/> in force.</summary>
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

    /// <summary>Entries written at <see cref="Level.Error"/> since the start or the last <see cref="Redirect"/> in force.</summary>
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

    /// <summary>The lowercase word of an area; throws for a value outside <see cref="Area"/>.</summary>
    public static string AreaName(Area area) =>
        Enum.IsDefined(area)
            ? area.ToString().ToLowerInvariant()
            : throw new ArgumentOutOfRangeException(nameof(area), area, "not a defined area");

    /// <summary>
    /// Wraps a value in double quotes and escapes backslash, double quote, and line breaks, so the value
    /// stays on one log line.
    /// </summary>
    public static string Quote(string value) =>
        "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r") + "\"";

    /// <summary>
    /// Whether <c>e</c> says the install lacks the data, as opposed to data that failed to load: absent data
    /// is a WARN, a failed load an ERROR (INV-0005).
    /// </summary>
    public static bool IsAbsent(Exception e) => e is System.IO.FileNotFoundException;

    /// <summary>Writes an <see cref="Level.Error"/> entry: something broke.</summary>
    public static void Error(Area area, string text) => Write(Level.Error, area, text);

    /// <summary>Writes an <see cref="Level.Info"/> entry: context.</summary>
    public static void Info(Area area, string text) => Write(Level.Info, area, text);

    /// <summary>
    /// Replaces the sink and resets the counts; disposing restores the previous sink and counts. For tests and
    /// the run's own use.
    /// </summary>
    public static IDisposable Redirect(Action<string> sink)
    {
        Restore previous;
        lock (_lock)
        {
            previous = new Restore(_sink, _warns, _errors);
            _sink = sink;
            _warns = 0;
            _errors = 0;
        }

        return previous;
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
            var count = lines.Length;
            while (count > 1 && lines[count - 1] == "")
            {
                count--;
            }

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

    sealed class Restore(Action<string> sink, int warns, int errors) : IDisposable
    {
        public void Dispose()
        {
            lock (_lock)
            {
                _sink = sink;
                _warns = warns;
                _errors = errors;
            }
        }
    }
}
