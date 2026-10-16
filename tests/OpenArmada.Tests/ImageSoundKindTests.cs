using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Assets;

namespace OpenArmada.Tests;

[TestClass]
public class ImageSoundKindTests
{
    readonly TempInstall temp = new();

    [TestCleanup]
    public void CleanUp() => temp.CleanUp();

    // Binary fixtures go through TempInstall's one-byte-per-char text.
    static string S(byte[] bytes) => Encoding.Latin1.GetString(bytes);

    string Dir(params (string Path, byte[] Bytes)[] files) =>
        temp.MakeInstall(files.Select(f => (f.Path, S(f.Bytes))).ToArray());

    GameFileSystem Install(params (string Path, byte[] Bytes)[] files) => new(Dir(files));

    static byte[] Tga(int size) => TgaTests.Make(size, size, 24);

    [TestMethod]
    public void TexturesListBothFoldersWithoutMipLevels()
    {
        var fs = Install(("Textures/RGB/a.tga", Tga(8)), ("Textures/RGB/a.1", Tga(4)), ("Textures/Index8/b.TGA", TgaTests.Make(4, 4, 8, 1)));
        CollectionAssert.AreEquivalent(new[] { "a.tga", "b.TGA" }, new TextureKind().Enumerate(fs).ToArray());
    }

    [TestMethod]
    public void EightBitTextureShadowedByFullColorOneIsListedWithItsFolder()
    {
        var fs = Install(("Textures/RGB/a.tga", Tga(8)), ("Textures/Index8/a.tga", TgaTests.Make(8, 8, 8, 1)),
            ("Textures/Index8/a.1", TgaTests.Make(4, 4, 8, 1)));
        var kind = new TextureKind();
        CollectionAssert.AreEquivalent(new[] { "a.tga", "Textures/Index8/a.tga" }, kind.Enumerate(fs).ToArray());
        var t = (TextureAsset)kind.Load(fs, "Textures/Index8/a.tga");
        Assert.AreEqual((true, 1), (t.Base.Info.Indexed, t.Mips.Count));
    }

    // An override texture takes its mip levels from the override layer, never the stock texture's levels.
    [TestMethod]
    public void OverrideTextureReadsNoMipLevelOfTheStockTexture()
    {
        var fs = Install(("addon/a.tga", Tga(8)), ("Textures/RGB/a.tga", Tga(16)), ("Textures/RGB/a.1", Tga(4)));
        Assert.AreEqual(0, ((TextureAsset)new TextureKind().Load(fs, "a.tga")).Mips.Count);
    }

    [TestMethod]
    public void TextureHoldsItsMipLevelsInOrder()
    {
        var fs = Install(("Textures/RGB/a.tga", Tga(8)), ("Textures/RGB/a.1", Tga(4)), ("Textures/RGB/a.2", Tga(2)));
        var t = (TextureAsset)new TextureKind().Load(fs, "a.tga");
        CollectionAssert.AreEqual(new[] { "a.1", "a.2" }, t.Mips.Select(m => m.Name).ToArray());
    }

    [TestMethod]
    public void MipLevelsStopAtTheFirstMissingSuffixUpToNine()
    {
        var files = new[] { ("Textures/RGB/a.tga", Tga(512)) }
            .Concat(new[] { 1, 2, 3, 4, 6 }.Select(i => ($"Textures/RGB/a.{i}", Tga(512 >> i))));
        var t = (TextureAsset)new TextureKind().Load(Install(files.ToArray()), "a.tga");
        Assert.AreEqual(4, t.Mips.Count);
    }

    [TestMethod]
    public void NineMipLevelsAreAllRead()
    {
        var files = new[] { ("Textures/RGB/a.tga", Tga(512)) }
            .Concat(Enumerable.Range(1, 9).Select(i => ($"Textures/RGB/a.{i}", Tga(512 >> i))));
        var t = (TextureAsset)new TextureKind().Load(Install(files.ToArray()), "a.tga");
        Assert.AreEqual(9, t.Mips.Count);
    }

    [TestMethod]
    public void MipChainEndsAtTheFirstLevelNotHalfTheSizeOfThePrevious()
    {
        // The install ships such files (a 32x32 base with a 128x128 level 1): level 1 and the ones after it are no part of the texture.
        var fs = Install(("Textures/RGB/a.tga", Tga(8)), ("Textures/RGB/a.1", Tga(4)), ("Textures/RGB/a.2", Tga(3)), ("Textures/RGB/a.3", Tga(1)));
        var t = (TextureAsset)new TextureKind().Load(fs, "a.tga");
        CollectionAssert.AreEqual(new[] { "a.1" }, t.Mips.Select(m => m.Name).ToArray());
    }

