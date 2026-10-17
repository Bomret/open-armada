using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using OpenArmada.Assets;
using OpenArmada.Formats;

namespace OpenArmada.Sandbox;

/// <summary>
/// One rock of an asteroid field: its model and its pose at any time t (seconds), a pure function of t so the
/// pose never depends on the frame step. The rock sits at Offset from the field's Center at t = 0.
///   stand-in: the motion. The game simulates the rocks' mutual gravity (Armada.exe, REA: FUN_0044e090); the
///   sandbox lets each rock circle the field's center about the vertical axis at the field's orbitingSpeed and
///   spin about its own vertical axis at the field's rotationSpeed. Both units are radians per second (the
///   game's unit of these two values is not known).
/// </summary>
public sealed record Rock
{
    /// <summary>The rock's model, one of <see cref="AsteroidField.ModelNames"/> (without ".sod").</summary>
    public required string Model { get; init; }

    /// <summary>The center of the rock's field, in Godot's frame (Z mirrored).</summary>
    public required Vector3 Center { get; init; }

    /// <summary>Where the rock sits relative to <see cref="Center"/> at t = 0.</summary>
    public required Vector3 Offset { get; init; }

    /// <summary>The rock's orientation at t = 0.</summary>
    public required Quaternion Initial { get; init; }

    /// <summary>The spin about the rock's own vertical axis in radians per second (the field's rotationSpeed).</summary>
    public required float Spin { get; init; }

    /// <summary>The circling about the vertical axis through <see cref="Center"/> in radians per second (the field's orbitingSpeed).</summary>
    public required float Orbit { get; init; }

    /// <summary>The rock's position and orientation at <c>t</c> seconds.</summary>
    public (Vector3 Position, Quaternion Orientation) Pose(double t) =>
        (
            Center + Vector3.Transform(Offset, Quaternion.CreateFromAxisAngle(Vector3.UnitY, (float)(Orbit * t))),
            Quaternion.Normalize(Initial * Quaternion.CreateFromAxisAngle(Vector3.UnitY, (float)(Spin * t)))
        );
}

/// <summary>
/// The values of one asteroid field, from its definition and the map: Center is in Godot's frame (Z mirrored),
/// Models are the asteroid models the install holds.
/// </summary>
public sealed record AsteroidField
{
    /// <summary>The field object's position, in Godot's frame (Z mirrored).</summary>
    public required Vector3 Center { get; init; }

    /// <summary>The definition's minAsteroids: the smallest rock count.</summary>
    public required int Min { get; init; }

    /// <summary>The definition's maxAsteroids: the largest rock count.</summary>
    public required int Max { get; init; }

    /// <summary>The definition's fieldRadius: the radius of the sphere that holds the rocks.</summary>
    public required float Radius { get; init; }

    /// <summary>The definition's rotationSpeed: each rock's spin, read as radians per second.</summary>
    public required float RotationSpeed { get; init; }

    /// <summary>The definition's orbitingSpeed: each rock's circling of <see cref="Center"/>, read as radians per second.</summary>
    public required float OrbitingSpeed { get; init; }

    /// <summary>
    /// The seed of the field's random rocks, from the map's own content and the field's handle and id, so the
    /// same map gives the same rocks on every start.
    /// </summary>
    public required uint Seed { get; init; }

    /// <summary>The asteroid models the install holds, out of <see cref="ModelNames"/>.</summary>
    public required IReadOnlyList<string> Models { get; init; }

    /// <summary>The game's eight hard-coded asteroid models (Armada.exe, REA: FUN_0044b200); master8 is not used.</summary>
    public static readonly IReadOnlyList<string> ModelNames = new[]
    {
        "master",
        "master1",
        "master2",
        "master3",
        "master4",
        "master5",
        "master6",
        "master7",
    };

    /// <summary>
    /// The rocks the field shows: a count in [Min, Max], each a random model of Models, placed inside the sphere
    /// of Radius less <c>largestExtent</c> (the largest rock's extent), flattened ×0.5 in height, with random orientation.
    /// </summary>
    public IReadOnlyList<Rock> Rocks(float largestExtent)
    {
        var random = new Random(unchecked((int)Seed));
        var reach = MathF.Max(0, Radius - largestExtent);
        var rocks = new List<Rock>();
        var count = random.Next(Min, Max + 1);
        for (var i = 0; i < count; i++)
        {
            var model = Models[random.Next(Models.Count)];
            var direction = Unit(random);
            // stand-in: the flatten factor 0.5 in height (the install defines none and the game's own spread is not known)
            var offset = direction * (reach * MathF.Cbrt((float)random.NextDouble())) * new Vector3(1, 0.5f, 1);
            rocks.Add(
                new Rock
                {
                    Model = model,
                    Center = Center,
                    Offset = offset,
                    Initial = Quaternion.Normalize(new Quaternion(Unit(random), (float)random.NextDouble() * 2 - 1)),
                    Spin = RotationSpeed,
                    Orbit = OrbitingSpeed,
                }
            );
        }

        return rocks;
    }

