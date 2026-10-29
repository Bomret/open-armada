using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Viewer;

namespace OpenArmada.Tests;

[TestClass]
public class ImageFitTests
{
    [TestMethod]
    public void A_wide_image_fits_the_box_width() => Assert.AreEqual(2f, ImageFit.Scale(400, 100, 800, 800));

    [TestMethod]
    public void A_tall_image_fits_the_box_height() => Assert.AreEqual(0.5f, ImageFit.Scale(100, 1200, 800, 600));

    [TestMethod]
    public void A_small_image_is_scaled_up() => Assert.AreEqual(25f, ImageFit.Scale(16, 16, 400, 600));

    [TestMethod]
    [DataRow(0f, 16f, 400f, 600f)]
    [DataRow(16f, 0f, 400f, 600f)]
    [DataRow(16f, 16f, 0f, 600f)]
    [DataRow(16f, 16f, 400f, -20f)]
    public void An_image_or_box_without_area_scales_to_zero(float w, float h, float bw, float bh) =>
        Assert.AreEqual(0f, ImageFit.Scale(w, h, bw, bh));
}