    [TestMethod]
    public void MipChainMismatchIsANoteNamingLevelSizeAndExpectedSize()
    {
        var fs = Install(("Textures/RGB/a.tga", Tga(8)), ("Textures/RGB/a.1", Tga(3)));
        var t = (TextureAsset)new TextureKind().Load(fs, "a.tga");
        Assert.AreEqual("level a.1 is 3x3, expected 4x4", t.Notes.Single());
    }

    [TestMethod]
    public void MipChainMismatchIsPrintedAsANoteAndDoesNotFailTheCheck()
    {
        var dir = Dir(("Textures/RGB/a.tga", Tga(8)), ("Textures/RGB/a.1", Tga(3)));
        var output = new StringWriter();
        var code = AssetCheck.Run(dir, new AssetKind[] { new TextureKind() }, output);
        StringAssert.Contains(output.ToString(), "NOTE textures/a.tga: level a.1 is 3x3, expected 4x4");
        StringAssert.Contains(output.ToString(), "textures: 1 loaded, 0 unsupported, 0 failed");
        Assert.AreEqual(0, code);
    }

    [TestMethod]
    public void NoteIsPrintedEvenWhenTheDecoderRejectsTheTexture()
    {
        var dir = Dir(("Textures/RGB/a.tga", Tga(8)), ("Textures/RGB/a.1", Tga(3)));
        var output = new StringWriter();
        var code = AssetCheck.Run(dir, new AssetKind[] { new TextureKind() }, output,
            (kind, files, name) => throw new InvalidDataException("Godot cannot decode it"));
        StringAssert.Contains(output.ToString(), "NOTE textures/a.tga: level a.1 is 3x3, expected 4x4");
        StringAssert.Contains(output.ToString(), "FAIL textures/a.tga: Godot cannot decode it");
        Assert.AreEqual(1, code);
    }

    [TestMethod]
    public void NonSquareMipChainKeepsASideAtOneAndKeepsHalving()
    {
        var fs = Install(("Textures/RGB/a.tga", TgaTests.Make(8, 2, 24)), ("Textures/RGB/a.1", TgaTests.Make(4, 1, 24)),
            ("Textures/RGB/a.2", TgaTests.Make(2, 1, 24)), ("Textures/RGB/a.3", TgaTests.Make(1, 1, 24)));
        var t = (TextureAsset)new TextureKind().Load(fs, "a.tga");
        Assert.AreEqual(3, t.Mips.Count);
    }

    [TestMethod]
    public void DecoderThatThrowsFailsOnlyItsAsset()
    {
        var dir = Dir(("sounds/ok.wav", WavTests.Make()), ("sounds/godot-rejects.wav", WavTests.Make()));
        var output = new StringWriter();
        var code = AssetCheck.Run(dir, new AssetKind[] { new SoundKind() }, output,
            (kind, files, name) => { if (name.Contains("godot-rejects")) throw new InvalidDataException("Godot cannot decode it (ErrFileCorrupt)"); });
        StringAssert.Contains(output.ToString(), "FAIL sounds/sounds/godot-rejects.wav: Godot cannot decode it");
        StringAssert.Contains(output.ToString(), "sounds: 1 loaded, 0 unsupported, 1 failed");
        Assert.AreEqual(1, code);
    }

    [TestMethod]
    public void DecoderDoesNotRunOnAnAssetTheReaderRejected()
    {
        var dir = Dir(("sounds/cut.wav", WavTests.Make(declared: 100)));
        var decoded = 0;
        AssetCheck.Run(dir, new AssetKind[] { new SoundKind() }, new StringWriter(), (kind, files, name) => decoded++);
        Assert.AreEqual(0, decoded);
    }

    [TestMethod]
    public void RootSplashBitmapBelongsToTheBitmapsKind()
    {
        var bmp = BmpPaletteTests.MakeBmp(2, 2, 24);
        var fs = Install(("splash.bmp", bmp), ("other.bmp", bmp));
        var kind = new BitmapKind();
        CollectionAssert.AreEqual(new[] { "splash.bmp" }, kind.Enumerate(fs).ToArray());
        Assert.AreEqual(2, ((OpenArmada.Formats.BmpInfo)kind.Load(fs, "splash.bmp")).Width);
    }