    /// <summary>
    /// The box that holds every rock of the field at any time: the field's sphere grown by <c>largestExtent</c>, so a rock at
    /// the center of a field smaller than the largest rock still fits (the height is included).
    /// </summary>
    public (Vector3 Min, Vector3 Max) DrawBox(float largestExtent)
    {
        var reach = Vector3.One * (Radius + largestExtent);

        return (Center - reach, Center + reach);
    }

    // A uniformly random unit-length direction (rejection sampling in the unit ball).
    static Vector3 Unit(Random random)
    {
        while (true)
        {
            var v = new Vector3(
                (float)random.NextDouble() * 2 - 1,
                (float)random.NextDouble() * 2 - 1,
                (float)random.NextDouble() * 2 - 1
            );
            var length = v.Length();
            if (length is > 0.01f and <= 1)
                return v / length;
        }
    }

    const string MissingKey = "asteroid field value missing or not a number, fields skipped";
    const string MinAboveMax = "asteroid field minimum above maximum, fields skipped";
    const string BadRadius = "asteroid field radius not above 0, fields skipped";
    const string BadCount = "asteroid field count negative or above the bound, fields skipped";

    // stand-in: the largest rock count of one field the sandbox plans (the game's limit is not known; 4mama's fields
    // hold up to 4,800 rocks together), which keeps a bad value from allocating without bound.
    const int MaxRocksPerField = 20000;
    const string MissingModel = "asteroid model missing, the others used";
    const string NoModel = "no asteroid model left, fields skipped";

    /// <summary>
    /// The fields of the map, each from its definition (a class whose classLabel is asteroidfield), and the gaps
    /// found: a field with a missing or illegal value is reported and left out, a missing model is reported and
    /// the rest used (INV-0002). Reports are grouped by cause; the count is the number of fields the cause touches.
    /// </summary>
    public static (IReadOnlyList<AsteroidField> Fields, IReadOnlyList<ReportEntry> Reports) Collect(
        BznMap map,
        IReadOnlyDictionary<string, ResolvedDefinition> definitions,
        Func<string, bool> hasModel
    )
    {
        var gaps = new Gaps();
        var present = ModelNames.Where(hasModel).ToList();
        var fields = new List<AsteroidField>();
        foreach (var o in map.Objects)
        {
            if (
                !definitions.TryGetValue(o.Class, out var d)
                || d.Find("classLabel")
                    ?.Tokens.FirstOrDefault()
                    ?.Equals("asteroidfield", StringComparison.OrdinalIgnoreCase) != true
            )
                continue;
            var bad = false;
            T Read<T>(string key, Func<string, T> get)
            {
                try
                {
                    return get(key);
                }
                catch (DefinitionException)
                {
                    gaps.Add(MissingKey, $"{o.Class}: {key}", o);
                    bad = true;

                    return default!;
                }
            }

            var min = Read("minAsteroids", d.GetInt);
            var max = Read("maxAsteroids", d.GetInt);
            var radius = Read("fieldRadius", d.GetFloat);
            var spin = Read("rotationSpeed", d.GetFloat);
            var orbit = Read("orbitingSpeed", d.GetFloat);
            if (bad)
                continue;
            if (min < 0 || max < 0 || max > MaxRocksPerField)
            {
                gaps.Add(BadCount, o.Class, o);

                continue;
            }

            if (min > max)
            {
                gaps.Add(MinAboveMax, o.Class, o);

                continue;
            }

            if (!(radius > 0))
            {
                gaps.Add(BadRadius, o.Class, o);

                continue;
            }

            foreach (var missing in ModelNames.Except(present))
            {
                gaps.Add(MissingModel, missing, o);
            }

            if (present.Count == 0)
            {
                gaps.Add(NoModel, o.Class, o);

                continue;
            }

            fields.Add(
                new AsteroidField
                {
                    Center = new Vector3(o.Position.X, o.Position.Y, -o.Position.Z),
                    Min = min,
                    Max = max,
                    Radius = radius,
                    RotationSpeed = spin,
                    OrbitingSpeed = orbit,
                    Seed = SeedOf(map, o),
                    Models = present,
                }
            );
        }

        return (fields, gaps.Entries);
    }

    // The seed of a field: FNV-1a over the map's own content (its description, mission file, terrain name, and
    // object count) and the field's handle and id. The typed map name and string.GetHashCode play no part, so the same
    // map gives the same rocks on every start.
    static uint SeedOf(BznMap map, MapObject field)
    {
        var hash = 2166136261u;
        foreach (
            var b in Encoding.UTF8.GetBytes(
                $"{map.Description}\0{map.MissionFile}\0{map.TerrainName}\0{map.Objects.Count}\0{field.Handle}\0{field.Id}"
            )
        )
        {
            hash = (hash ^ b) * 16777619u;
        }

        return hash;
    }
}
