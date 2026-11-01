using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Formats;

namespace OpenArmada.Tests;

// A model loaded at a class's scaleSOD, built in code (INV-0001).
[TestClass]
public class SodScaleTests
{
    static readonly Sod.Matrix34 Turned = new()
    {
        Right = new Vector3(0, 0, 1),
        Up = Vector3.UnitY,
        Front = new Vector3(-1, 0, 0),
        Position = new Vector3(2, 4, 6),
    };

    static Sod Model() =>
        new()
        {
            Version = 1.8f,
            Nodes =
            [
                new Sod.Node
                {
                    Type = Sod.NodeType.Mesh,
                    Name = "hull",
                    Transform = Turned,
                    Mesh = new Sod.Mesh { Vertices = [new Vector3(1, 2, 3)], TexCoords = [new Vector2(0.25f, 0.5f)] },
                },
                new Sod.Node
                {
                    Type = Sod.NodeType.Sprite,
                    Name = "light",
                    Parent = "hull",
                    Transform = Turned,
                },
            ],
        };

    [TestMethod]
    public void ScaleMultipliesNodePositionsAndMeshVerticesOnly()
    {
        var scaled = Model().Scaled(1.5f);

        foreach (var node in scaled.Nodes)
        {
            Assert.AreEqual(new Vector3(3, 6, 9), node.Transform.Position);
            Assert.AreEqual(Turned.Right, node.Transform.Right);
            Assert.AreEqual(Turned.Up, node.Transform.Up);
            Assert.AreEqual(Turned.Front, node.Transform.Front);
        }

        Assert.AreEqual(new Vector3(1.5f, 3, 4.5f), scaled.Nodes[0].Mesh!.Vertices[0]);
        Assert.AreEqual(new Vector2(0.25f, 0.5f), scaled.Nodes[0].Mesh!.TexCoords[0]);
        Assert.AreEqual("hull", scaled.Nodes[1].Parent);
    }

    [TestMethod]
    public void ScalingLeavesTheModelAsReadUnchanged()
    {
        var model = Model();
        model.Scaled(2f);

        Assert.AreEqual(new Vector3(2, 4, 6), model.Nodes[0].Transform.Position);
        Assert.AreEqual(new Vector3(1, 2, 3), model.Nodes[0].Mesh!.Vertices[0]);
    }

    [TestMethod]
    public void ScaleOneIsTheModelItself()
    {
        var model = Model();

        Assert.AreSame(model, model.Scaled(1f));
    }
}
