using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Assets;
using OpenArmada.Formats;

namespace OpenArmada.Tests;

// The rules the model preview draws by, tested without Godot on models built in code (INV-0001).
[TestClass]
public class ModelRulesTests
{
    static Sod.Mesh MeshOf(string textureMaterial, string texture = "tex") =>
        new() { TextureMaterial = textureMaterial, Texture = texture };

    static Sod V17() => new() { Version = 1.7f };

    [TestMethod]
    [DataRow("default", BlendMode.Opaque, 1f)]
    [DataRow("additive", BlendMode.Add, 1f)]
    [DataRow("translucent", BlendMode.Alpha, 0.5f)]
    [DataRow("alpha", BlendMode.Alpha, 1f)]
    [DataRow("alphathreshold", BlendMode.Cutout, 1f)]
    [DataRow("Additive", BlendMode.Add, 1f)]
    public void NamedTextureMaterialBlendsAsTheGameDefinesIt(string name, BlendMode mode, float opacity)
    {
        var look = ModelRules.Look(V17(), MeshOf(name));
        Assert.IsNull(look.Problem);
        Assert.AreEqual(new BlendState { Mode = mode, Opacity = opacity }, look.Blend);
    }

    [TestMethod]
    public void RenderTypeNamedAloneIsReadWithoutRegardToCase()
    {
        Assert.AreEqual(BlendMode.Add, ModelRules.Blend("Additive")!.Mode);
        Assert.AreEqual(BlendMode.Cutout, ModelRules.Blend("ALPHATHRESHOLD")!.Mode);
        Assert.IsNull(ModelRules.Blend("nosuch"));
    }

    [TestMethod]
    public void WireframeTextureMaterialDrawsEdges()
    {
        Assert.IsTrue(ModelRules.Look(V17(), MeshOf("wireframe")).Blend!.Wireframe);
    }

    [TestMethod]
    public void UnknownTextureMaterialIsReportedWithItsName()
    {
        var look = ModelRules.Look(V17(), MeshOf("sparkly"));
        Assert.IsNull(look.Blend);
        StringAssert.Contains(look.Problem, "'sparkly'");
    }

    static Sod Old(float transparency, byte src, byte dst, byte filter = 2) =>
        new()
        {
            Version = 1.2f,
            TextureMaterials =
            [
                new Sod.TextureMaterial
                {
                    Name = "fx",
                    Texture = "fxtex",
                    Transparency = transparency,
                    SrcBlend = src,
                    DstBlend = dst,
                    Filter = filter,
                },
            ],
        };

    [TestMethod]
    public void OldEntryNamesTheTextureAndBlendsAdditivelyWithOneOne()
    {
        var look = ModelRules.Look(Old(0.2f, 2, 2), MeshOf("", "fx"));
        Assert.AreEqual("fxtex", look.Texture);
        Assert.AreEqual(new BlendState { Mode = BlendMode.Add, Opacity = 0.8f }, look.Blend);
    }

    [TestMethod]
    public void OldEntryWithSourceAlphaBlendsByAlphaAtItsOpacityAndAlphaTests()
    {
        // The alpha test is on for every entry but one/one (FUN_0059b230): texels below 128/255 are dropped.
        var look = ModelRules.Look(Old(0.25f, 5, 6), MeshOf("", "fx"));
        Assert.AreEqual(
            new BlendState
            {
                Mode = BlendMode.Alpha,
                Opacity = 0.75f,
                AlphaTest = true,
            },
            look.Blend
        );
    }

    [TestMethod]
    public void OldEntryWithSourceAlphaAndHighTransparencyKeepsItsOpacityAndAlphaTests()
    {
        var look = ModelRules.Look(Old(0.75f, 5, 6), MeshOf("", "fx"));
        Assert.AreEqual(
            new BlendState
            {
                Mode = BlendMode.Alpha,
                Opacity = 0.25f,
                AlphaTest = true,
            },
            look.Blend
        );
    }

    [TestMethod]
    public void OldEntryWithOneZeroAlphaTests()
    {
        // The game turns alpha test on for every entry whose blend is not one/one (FUN_0059b230).
        Assert.AreEqual(BlendMode.Cutout, ModelRules.Look(Old(0f, 2, 1), MeshOf("", "fx")).Blend!.Mode);
    }

    [TestMethod]
    public void OldEntryFilterOneIsNearest()
    {
        Assert.IsTrue(ModelRules.Look(Old(0f, 2, 1, 1), MeshOf("", "fx")).Blend!.Nearest);
    }

    [TestMethod]
    public void OldEntryWithAnUnknownBlendValueIsReportedWithItsName()
    {
        var look = ModelRules.Look(Old(0f, 3, 9), MeshOf("", "fx"));
        Assert.IsNull(look.Blend);
        StringAssert.Contains(look.Problem, "'fx'");
        StringAssert.Contains(look.Problem, "3/9");
    }

    [TestMethod]
    public void OldMeshNamingNoEntryIsReported()
    {
        StringAssert.Contains(ModelRules.Look(Old(0f, 2, 1), MeshOf("", "gone")).Problem, "'gone'");
    }

    [TestMethod]
    public void MeshWithoutAnyTextureMaterialNameDrawsOpaque()
    {
        var look = ModelRules.Look(Old(0f, 2, 1), MeshOf("", ""));
        Assert.IsNull(look.Problem);
        Assert.AreEqual(BlendMode.Opaque, look.Blend!.Mode);
    }

