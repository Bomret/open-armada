using System.Globalization;
using WavLevel;

// Usage: WavLevel <file.wav> [--from <seconds>] [--to <seconds>]
// Prints "peak <x> dB, RMS <y> dB"; a problem prints "error: <message>" to standard error, exit 1.
string? path = null;
double? from = null,
    to = null;
for (var i = 0; i < args.Length; i++)
{
    if (args[i] is "--from" or "--to")
    {
        var flag = args[i];
        if (i + 1 >= args.Length || !double.TryParse(args[i + 1], CultureInfo.InvariantCulture, out var seconds))
            return Fail($"{flag} needs a number of seconds; usage: WavLevel <file.wav> [--from <s>] [--to <s>]");
        if (flag == "--from")
            from = seconds;
        else
            to = seconds;
        i++;
    }
    else if (path == null && !args[i].StartsWith("--"))
        path = args[i];
    else
        return Fail($"unexpected argument {args[i]}");
}
if (path == null)
    return Fail("no file given; usage: WavLevel <file.wav> [--from <s>] [--to <s>]");

byte[] bytes;
try
{
    bytes = File.ReadAllBytes(path);
}
catch (Exception e) when (e is IOException or UnauthorizedAccessException)
{
    return Fail($"cannot read {path}: {e.Message}");
}

var (level, problem) = WavLevelMeter.Measure(bytes, from, to);
if (level == null)
    return Fail($"{path}: {problem}");

static string Format(double db) =>
    double.IsNegativeInfinity(db) ? "-inf" : (Math.Round(db, 1) + 0.0).ToString("0.0", CultureInfo.InvariantCulture);
Console.WriteLine($"peak {Format(level.PeakDb)} dB, RMS {Format(level.RmsDb)} dB");
return 0;

static int Fail(string message)
{
    Console.Error.WriteLine($"error: {message}");
    return 1;
}
