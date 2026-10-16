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
        Assert.AreEqual((true, 1), (t.Info.Indexed, t.Mips.Count));
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
    public void MipLevelOfWrongSizeFailsTheTexture()
    {
        var fs = Install(("Textures/RGB/a.tga", Tga(8)), ("Textures/RGB/a.1", Tga(3)));
        var e = Assert.ThrowsExactly<InvalidDataException>(() => new TextureKind().Load(fs, "a.tga"));
        StringAssert.Contains(e.Message, "a.1");
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