    static Sod.LightingMaterial Material(string name, byte model, float power = 8f) =>
        new()
        {
            Name = name,
            Model = model,
            Ambient = new Vector3(0.1f, 0.1f, 0.1f),
            Diffuse = new Vector3(0.2f, 0.4f, 0.6f),
            Specular = new Vector3(1f, 0.5f, 0f),
            SpecularPower = power,
        };

    [TestMethod]
    public void LightingMaterialKeepsItsModelColorsAndPower()
    {
        var s = ModelRules.Lighting(Material("hull", 2))!;
        Assert.AreEqual(LightingModel.Phong, s.Model);
        Assert.AreEqual(new Vector3(0.2f, 0.4f, 0.6f), s.Diffuse);
        Assert.AreEqual(new Vector3(1f, 0.5f, 0f), s.Specular);
        Assert.AreEqual(8f, s.Power);
    }

    [TestMethod]
    [DataRow((byte)0, LightingModel.Constant)]
    [DataRow((byte)1, LightingModel.Lambert)]
    public void LightingModelsOfTheGame(byte model, LightingModel expected) =>
        Assert.AreEqual(expected, ModelRules.Lighting(Material("hull", model))!.Model);

    [TestMethod]
    public void UnknownLightingModelIsNotInvented()
    {
        Assert.IsNull(ModelRules.Lighting(Material("hull", 7)));
    }

    [TestMethod]
    public void TeamMaterialTakesTheTeamColorForDiffuseAndSpecularAndNoAmbient()
    {
        var s = ModelRules.Lighting(Material("Team_Fed", 2, 30f))!.WithTeam(new Vector3(0f, 0.4f, 1f));
        Assert.IsTrue(ModelRules.IsTeam("Team_Fed"));
        Assert.AreEqual(Vector3.Zero, s.Ambient);
        Assert.AreEqual(new Vector3(0f, 0.4f, 1f), s.Diffuse);
        Assert.AreEqual(new Vector3(0f, 0.4f, 1f), s.Specular);
        Assert.AreEqual(30f, s.Power); // power and model stay the file's
        Assert.AreEqual(LightingModel.Phong, s.Model);
    }

    [TestMethod]
    public void TeamNameNeedsNoUnderscore()
    {
        Assert.IsTrue(ModelRules.IsTeam("TEAMglow"));
        Assert.IsFalse(ModelRules.IsTeam("steam"));
    }

    static SpriteAnimation Flipbook(string auto, int count, double length, string field = "offset") =>
        new()
        {
            Name = "anim",
            Field = field,
            Count = count,
            Length = length,
            Method = "step",
            Auto = auto,
            Keyframes = System.Array.Empty<string[]>(),
        };

    [TestMethod]
    public void SquareFlipbookStepsThroughTheCellsAtItsFrameTime()
    {
        var a = ModelRules.Animation(Flipbook("square", 16, 4.0), 0, out var problem)!;
        Assert.IsNull(problem);
        Assert.AreEqual(Vector2.Zero, a.UvOffset(0.1));
        Assert.AreEqual(new Vector2(0.25f, 0f), a.UvOffset(0.3)); // 0.25 s per frame
        Assert.AreEqual(new Vector2(0f, 0.25f), a.UvOffset(1.1)); // frame 4
    }

    [TestMethod]
    public void ColumnFlipbookStepsDownTheTexture()
    {
        var a = ModelRules.Animation(Flipbook("column", 4, 2.0), 0, out _)!;
        Assert.AreEqual(new Vector2(0f, 0.25f), a.UvOffset(0.6));
    }

    [TestMethod]
    public void RowFlipbookStepsAcrossTheTexture()
    {
        var a = ModelRules.Animation(Flipbook("row", 4, 2.0), 0, out _)!;
        Assert.AreEqual(new Vector2(0.5f, 0f), a.UvOffset(1.1));
    }

    [TestMethod]
    public void FlipbookLoops()
    {
        var a = ModelRules.Animation(Flipbook("column", 4, 2.0), 0, out _)!;
        Assert.AreEqual(a.UvOffset(0.1), a.UvOffset(2.1));
    }

    [TestMethod]
    public void FrameIsPickedOverTheLengthPlusAPad()
    {
        // tex4x4, the shield hit effect's flipbook: 16 frames in 0.5 s, picked over 0.55 s (0x576220, 0x5ccf24)
        var a = ModelRules.Animation(Flipbook("square", 16, 0.5), 0, out _)!;
        Assert.AreEqual(new Vector2(0.5f, 0.75f), a.UvOffset(0.49)); // frame 14 of 0..15
        Assert.AreEqual(new Vector2(0.75f, 0.5f), a.UvOffset(0.38)); // frame 11; 0.38 / 0.5 · 16 would be 12
        Assert.AreEqual(Vector2.Zero, a.UvOffset(0.5)); // the time loops over the length
    }

    [TestMethod]
    public void TimeOffsetOfAReferenceShiftsThePhase()
    {
        var a = ModelRules.Animation(Flipbook("column", 4, 2.0), 0.5f, out _)!;
        Assert.AreEqual(new Vector2(0f, 0.25f), a.UvOffset(0.1));
    }

    [TestMethod]
    public void AnimationOfAnotherFieldIsReported()
    {
        Assert.IsNull(ModelRules.Animation(Flipbook("column", 4, 2.0, "colour"), 0, out var problem));
        StringAssert.Contains(problem, "colour");
    }

    [TestMethod]
    public void AnimationWithExplicitKeyframesIsReported()
    {
        Assert.IsNull(ModelRules.Animation(Flipbook(null!, 4, 2.0), 0, out var problem));
        StringAssert.Contains(problem, "keyframes");
    }
}
