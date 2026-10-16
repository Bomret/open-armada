using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Formats;
using static OpenArmada.Tests.SodBytes;

namespace OpenArmada.Tests;

[TestClass]
public class SodModelDataTests
{
    [TestMethod]
    public void TextureMaterialBeforeV17KeepsItsBlendBytes()
    {
        var sod = Sod.Read(new SodBytes(1.6f).TextureMaterials(("fx", "fxtex", 0.25f, 5, 6, 2)).Lighting().Nodes().End());
        var t = sod.TextureMaterials.Single();
        Assert.AreEqual(0.25f, t.Transparency);
        Assert.AreEqual((5, 6, 2), (t.SrcBlend, t.DstBlend, t.Filter));
    }

    [TestMethod]
    public void LightingModelByteOfAnOldFileIsOneAboveTheModel()
    {
        // Versions up to 1.4 store model + 1 (Armada.exe 0x59bd80); the installed files hold 1 to 3 there.
        var old = Sod.Read(new SodBytes(1.4f).TextureMaterials().Lighting(("a", 1f, 3), ("b", 1f, 1)).Nodes().End());
        CollectionAssert.AreEqual(new byte[] { 2, 0 }, old.LightingMaterials.Select(m => m.Model).ToArray());
    }

    [TestMethod]
    public void LightingModelByteOfV15IsTheModel()
    {
        var sod = Sod.Read(new SodBytes(1.5f).TextureMaterials().Lighting(("a", 1f, 2)).Nodes().End());
        Assert.AreEqual(2, sod.LightingMaterials.Single().Model);
    }

    [TestMethod]
    public void ParentCycleIsReportedForEveryNodeInIt()
    {
        var sod = Sod.Read(new SodBytes(1.8f).Lighting().Nodes(new NodeSpec("a", "b"), new NodeSpec("b", "a"), new NodeSpec("c", "a")).End());
        Assert.AreEqual(2, sod.Problems.Count);
        Assert.IsTrue(sod.Problems.Any(p => p.Contains("'a'") && p.Contains("cycle")));
        Assert.IsTrue(sod.Problems.Any(p => p.Contains("'b'") && p.Contains("cycle")));
    }

    [TestMethod]
    public void NodeNamingItselfAsParentIsReported()
    {
        var sod = Sod.Read(new SodBytes(1.8f).Lighting().Nodes(new NodeSpec("a", "a")).End());
        StringAssert.Contains(sod.Problems.Single(), "'a'");
    }

    [TestMethod]
    public void FaceCornerPastTheVerticesIsReportedForTheNode()
    {
        var mesh = Triangle() with { Corners = new[] { (0, 0), (1, 1), (9, 2) } };
        var sod = Sod.Read(new SodBytes(1.8f).Lighting(("lit", 1f, 1)).Nodes(new NodeSpec("hull", "", mesh)).End());
        StringAssert.Contains(sod.Problems.Single(), "hull");
        StringAssert.Contains(sod.Problems.Single(), "vertex 9");
    }

    [TestMethod]
    public void FaceCornerPastTheTexCoordsIsReportedForTheNode()
    {
        var mesh = Triangle() with { Corners = new[] { (0, 0), (1, 7), (2, 2) } };
        var sod = Sod.Read(new SodBytes(1.8f).Lighting(("lit", 1f, 1)).Nodes(new NodeSpec("hull", "", mesh)).End());
        StringAssert.Contains(sod.Problems.Single(), "texture coordinate 7");
    }

    [TestMethod]
    public void NodeWithUnknownParentIsReported()
    {
        var sod = Sod.Read(new SodBytes(1.8f).Lighting().Nodes(new NodeSpec("root", ""), new NodeSpec("wing", "nowhere")).End());
        StringAssert.Contains(sod.Problems.Single(), "'wing'");
        StringAssert.Contains(sod.Problems.Single(), "'nowhere'");
    }

    [TestMethod]
    public void WellFormedModelHasNoProblems()
    {
        var sod = Sod.Read(new SodBytes(1.8f).Lighting(("lit", 1f, 1)).Nodes(new NodeSpec("root", ""), new NodeSpec("hull", "root", Triangle())).End());
        Assert.AreEqual(0, sod.Problems.Count);
    }
}
