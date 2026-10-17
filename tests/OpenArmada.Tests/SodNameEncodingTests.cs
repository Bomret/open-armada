using System;
using System.IO;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Formats;

namespace OpenArmada.Tests;

[TestClass]
public class SodNameEncodingTests
{
    [TestMethod]
    public void NamesDecodeAsWindows1252()
    {
        var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write(Encoding.ASCII.GetBytes("Storm3D_SW"));
        w.Write(1.7f);
        w.Write((ushort)1); // one lighting material
        var name = new byte[] { (byte)'K', 0x92, (byte)'s' };
        w.Write((ushort)name.Length);
        w.Write(name);
        for (var i = 0; i < 10; i++)
        {
            w.Write(0f); // ambient, diffuse, specular, power
        }

        w.Write((byte)0);
        w.Write(new byte[6]); // no nodes, no animations
        Assert.AreEqual("K’s", Sod.Read(ms.ToArray()).LightingMaterials[0].Name);
    }
}
