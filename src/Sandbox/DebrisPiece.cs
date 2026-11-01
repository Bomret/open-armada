using System.Numerics;
using OpenArmada.Formats;

namespace OpenArmada.Sandbox;

/// <summary>
/// One triangle of an ended unit's model that flies off and tumbles until its life runs out (0x57d270, 0x57cd90). It
/// moves at its velocity and turns at its tumble rate, takes no share of the unit's velocity and no pull from a black
/// hole, and never fades. All vectors are in the world's frame (Godot's: Z mirrored from the model's), where a point
/// <c>p</c> of <see cref="Vertices"/> stands at <c>Position + AxisX * p.X + AxisY * p.Y + AxisZ * p.Z</c>.
/// </summary>
public sealed class DebrisPiece
{
    /// <summary>The map object of the unit the piece came from.</summary>
    public required PlacedObject Unit { get; init; }

    /// <summary>The mesh node of the unit's model the piece's face belongs to; the view draws the piece with its material.</summary>
    public required Sod.Node Node { get; init; }

    /// <summary>The index of the face's group in the mesh, which names the face's lighting material.</summary>
    public required int Group { get; init; }

    /// <summary>The index of the face among all faces of the mesh, in group order.</summary>
    public required int Face { get; init; }

    /// <summary>
    /// The face's three corners relative to its centroid, in the drawing order (the file's winding reversed, as
    /// SodBuilder draws a mesh).
    /// </summary>
    public required Vector3[] Vertices { get; init; }

    /// <summary>The texture coordinates of <see cref="Vertices"/>, in the same order.</summary>
    public required Vector2[] TexCoords { get; init; }

    /// <summary>Where the face's centroid is.</summary>
    public required Vector3 Position { get; set; }

    /// <summary>The piece's frame: the images of the local x axis, y axis, and z axis in the world.</summary>
    public required Vector3 AxisX { get; set; }

    /// <inheritdoc cref="AxisX"/>
    public required Vector3 AxisY { get; set; }

    /// <inheritdoc cref="AxisX"/>
    public required Vector3 AxisZ { get; set; }

    /// <summary>The piece's velocity in units per second.</summary>
    public required Vector3 Velocity { get; init; }

    /// <summary>The tumble rate about the piece's own axes in radians per second; each tick turns it by the negative of this.</summary>
    public required Vector3 AngularVelocity { get; init; }

    /// <summary>The seconds of life left; the piece is gone once they run out.</summary>
    public required double Life { get; set; }
}
