using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using OpenArmada.Assets;
using OpenArmada.Formats;

namespace OpenArmada.Sandbox;

/// <summary>
/// An asteroid field object of the map as the path costs read it (0x44e7b0): a field without a path costs a square
/// around its <see cref="Center"/>, and a belt (a field that follows a path of type 3) costs squares along its
/// <see cref="Belt"/>. Neither is drawn by the plan.
/// </summary>
public sealed record AsteroidSite
{
    /// <summary>The map object's label.</summary>
    public required string Label { get; init; }

    /// <summary>The map object's class, the name of its definition without ".odf".</summary>
    public required string Class { get; init; }

    /// <summary>The field object's position in Godot's frame (Z mirrored).</summary>
    public required Vector3 Center { get; init; }

    /// <summary>
    /// The points of the belt's path in Godot's frame (Z mirrored), in file order; null for a field that is no belt.
    /// </summary>
    public IReadOnlyList<Vector3>? Belt { get; init; }

    const string FieldLabel = "asteroidfield"; // AsteroidFieldClass names itself so
    const int BeltPathType = 3; // 0x44e7b0 keeps a path only of this type
    const string BadBelt = "asteroid belt record cannot be read, the field planned without a belt";
    const string MissingPath = "asteroid belt path missing, the field planned without a belt";

    // The class-specific tokens of an asteroid field object, as the loader 0x44f8e0 reads them: have_belt, and when set the
    // closest offset (a vector), three floats, the length of the path's label (an int), and the label.
    static readonly byte[] BeltTokenTypes = [9, 5, 5, 5, 4, 2];

    /// <summary>
    /// The asteroid field objects of the map (a class whose classLabel is asteroidfield), whether or not their rocks can
    /// be planned. A field whose record names a path of the map's of type 3 is a belt. A record that cannot be read, and a
    /// path the map lacks, are gaps naming the class or the path, and the field is planned without a belt (INV-0002); a
    /// path of another type is dropped without a gap, as the game does (0x44e7b0). A field without a record, such as a
    /// layout's, is no belt.
    /// </summary>
    internal static IReadOnlyList<AsteroidSite> Collect(
        BznMap map,
        IReadOnlyDictionary<string, ResolvedDefinition> definitions,
        Gaps gaps
    )
    {
        var sites = new List<AsteroidSite>();
        foreach (var o in map.Objects)
        {
            if (
                !definitions.TryGetValue(o.Class, out var d)
                || d.Find("classLabel")?.Tokens.FirstOrDefault()?.Equals(FieldLabel, StringComparison.OrdinalIgnoreCase)
                    != true
            )
                continue;
            sites.Add(
                new AsteroidSite
                {
                    Label = o.Label,
                    Class = o.Class,
                    Center = new Vector3(o.Position.X, o.Position.Y, -o.Position.Z),
                    Belt = BeltOf(o, map.Paths, gaps),
                }
            );
        }

        return sites;
    }

    static IReadOnlyList<Vector3>? BeltOf(MapObject o, IReadOnlyList<MapPath> paths, Gaps gaps)
    {
        var record = o.Prefix;
        if (record.Count == 0 || record[0].Type != 1 || record[0].Data.Length != 1 || record[0].Data[0] == 0)
            return null;
        if (
            record.Count < 1 + BeltTokenTypes.Length
            || !BeltTokenTypes.Select((t, i) => record[1 + i].Type == t).All(ok => ok)
        )
        {
            gaps.Add(BadBelt, o.Class, o);

            return null;
        }

        var data = record[^1].Data;
        var end = Array.IndexOf(data, (byte)0);
        var label = Windows1252.Encoding.GetString(data, 0, end < 0 ? data.Length : end);
        if (paths.FirstOrDefault(p => p.Name == label) is not { } path)
        {
            gaps.Add(MissingPath, label, o);

            return null;
        }

        return path.Type == BeltPathType ? path.Points.Select(p => new Vector3(p.X, p.Y, -p.Z)).ToList() : null;
    }
}