    [TestMethod]
    public void BaseOnlyLoadIgnoresMipLevels()
    {
        var fs = Install(("Textures/RGB/a.tga", Tga(8)), ("Textures/RGB/a.1", new byte[4]));
        Assert.AreEqual(8, new TextureKind().LoadBase(fs, "a.tga").Info.Width);
    }

    [TestMethod]
    public void CorruptMipLevelNamesTheLevel()
    {
        var fs = Install(("Textures/RGB/a.tga", Tga(8)), ("Textures/RGB/a.1", new byte[4]));
        var e = Assert.ThrowsExactly<InvalidDataException>(() => new TextureKind().Load(fs, "a.tga"));
        StringAssert.Contains(e.Message, "a.1");
    }

    [TestMethod]
    public void MipLevelsComeFromTheFolderOfTheTexture()
    {
        var fs = Install(("Textures/Index8/a.tga", TgaTests.Make(8, 8, 8, 1)), ("Textures/Index8/a.1", TgaTests.Make(4, 4, 8, 1)));
        Assert.AreEqual(1, ((TextureAsset)new TextureKind().Load(fs, "a.tga")).Mips.Count);
    }

    [TestMethod]
    public void BitmapsListEveryFolderBelowBitmapsAndBzn()
    {
        var bmp = BmpPaletteTests.MakeBmp(2, 2, 24);
        var fs = Install(("bitmaps/a/x.bmp", bmp), ("bitmaps/a/deep/y.BMP", bmp), ("bzn/m.bmp", bmp), ("bmp/launcher.bmp", bmp), ("Textures/RGB/z.bmp", bmp));
        CollectionAssert.AreEquivalent(new[] { "bitmaps/a/x.bmp", "bitmaps/a/deep/y.BMP", "bzn/m.bmp" }, new BitmapKind().Enumerate(fs).ToArray());
    }

    [TestMethod]
    public void BitmapLoadsByItsNameWithFolder()
    {
        var fs = Install(("bitmaps/a/x.bmp", BmpPaletteTests.MakeBmp(2, 2, 24)));
        Assert.AreEqual(2, ((OpenArmada.Formats.BmpInfo)new BitmapKind().Load(fs, "bitmaps/a/x.bmp")).Width);
    }

    [TestMethod]
    public void PalettesListRootAndTextureFolder()
    {
        var pal = new byte[768];
        var fs = Install(("Armada.pal", pal), ("Textures/RGB/StarTrek.pal", pal), ("Textures/Index8/other.pal", pal));
        CollectionAssert.AreEquivalent(new[] { "Armada.pal", "StarTrek.pal" }, new PaletteKind().Enumerate(fs).ToArray());
        Assert.AreEqual(256, ((System.Collections.Generic.IReadOnlyList<(byte, byte, byte)>)new PaletteKind().Load(fs, "StarTrek.pal")).Count);
    }

    [TestMethod]
    public void SoundsListEverySubfolderOfSounds()
    {
        var wav = WavTests.Make();
        var fs = Install(("sounds/effects/a.wav", wav), ("sounds/voiceover/ingame/b.WAV", wav), ("wav/launcher.wav", wav));
        CollectionAssert.AreEquivalent(new[] { "sounds/effects/a.wav", "sounds/voiceover/ingame/b.WAV" }, new SoundKind().Enumerate(fs).ToArray());
    }

    [TestMethod]
    public void BrokenSoundIsReportedAloneAndTheRestLoad()
    {
        var dir = Dir(("sounds/ok.wav", WavTests.Make()), ("sounds/cut.wav", WavTests.Make(declared: 100)),
            ("sounds/adpcm.wav", WavTests.Make(format: 2)));
        var output = new StringWriter();
        var code = AssetCheck.Run(dir,new AssetKind[] { new SoundKind() }, output);
        var text = output.ToString();
        StringAssert.Contains(text, "FAIL sounds/sounds/cut.wav");
        StringAssert.Contains(text, "UNSUPPORTED sounds/sounds/adpcm.wav");
        StringAssert.Contains(text, "sounds: 1 loaded, 1 unsupported, 1 failed");
        Assert.AreNotEqual(0, code);
    }
}
