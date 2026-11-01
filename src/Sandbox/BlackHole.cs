using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using OpenArmada.Assets;

namespace OpenArmada.Sandbox;

/// <summary>
/// A black hole of the map (a placed object whose definition's classLabel is <c>blackhole_obj</c>, class keys read at
/// 0x44b7d0): it turns its model, pulls every ship without thrust within <see cref="GravityRadius"/> (0x450a80),
/// pushes ships with thrust out of the ring of 1.5 x <see cref="EventRadius"/> (0x4644b0), and swallows every living
/// unit whose center lies within <see cref="EventRadius"/> in the plane (0x44da40).
/// </summary>
public sealed record BlackHole
{
    /// <summary>The game's defaults for a key the definition lacks (0x44b7d0).</summary>
    public const float DefaultGravityRadius = 50;

    /// <inheritdoc cref="DefaultGravityRadius"/>
    public const float DefaultEventRadius = 10;

    /// <inheritdoc cref="DefaultGravityRadius"/>
    public const float DefaultGravityForce = 50;

    /// <inheritdoc cref="DefaultGravityRadius"/>
    public const float DefaultRotation = 0.02f;

    /// <summary>The ring of the push-out, in event radii (0x5fb898).</summary>
    public const float RingFactor = 1.5f;

    /// <summary>The placed object whose model the hole turns.</summary>
    public required PlacedObject Object { get; init; }

    /// <summary>
    /// The hole's center in Godot's frame (Z mirrored): the placed position lowered by <c>distanceBelowGrid</c>
    /// (inferred: 0x46d5c0 applies it when the definition sets <c>moveBelowCombatArea</c>, whose floor is not read here).
    /// </summary>
    public required Vector3 Center { get; init; }

    /// <summary>The definition's <c>GravityRadius</c>: how far the pull reaches (3D).</summary>
    public float GravityRadius { get; init; } = DefaultGravityRadius;

    /// <summary>The definition's <c>EventRadius</c>: the event horizon, in the plane.</summary>
    public float EventRadius { get; init; } = DefaultEventRadius;

    /// <summary>The definition's <c>GravityForce</c>: the pull at distance <see cref="GravityRadius"/>.</summary>
    public float GravityForce { get; init; } = DefaultGravityForce;

    /// <summary>The definition's <c>Rotation</c>: the model's turn about the vertical axis, radians per second.</summary>
    public float Rotation { get; init; } = DefaultRotation;

    /// <summary>The ring a ship with thrust is pushed out of, around the center: 1.5 x <see cref="EventRadius"/>.</summary>
    public float Ring => RingFactor * EventRadius;

    /// <summary>
    /// The turn of the model about the vertical axis at <c>seconds</c> of world time: a pure function of the time,
    /// so the model's pose never depends on the frame step. The model's placed orientation is turned by it.
    /// </summary>
    public Quaternion Turn(double seconds) =>
        Quaternion.CreateFromAxisAngle(Vector3.UnitY, (float)(Rotation * seconds));

    const string ClassLabel = "blackhole_obj"; // BlackHoleClass names itself so (0x44b7d0)
    const string MissingKey = "black hole key missing, the game's default used";
    const string BadKey = "black hole key is no number (a radius also not below 0), the game's default used";

    /// <summary>
    /// The black holes among the placed objects: each object whose definition's classLabel is <c>blackhole_obj</c>.
    /// A key the definition lacks takes the game's default (no lowering for <c>distanceBelowGrid</c>), and a key that is
    /// no number, or a radius below 0, is reported and takes the default too (INV-0002). A gap is one entry per cause,
    /// with the distinct <c>class: key</c> names.
    /// </summary>
    internal static IReadOnlyList<BlackHole> Collect(
        IEnumerable<PlacedObject> objects,
        IReadOnlyDictionary<string, ResolvedDefinition> definitions,
        Gaps gaps
    )
    {
        var holes = new List<BlackHole>();
        foreach (var o in objects)
        {
            if (
                !definitions.TryGetValue(o.Class, out var d)
                || d.Find("classLabel")?.Tokens.FirstOrDefault()?.Equals(ClassLabel, StringComparison.OrdinalIgnoreCase)
                    != true
            )
                continue;
            float Read(string key, float fallback, bool radius = false)
            {
                if (d.Find(key) is not { } value)
                {
                    gaps.Add(MissingKey, $"{o.Class}: {key}", o);

                    return fallback;
                }

                if (
                    value.Tokens.Count == 1
                    && ResolvedDefinition.TryNumber(value.Tokens[0], out var number)
                    && !(radius && number < 0)
                )
                    return number;
                gaps.Add(BadKey, $"{o.Class}: {key}", o);

                return fallback;
            }

            var below = Read("distanceBelowGrid", 0);
            holes.Add(
                new BlackHole
                {
                    Object = o,
                    // inferred: the center is the placed position lowered by distanceBelowGrid (0x46d5c0)
                    Center = o.Position - new Vector3(0, below, 0),
                    GravityRadius = Read("GravityRadius", DefaultGravityRadius, radius: true),
                    EventRadius = Read("EventRadius", DefaultEventRadius, radius: true),
                    GravityForce = Read("GravityForce", DefaultGravityForce),
                    Rotation = Read("Rotation", DefaultRotation),
                }
            );
        }

        return holes;
    }
}
