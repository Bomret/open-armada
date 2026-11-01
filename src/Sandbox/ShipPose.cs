using System.Numerics;

namespace OpenArmada.Sandbox;

/// <summary>
/// A ship's pose: its position in the sandbox's frame and its orientation as the game's pitch, yaw, and roll
/// (0x55d080 composes them, 0x458ea0 decomposes a matrix into them; <see cref="MoveGeometry"/> states the mapping
/// to the sandbox's frame). The angles are in radians.
/// </summary>
public readonly record struct ShipPose
{
    /// <summary>The ship's position in the map's world units, in the sandbox's frame (Z mirrored).</summary>
    public required Vector3 Position { get; init; }

    /// <summary>The pitch about the ship's side axis: positive lowers the nose.</summary>
    public required float Pitch { get; init; }

    /// <summary>The yaw about the up axis: 0 faces the game's +Z, positive turns toward +X.</summary>
    public required float Yaw { get; init; }

    /// <summary>The roll about the ship's forward axis: positive raises the right side.</summary>
    public required float Roll { get; init; }

    /// <summary>The rotation that takes a model offset in Godot's frame to its world offset (<see cref="MoveGeometry.Orientation"/>).</summary>
    public Quaternion Orientation => MoveGeometry.Orientation(Pitch, Yaw, Roll);

    /// <summary>
    /// The bearing of the bow on the sandbox's ground plane (<see cref="MoveGeometry.HeadingOfYaw"/>): 0 faces +Z, a
    /// positive heading turns toward +X. The collision turn works in it.
    /// </summary>
    public float Heading => MoveGeometry.HeadingOfYaw(Yaw);
}
