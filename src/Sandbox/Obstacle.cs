using System.Numerics;

namespace OpenArmada.Sandbox;

/// <summary>
/// A sphere a ship that is not derelict and not dying is pushed out of (0x4644b0): an asteroid field by its field radius, a planet by its
/// model radius. <c>Center</c> is in Godot's frame (Z mirrored), as the units' positions are.
/// </summary>
public readonly record struct Obstacle(Vector3 Center, float Radius);
