using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using OpenArmada.Assets;
using OpenArmada.Diagnostics;

namespace OpenArmada.Sandbox;

/// <summary>
/// How a combat sound fades with its distance from the camera eye, by the 3D settings of the install's
/// <c>audio.odf</c> (0x5abbc0). A sound's minimum distance is <see cref="MinimumDistance"/> times its minimum scale,
/// its maximum distance <see cref="MaximumDistance"/> (0x5aff40, maximum scale 1 for every combat sound).
/// inferred: the game hands these to DirectSound, whose fade is not documented; the gain stands in for it as
/// min / (min + rolloff × (distance − min)) between the minimum and maximum distance.
/// </summary>
public sealed record SoundFade
{
    /// <summary>The minimum scale of a fire sound (0x50b7c0).</summary>
    public const double FireMinimumScale = 1;

    /// <summary>The minimum scale of an explosion sound (0x51a490).</summary>
    public const double ExplosionMinimumScale = 2;

    /// <summary>The level of a silent sound, in dB: Godot's floor, never a NaN or an infinite volume.</summary>
    public const double SilentDb = -80;

    const string File = "audio.odf";
    const string CannotBeRead = "audio.odf cannot be read, combat sounds take the game's fade";

    /// <summary>How fast a sound fades beyond its minimum distance; 0 is no fade (<c>rolloffFactor</c>).</summary>
    public required double Rolloff { get; init; }

    /// <summary>The distance within which a sound plays at full level, before its scale (<c>minimumDistance3D</c>).</summary>
    public required double MinimumDistance { get; init; }

    /// <summary>The distance beyond which a sound is silent (<c>maximumDistance3D</c>).</summary>
    public required double MaximumDistance { get; init; }

    /// <summary>The game's own values, which stand where <c>audio.odf</c> gives none (0x5abbc0's defaults).</summary>
    public static SoundFade Game =>
        new()
        {
            Rolloff = 1,
            MinimumDistance = 1,
            MaximumDistance = 1e9,
        };

    /// <summary>
    /// Reads the fade from the install's <c>audio.odf</c>, with the gaps found (INV-0002). A missing or unreadable file
    /// takes <see cref="Game"/>; a missing key, a value that is no number, and an unusable value (a rolloff below 0, a
    /// minimum distance of 0 or less, a maximum distance not above the minimum) each take the game's default for that
    /// key. Each cause is reported once; an unreadable file is also logged as one ERROR.
    /// </summary>
    public static (SoundFade Fade, IReadOnlyList<ReportEntry> Reports) Read(GameFileSystem files)
    {
        var gaps = new Gaps();
        var fade = Game;
        ResolvedDefinition definition;
        try
        {
            definition = ResolvedDefinition.Resolve(files, File);
        }
        catch (Exception e) // a file that cannot be resolved is a gap, whatever the reason (INV-0002)
        {
            if (Log.IsAbsent(e))
            {
                gaps.Add($"{File} missing, combat sounds take the game's fade", File);
            }
            else
            {
                Log.Error(Area.Sandbox, $"{File}: {e.Message}");
                gaps.Add(CannotBeRead, File);
            }

            return (fade, Gaps.Logged(gaps.Entries, CannotBeRead));
        }

        double Key(string key, double fallback, string rule, Func<double, bool> usable)
        {
            if (!definition.Has(key))
            {
                gaps.Add($"{File} lacks {key}, combat sounds take the game's default {Show(fallback)}", File);

                return fallback;
            }

            var tokens = definition.GetList(key);
            if (tokens.Count == 1 && ResolvedDefinition.TryNumber(tokens[0], out var value) && usable(value))
                return value;
            gaps.Add($"{File}'s {key} is {rule}, combat sounds take the game's default {Show(fallback)}", File);

            return fallback;
        }

        var rolloff = Key("rolloffFactor", fade.Rolloff, "no number of 0 or more", v => v >= 0);
        var minimum = Key("minimumDistance3D", fade.MinimumDistance, "no number above 0", v => v > 0);
        var maximum = Key(
            "maximumDistance3D",
            fade.MaximumDistance,
            "no number above the minimum distance",
            v => v > minimum
        );

        return (
            new SoundFade
            {
                Rolloff = rolloff,
                MinimumDistance = minimum,
                MaximumDistance = maximum,
            },
            Gaps.Logged(gaps.Entries, CannotBeRead)
        );
    }

    /// <summary>
    /// The level in dB of a sound <paramref name="distance"/> from the camera eye with the minimum scale
    /// <paramref name="minimumScale"/>: 0 within its minimum distance, the fade between, and null (silent) beyond
    /// its maximum distance, which wins over a minimum distance beyond it.
    /// </summary>
    public double? DbAt(double distance, double minimumScale)
    {
        if (distance > MaximumDistance)
            return null;
        var minimum = MinimumDistance * minimumScale;
        if (distance <= minimum)
            return 0;

        return 20 * Math.Log10(minimum / (minimum + Rolloff * (distance - minimum)));
    }

    /// <summary>
    /// Whether a sound <paramref name="distance"/> from the camera eye starts at all: one beyond its maximum distance
    /// is never played, even when the camera comes closer later (0x5b0b90).
    /// </summary>
    public bool StartsAt(double distance) => distance <= MaximumDistance;

    /// <summary>
    /// The level in dB of a playing sound at <paramref name="at"/> heard from the camera eye <paramref name="eye"/>:
    /// <see cref="DbAt"/>, or <see cref="SilentDb"/> beyond its maximum distance (the game's buffers mute there,
    /// 0x5aff40) and for a distance that is no finite number.
    /// </summary>
    public double VolumeDbAt(Vector3 at, Vector3 eye, double minimumScale)
    {
        var distance = Vector3.Distance(at, eye);

        return float.IsFinite(distance) && DbAt(distance, minimumScale) is { } db ? db : SilentDb;
    }

    static string Show(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
