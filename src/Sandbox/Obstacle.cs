using System.Numerics;

namespace OpenArmada.Sandbox;

/// <summary>
/// A sphere a ship that is not derelict and not dying is pushed out of (0x4644b0): an asteroid field by its field radius, a planet by its
/// model radius. <c>Center</c> is in Godot's frame (Z mirrored), as the units' positions are. <c>Field</c> marks an
/// asteroid field, whose predicted hit stops a ship under obstacle avoidance; <c>CollisionRadius</c> is the field
/// definition's <c>collisionRadius</c> (0 where it sets none above 0), which that prediction reads in place of the field radius.
/// </summary>
public readonly record struct Obstacle(Vector3 Center, float Radius, bool Field = false, float CollisionRadius = 0);
