using System.IO;
using System.Linq;
using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Formats;

namespace OpenArmada.Tests;

[TestClass]
public class MapCompanionTests
{
    [TestMethod]
    public void MdfHoldsOneStartCellPerLocation()
    {
        var starts = Mdf.Read("StartLocations = 2\r\nStart1 = 53 61\r\nStart2 = 63 61\r\n");
        CollectionAssert.AreEqual(
            new[]
            {
                new StartCell { X = 53, Y = 61 },
                new StartCell { X = 63, Y = 61 },
            },
            starts.ToArray()
        );
    }

    [TestMethod]
    public void MdfWithFewerStartsThanItsCountFails()
    {
        var e = Assert.ThrowsExactly<InvalidDataException>(() =>
            Mdf.Read("StartLocations = 3\r\nStart1 = 1 1\r\nStart2 = 2 2\r\n")
        );
        StringAssert.Contains(e.Message, "Start3");
    }

    [TestMethod]
    public void MdfCountOutsideTwoToEightFails()
    {
        Assert.ThrowsExactly<InvalidDataException>(() => Mdf.Read("StartLocations = 9\r\n"));
    }

    [TestMethod]
    public void MdfCellOutsideTheGridFails()
    {
        Assert.ThrowsExactly<InvalidDataException>(() =>
            Mdf.Read("StartLocations = 2\r\nStart1 = 118 1\r\nStart2 = 1 1\r\n")
        );
    }

    [TestMethod]
    public void MdfLineItDoesNotKnowIsReported()
    {
        var e = Assert.ThrowsExactly<InvalidDataException>(() =>
            Mdf.Read("StartLocations = 2\r\nStart1 = 1 1\r\nStart2 = 1 1\r\nFoo = 1\r\n")
        );
        StringAssert.Contains(e.Message, "Foo");
    }

    [TestMethod]
    public void MdfStartGivenTwiceIsReported()
    {
        var e = Assert.ThrowsExactly<InvalidDataException>(() =>
            Mdf.Read("StartLocations = 2\r\nStart1 = 1 1\r\nStart1 = 2 2\r\nStart2 = 1 1\r\n")
        );
        StringAssert.Contains(e.Message, "Start1");
    }

    [TestMethod]
    public void PthHoldsItsPoints()
    {
        var points = Pth.Read("2\r\n0 0 -60.0\r\n-50 0 0\r\n");
        CollectionAssert.AreEqual(new[] { new Vector3(0, 0, -60), new Vector3(-50, 0, 0) }, points.ToArray());
    }

    [TestMethod]
    public void PthWithFewerPointsThanItsCountFails()
    {
        Assert.ThrowsExactly<InvalidDataException>(() => Pth.Read("3\r\n0 0 1\r\n"));
    }

    [TestMethod]
    public void PthWithTrailingValuesFails()
    {
        Assert.ThrowsExactly<InvalidDataException>(() => Pth.Read("1\r\n0 0 1\r\n5\r\n"));
    }

    [TestMethod]
    public void PthCountBeyondTheFileFailsWithoutAllocating()
    {
        Assert.ThrowsExactly<InvalidDataException>(() => Pth.Read("2000000000\r\n0 0 1\r\n"));
    }

    [TestMethod]
    public void StartCellsMapToTheGroundWithNorthUp()
    {
        var b = new MapBounds
        {
            MinX = 0,
            MaxX = 6400,
            MinY = -28.5f,
            MaxY = 105.5f,
            MinZ = 0,
            MaxZ = 3200,
        };
        Assert.AreEqual((0f, 3200f), b.FromStartCell(new StartCell { X = 0, Y = 0 }));
        Assert.AreEqual((6400f, 0f), b.FromStartCell(new StartCell { X = 117, Y = 117 }));
    }

    [TestMethod]
    public void ImageMappingPutsPlusXRightAndMaxZAtTheTopOfTheLetterbox()
    {
        var b = new MapBounds
        {
            MinX = 0,
            MaxX = 6400,
            MinY = 0,
            MaxY = 1,
            MinZ = 0,
            MaxZ = 3200,
        }; // twice as wide as deep: rows 32..96 of 128
        Assert.AreEqual((0f, 96f), b.ToImage(0, 0, 128));
        Assert.AreEqual((128f, 32f), b.ToImage(6400, 3200, 128));
    }
}
