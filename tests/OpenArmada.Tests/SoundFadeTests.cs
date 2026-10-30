using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Assets;
using OpenArmada.Sandbox;

namespace OpenArmada.Tests;

// The game's fade of fire and explosion sounds by their distance from the camera, and its reading from an audio.odf
// built in code (INV-0001).
[TestClass]
public class SoundFadeTests
{
    readonly TempInstall temp = new();

    [TestCleanup]
    public void CleanUp() => temp.CleanUp();

    static readonly SoundFade Stock = new()
    {
        Rolloff = 0.15,
        MinimumDistance = 50,
        MaximumDistance = 2000,
    };

    [TestMethod]
    public void AtTheCamerasOpeningDistanceAFireSoundPlaysAtAboutMinus7Point6DbAndAnExplosionAtAboutMinus4Point2Db()
    {
        Assert.AreEqual(-7.6, Stock.DbAt(509, SoundFade.FireMinimumScale)!.Value, 0.1);
        Assert.AreEqual(-4.2, Stock.DbAt(509, SoundFade.ExplosionMinimumScale)!.Value, 0.1);
    }

    [TestMethod]
    public void ASoundWithinItsMinimumDistancePlaysAtFullLevel()
    {
        Assert.AreEqual(0, Stock.DbAt(0, SoundFade.FireMinimumScale));
        Assert.AreEqual(0, Stock.DbAt(50, SoundFade.FireMinimumScale));
    }

    [TestMethod]
    public void ASoundBeyondItsMaximumDistanceIsSilent()
    {
        Assert.IsNotNull(Stock.DbAt(2000, SoundFade.FireMinimumScale));
        Assert.IsNull(Stock.DbAt(2001, SoundFade.FireMinimumScale));
    }

    [TestMethod]
    public void AnExplosionUsesTwiceTheMinimumDistanceAndTheSameMaximum()
    {
        Assert.AreEqual(0, Stock.DbAt(100, SoundFade.ExplosionMinimumScale));
        Assert.IsTrue(Stock.DbAt(100, SoundFade.FireMinimumScale) < 0);
        Assert.IsNotNull(Stock.DbAt(2000, SoundFade.ExplosionMinimumScale));
        Assert.IsNull(Stock.DbAt(2001, SoundFade.ExplosionMinimumScale));
    }

    [TestMethod]
    public void ARolloffOf0GivesNoFade()
    {
        Assert.AreEqual(0, (Stock with { Rolloff = 0 }).DbAt(1999, SoundFade.FireMinimumScale));
    }

    // The fade read from an install that holds `audio` as audio.odf (null: none).
    (SoundFade Fade, IReadOnlyList<ReportEntry> Reports) Read(string? audio)
    {
        var files = audio == null ? Array.Empty<(string, string)>() : [("addon/audio.odf", audio)];

        return SoundFade.Read(new GameFileSystem(temp.MakeInstall(files)));
    }

    const string StockOdf = "rolloffFactor = 0.15\r\nminimumDistance3D = 50\r\nmaximumDistance3D = 2000\r\n";

    [TestMethod]
    public void TheFadeComesFromTheInstallsAudioOdf()
    {
        var (fade, reports) = Read(StockOdf);
        Assert.AreEqual(Stock.Rolloff, fade.Rolloff, 1e-6); // the game reads the keys as floats
        Assert.AreEqual(Stock.MinimumDistance, fade.MinimumDistance);
        Assert.AreEqual(Stock.MaximumDistance, fade.MaximumDistance);
        Assert.AreEqual(0, reports.Count);
    }

    [TestMethod]
    public void AMissingAudioOdfTakesTheGamesDefaultsAndIsReportedOnce()
    {
        var (fade, reports) = Read(null);
        Assert.AreEqual(SoundFade.Game, fade);
        StringAssert.Contains(reports.Single().Text, "audio.odf missing");
    }

    [TestMethod]
    public void AnAudioOdfThatCannotBeReadTakesTheGamesDefaultsAndIsReportedOnce()
    {
        // a compressed archive entry: the install lists audio.odf, but reading it throws UnsupportedFormatException
        var archive = System.Text.Encoding.Latin1.GetString(
            ZfsBuilder.Build(0, ZfsBuilder.M("audio.odf", "rolloffFactor = 0.15", 2))
        );
        using var log = new CapturedLog();
        var (fade, reports) = SoundFade.Read(new GameFileSystem(temp.MakeInstall(("addon/x.zfs", archive))));
        Assert.AreEqual(SoundFade.Game, fade);
        StringAssert.Contains(reports.Single().Text, "audio.odf cannot be read");
    }

    [TestMethod]
    [DataRow("rolloffFactor", "1")]
    [DataRow("minimumDistance3D", "1")]
    [DataRow("maximumDistance3D", "1000000000")]
    public void AMissingKeyTakesTheGamesDefaultAndIsReportedOnce(string key, string fallback)
    {
        var odf = string.Join("\r\n", StockOdf.Split("\r\n").Where(l => !l.StartsWith(key)));
        var (fade, reports) = Read(odf);
        Assert.AreEqual(double.Parse(fallback), Value(fade, key));
        StringAssert.Contains(reports.Single().Text, $"audio.odf lacks {key}");
    }

    [TestMethod]
    [DataRow("rolloffFactor", "loud", "1")]
    [DataRow("rolloffFactor", "-0.5", "1")]
    [DataRow("minimumDistance3D", "near", "1")]
    [DataRow("minimumDistance3D", "0", "1")]
    [DataRow("maximumDistance3D", "far", "1000000000")]
    [DataRow("maximumDistance3D", "50", "1000000000")]
    public void AValueThatIsNoNumberOrUnusableTakesTheGamesDefaultAndIsReportedOnce(
        string key,
        string value,
        string fallback
    )
    {
        var odf = string.Join("\r\n", StockOdf.Split("\r\n").Select(l => l.StartsWith(key) ? $"{key} = {value}" : l));
        var (fade, reports) = Read(odf);
        Assert.AreEqual(double.Parse(fallback), Value(fade, key));
        StringAssert.Contains(reports.Single().Text, $"audio.odf's {key}");
    }

    [TestMethod]
    public void ARolloffOf0IsUsable()
    {
        var (fade, reports) = Read(StockOdf.Replace("0.15", "0"));
        Assert.AreEqual(0, fade.Rolloff);
        Assert.AreEqual(0, reports.Count);
    }

    static double Value(SoundFade fade, string key) =>
        key switch
        {
            "rolloffFactor" => fade.Rolloff,
            "minimumDistance3D" => fade.MinimumDistance,
            _ => fade.MaximumDistance,
        };
}
