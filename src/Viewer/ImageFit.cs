using System;

namespace OpenArmada.Viewer;

/// <summary>The scale that fits an image into a box with its aspect ratio kept. Godot-free, so the tests compile it.</summary>
public static class ImageFit
{
    /// <summary>
    /// The largest scale at which an image of <c>width</c> by <c>height</c> fits a box of <c>boxWidth</c> by
    /// <c>boxHeight</c>; 0 when the image or the box has no area.
    /// </summary>
    public static float Scale(float width, float height, float boxWidth, float boxHeight)
    {
        if (width <= 0 || height <= 0 || boxWidth <= 0 || boxHeight <= 0)
            return 0;

        return Math.Min(boxWidth / width, boxHeight / height);
    }
}
