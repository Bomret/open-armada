using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using OpenArmada.Assets;
using OpenArmada.Formats;

namespace OpenArmada.Sandbox;

// One rock of an asteroid field: its model and its pose at any time t (seconds), a pure function of t so the
// pose never depends on the frame step. The rock sits at Offset from the field's Center at t = 0.
//   stand-in: the motion. The game simulates the rocks' mutual gravity (Armada.exe, REA: FUN_0044e090); the
//   sandbox lets each rock circle the field's center about the vertical axis at the field's orbitingSpeed and
//   spin about its own vertical axis at the field's rotationSpeed. Both units are radians per second (the
//   game's unit of these two values is not known).
public sealed record Rock(string Model, Vector3 Center, Vector3 Offset, Quaternion Initial, float Spin, float Orbit)
{
    public (Vector3 Position, Quaternion Orientation) Pose(double t) =>
        (Center + Vector3.Transform(Offset, Quaternion.CreateFromAxisAngle(Vector3.UnitY, (float)(Orbit * t))),
         Quaternion.Normalize(Initial * Quaternion.CreateFromAxisAngle(Vector3.UnitY, (float)(Spin * t))));
}

// The values of one asteroid field, from its definition and the map: Center is in Godot's frame (Z mirrored),
// Models are the asteroid models the install holds.
public sealed record AsteroidField(Vector3 Center, int Min, int Max, float Radius, float RotationSpeed, float OrbitingSpeed,
    uint Seed, IReadOnlyList<string> Models)
{
    // The game's eight hard-coded asteroid models (Armada.exe, REA: FUN_0044b200); master8 is not used.
    public static readonly IReadOnlyList<string> ModelNames = new[] { "master", "master1", "master2", "master3", "master4", "master5", "master6", "master7" };

    // The rocks the field shows: a count in [Min, Max], each a random model of Models, placed inside the sphere
    // of Radius less `largestExtent` (the largest rock's extent), flattened ×0.5 in height, with random orientation.
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
            var offset = direction * (reach * MathF.Cbrt((float)random.NextDouble())) * new Vector3(1, 0.5f, 1);
            rocks.Add(new Rock(model, Center, offset, Quaternion.Normalize(new Quaternion(Unit(random), (float)random.NextDouble() * 2 - 1)), RotationSpeed, OrbitingSpeed));
        }
        return rocks;
    }

    // A uniformly random unit-length direction (rejection sampling in the unit ball).
    static Vector3 Unit(Random random)
    {
        while (true)
        {
            var v = new Vector3((float)random.NextDouble() * 2 - 1, (float)random.NextDouble() * 2 - 1, (float)random.NextDouble() * 2 - 1);
            var length = v.Length();
            if (length is > 0.01f and <= 1) return v / length;
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

    // The fields of the map, each from its definition (a class whose classLabel is asteroidfield), and the gaps
    // found: a field with a missing or illegal value is reported and left out, a missing model is reported and
    // the rest used (INV-0002). Reports are grouped by cause; the count is the number of fields the cause touches.
    public static (IReadOnlyList<AsteroidField> Fields, IReadOnlyList<ReportEntry> Reports) Collect(
        BznMap map, IReadOnlyDictionary<string, ResolvedDefinition> definitions, Func<string, bool> hasModel)
    {
        var gaps = new List<(string Cause, string Name, MapObject Field)>();
        var present = ModelNames.Where(hasModel).ToList();
        var fields = new List<AsteroidField>();
        foreach (var o in map.Objects)
        {
            if (!definitions.TryGetValue(o.Class, out var d)
                || d.Find("classLabel")?.Tokens.FirstOrDefault()?.Equals("asteroidfield", StringComparison.OrdinalIgnoreCase) != true) continue;
            var bad = false;
            T Read<T>(string key, Func<string, T> get)
            {
                try { return get(key); }
                catch (DefinitionException) { gaps.Add((MissingKey, $"{o.Class}: {key}", o)); bad = true; return default!; }
            }
            var min = Read("minAsteroids", d.GetInt);
            var max = Read("maxAsteroids", d.GetInt);
            var radius = Read("fieldRadius", d.GetFloat);
            var spin = Read("rotationSpeed", d.GetFloat);
            var orbit = Read("orbitingSpeed", d.GetFloat);
            if (bad) continue;
            if (min < 0 || max < 0 || max > MaxRocksPerField) { gaps.Add((BadCount, o.Class, o)); continue; }
            if (min > max) { gaps.Add((MinAboveMax, o.Class, o)); continue; }
            if (!(radius > 0)) { gaps.Add((BadRadius, o.Class, o)); continue; }
            foreach (var missing in ModelNames.Except(present)) gaps.Add((MissingModel, missing, o));
            if (present.Count == 0) { gaps.Add((NoModel, o.Class, o)); continue; }
            fields.Add(new AsteroidField(new Vector3(o.Position.X, o.Position.Y, -o.Position.Z), min, max, radius, spin, orbit, SeedOf(map, o), present));
        }
        var reports = gaps.GroupBy(g => g.Cause)
            .Select(g => new ReportEntry(g.Key, g.Select(x => x.Name).Distinct().ToList(), g.Select(x => x.Field).Distinct().Count())).ToList();
        return (fields, reports);
    }

    // The seed of a field: FNV-1a over the map's own content (its description, mission file, terrain name, and
    // object count) and the field's handle and id. The typed map name and string.GetHashCode play no part, so the same
    // map gives the same rocks on every start.
    static uint SeedOf(BznMap map, MapObject field)
    {
        var hash = 2166136261u;
        foreach (var b in Encoding.UTF8.GetBytes($"{map.Description}\0{map.MissionFile}\0{map.TerrainName}\0{map.Objects.Count}\0{field.Handle}\0{field.Id}"))
            hash = (hash ^ b) * 16777619u;
        return hash;
    }
}
