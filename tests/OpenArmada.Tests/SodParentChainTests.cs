using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Formats;
using OpenArmada.Sandbox;
using static OpenArmada.Tests.SodBytes;

namespace OpenArmada.Tests;

// A model with tens of thousands of nodes in one parent chain or loop is read and walked in linear time (INV-0002).
[TestClass]
public class SodParentChainTests
{
    const int Count = 65535;

    static Sod Chain(bool loop)
    {
        var nodes = Enumerable
            .Range(0, Count)
            .Select(i => new NodeSpec { Name = $"n{i}", Parent = i == 0 ? (loop ? $"n{Count - 1}" : "") : $"n{i - 1}" })
            .ToArray();

        return Sod.Read(new SodBytes(1.8f).Lighting().Nodes(nodes).End());
    }

    [TestMethod]
    [Timeout(30000)]
    public void ParentChainIsReadCheckedAndWalkedInLinearTime()
    {
        var sod = Chain(loop: false);

        Assert.AreEqual(0, sod.Problems.Count);
        Assert.IsFalse(sod.InParentCycle(sod.Nodes[Count - 1]));
        Assert.AreEqual("n1", sod.ChildrenOf(sod.Nodes[0]).Single().Name);
        Assert.AreEqual(Count, HardpointGeometry.Offsets(sod).Count);
    }

    [TestMethod]
    [Timeout(30000)]
    public void ParentLoopIsReadCheckedAndWalkedInLinearTime()
    {
        var sod = Chain(loop: true);

        Assert.AreEqual(Count, sod.Problems.Count);
        Assert.IsTrue(sod.Nodes.All(sod.InParentCycle));
        Assert.AreEqual(0, sod.ChildrenOf(sod.Nodes[0]).Count());
        Assert.AreEqual(Count, HardpointGeometry.Offsets(sod).Count);
    }
}
