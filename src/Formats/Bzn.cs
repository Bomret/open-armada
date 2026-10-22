using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;

namespace OpenArmada.Formats;

/// <summary>One value of the binary part of a map: type code, offset of the token in the file, payload bytes.</summary>
public readonly record struct MapToken
{
    /// <summary>The token's type code, 0 to 11.</summary>
    public required byte Type { get; init; }

    /// <summary>The file offset of the token's 8-byte header.</summary>
    public required int Offset { get; init; }

    /// <summary>A copy of the token's payload bytes.</summary>
    public required byte[] Data { get; init; }
}

/// <summary>The playable area. X and Z are the ground plane, Y is up. The bounds start at 0 in every shipped map.</summary>
public sealed record MapBounds
{
    /// <summary>The lowest X of the playable area.</summary>
    public required float MinX { get; init; }

    /// <summary>The highest X of the playable area.</summary>
    public required float MaxX { get; init; }

    /// <summary>The lowest Y (height) of the playable area.</summary>
    public required float MinY { get; init; }

    /// <summary>The highest Y (height) of the playable area.</summary>
    public required float MaxY { get; init; }

    /// <summary>The lowest Z of the playable area.</summary>
    public required float MinZ { get; init; }

    /// <summary>The highest Z of the playable area.</summary>
    public required float MaxZ { get; init; }

    /// <summary>
    /// The extent along X: <see cref="MaxX"/> minus <see cref="MinX"/>; positive for a map that <see cref="Bzn.Read"/>
    /// returns.
    /// </summary>
    public float Width => MaxX - MinX;

    /// <summary>
    /// The extent along Z: <see cref="MaxZ"/> minus <see cref="MinZ"/>; positive for a map that <see cref="Bzn.Read"/>
    /// returns.
    /// </summary>
    public float Depth => MaxZ - MinZ;

    /// <summary>
    /// Where a ground position lands in the map's square overview image of <c>size</c> pixels: uniform scale,
    /// the longer side fills the image, the shorter one is centred. Image right is +X and image top is
    /// maximum Z (north), so the map is not mirrored; in Godot's frame, where models mirror Z, north is -Z.
    /// </summary>
    public (float Col, float Row) ToImage(float x, float z, float size)
    {
        var longest = Math.Max(Width, Depth);

        return (
            (x - MinX + (longest - Width) / 2) / longest * size,
            size - (z - MinZ + (longest - Depth) / 2) / longest * size
        );
    }

    /// <summary>
    /// Ground position of a start cell (derived from data, within one cell): the 118-cell grid stretches over
    /// the whole map on each axis, row 0 at the top (maximum Z).
    /// </summary>
    public (float X, float Z) FromStartCell(StartCell c) =>
        (MinX + c.X / (float)Mdf.MaxCell * Width, MinZ + (1 - c.Y / (float)Mdf.MaxCell) * Depth);

    /// <summary>Whether a ground position lies inside the area on X and Z, edges included; Y is not checked.</summary>
    public bool Contains(float x, float z) => x >= MinX && x <= MaxX && z >= MinZ && z <= MaxZ;
}

/// <summary>
/// One game object. Prefix holds the class-specific tokens before the shared tail, Tail the 20 shared
/// tokens, both raw: the meaning of most of them is unknown (see the BZN maps page).
/// </summary>
public sealed record MapObject
{
    /// <summary>The object's class name, such as "fbattle": the text of the first header token.</summary>
    public required string Class { get; init; }

    /// <summary>The second header token as an unsigned 32-bit number, not interpreted.</summary>
    public required uint Handle { get; init; }

    /// <summary>The object's team, 0..8.</summary>
    public required int Team { get; init; }

    /// <summary>The object's label in the map, such as "ship1": the text of the fifth header token.</summary>
    public required string Label { get; init; }

    /// <summary>The sixth header token as an unsigned 32-bit number, not interpreted.</summary>
    public required uint Id { get; init; }

    /// <summary>The object's position, the fourth vector of the header's 48-byte matrix token.</summary>
    public required Vector3 Position { get; init; }

    /// <summary>The object's right axis, the first vector of the header's matrix token.</summary>
    public required Vector3 Right { get; init; }

    /// <summary>The object's up axis, the second vector of the header's matrix token.</summary>
    public required Vector3 Up { get; init; }

    /// <summary>The object's front axis, the third vector of the header's matrix token.</summary>
    public required Vector3 Front { get; init; }

    /// <summary>The object's display name: the text of the seventh tail token (32 bytes).</summary>
    public required string DisplayName { get; init; }

    /// <summary>The object's race: the text of the last tail token (64 bytes).</summary>
    public required string Race { get; init; }

    /// <summary>The raw class-specific tokens between the header and the tail, at most 64.</summary>
    public required IReadOnlyList<MapToken> Prefix { get; init; }

    /// <summary>The 20 raw shared tokens that end the object's record.</summary>
    public required IReadOnlyList<MapToken> Tail { get; init; }

    /// <summary>The file offset of the object's first header token.</summary>
    public required int Offset { get; init; }

    /// <summary>
    /// The object's starting state; null for full strength. A .bzn read leaves it null; a layout sets it (ADR 0007).
    /// </summary>
    public StartingState? Start { get; init; }
}

/// <summary>
/// The condition a placed object starts the fight in. Each value left unset is full strength.
/// </summary>
public sealed record StartingState
{
    /// <summary>The crew as a fraction from 0 to 1 of the class's crewCost; null for the full crew.</summary>
    public double? Crew { get; init; }

    /// <summary>The shields as a fraction from 0 to 1 of the class's maxHealth; null for full shields.</summary>
    public double? Shields { get; init; }

    /// <summary>The ship systems that start destroyed, by name, each once.</summary>
    public IReadOnlyList<string> DestroyedSystems { get; init; } = [];
}

/// <summary>One of the 9 team records. Fields named Unknown are read but their meaning is not known.</summary>
public sealed record MapTeam
{
    /// <summary>The record's first value, a float of unknown meaning.</summary>
    public required float Unknown1 { get; init; }

    /// <summary>The record's second value, a float of unknown meaning.</summary>
    public required float Unknown2 { get; init; }

    /// <summary>The record's third value, an int of unknown meaning.</summary>
    public required int Unknown3 { get; init; }

    /// <summary>The record's fourth value, a float of unknown meaning.</summary>
    public required float Unknown4 { get; init; }

    /// <summary>The record's fifth value, a float of unknown meaning.</summary>
    public required float Unknown5 { get; init; }

    /// <summary>The team's color value, the record's sixth value, an int.</summary>
    public required int Color { get; init; }

    /// <summary>The team's secondary color value, the record's seventh value, an int.</summary>
    public required int SecondaryColor { get; init; }
}

/// <summary>A named path of points in the map (AiPath), e.g. Camera_1.</summary>
public sealed record MapPath
{
    /// <summary>The path's name; empty when its stored name length is 0.</summary>
    public required string Name { get; init; }

    /// <summary>The path's points in file order.</summary>
    public required IReadOnlyList<Vector3> Points { get; init; }
}

/// <summary>
/// A map as <see cref="Bzn.Read"/> returns it: the text preamble and the sections of the binary part, in file order.
/// </summary>
public sealed record BznMap
{
    /// <summary>The value of the "version [1] =" line, 2050 to 2052.</summary>
    public required int Version { get; init; }

    /// <summary>The hex-decoded saveGameDesc line, as text up to its first NUL.</summary>
    public required string Description { get; init; }

    /// <summary>The msn_filename field (16 bytes).</summary>
    public required string MissionFile { get; init; }

    /// <summary>The TerrainName field (100 bytes).</summary>
    public required string TerrainName { get; init; }

    /// <summary>The playable area; finite, with a positive width and depth.</summary>
    public required MapBounds Bounds { get; init; }

    /// <summary>The ambient light color: X, Y and Z hold the AmbientRed, AmbientGreen and AmbientBlue fields.</summary>
    public required Vector3 Ambient { get; init; }

    /// <summary>The BackgroundArt field (100 bytes); empty when the map names none.</summary>
    public required string BackgroundArt { get; init; }

    /// <summary>The techTreeFilename field (128 bytes).</summary>
    public required string TechTree { get; init; }

    /// <summary>The raceName field (128 bytes).</summary>
    public required string Race { get; init; }

    /// <summary>The game objects in file order.</summary>
    public required IReadOnlyList<MapObject> Objects { get; init; }

    /// <summary>The 9 team records; index i is team i.</summary>
    public required IReadOnlyList<MapTeam> Teams { get; init; }

    /// <summary>The mission class field (40 bytes), such as "EmptyMission".</summary>
    public required string MissionClass { get; init; }

    /// <summary>The mission script field (64 bytes).</summary>
    public required string MissionScript { get; init; }

    /// <summary>The mission rules field (64 bytes).</summary>
    public required string MissionRules { get; init; }

    /// <summary>The mission name field (64 bytes).</summary>
    public required string MissionName { get; init; }

    /// <summary>The areas of interest, each its 6 raw tokens, not interpreted.</summary>
    public required IReadOnlyList<IReadOnlyList<MapToken>> Aois { get; init; }

    /// <summary>The named paths (AiPath) in file order.</summary>
    public required IReadOnlyList<MapPath> Paths { get; init; }
}

/// <summary>
/// Reader of the .bzn map (layout: the BZN maps page of the knowledge base, REA report in the scope).
/// Throws InvalidDataException for a corrupt or cut-off file, naming the offset; UnsupportedFormatException
/// for a branch of the format no shipped map uses.
/// </summary>
public static class Bzn
{
    // the versions the shipped maps carry
    const int FirstVersion = 2050,
        LastVersion = 2052;
    const int MaxPrefix = 64;

    /// <summary>The number of team records in a map: teams 0..8.</summary>
    public const int TeamCount = 9; // teams 0..8

    static readonly (byte, int)[] HeaderSig = { (7, 8), (4, 4), (9, 12), (4, 4), (2, 40), (8, 4), (11, 48) };
    static readonly (byte, int)[] TailSig =
    {
        (9, 12),
        (5, 4),
        (5, 4),
        (9, 12),
        (9, 12),
        (9, 12),
        (2, 32),
        (1, 1),
        (4, 4),
        (4, 4),
        (1, 1),
        (5, 4),
        (5, 4),
        (5, 4),
        (5, 4),
        (5, 4),
        (5, 4),
        (1, 1),
        (4, 4),
        (2, 64),
    };
    static readonly (byte, int)[] TeamSig = { (5, 4), (5, 4), (4, 4), (5, 4), (5, 4), (4, 4), (4, 4) };
    static readonly (byte, int)[] AoiSig = { (8, 4), (4, 4), (1, 1), (1, 1), (4, 4), (4, 4) };

    /// <summary>Reads a binary .bzn map of version 2050 to 2052 and checks that every token is accounted for.</summary>
    /// <param name="d">The whole map file.</param>
    /// <exception cref="InvalidDataException">
    /// when the text preamble or a token is cut off, a line or token is not the one the layout expects, the bounds
    /// are not a finite, non-empty area, a count does not fit in the rest of the file, an object has no tail, a
    /// non-finite position or orientation, or a team outside 0..8, or tokens follow the last section. The message
    /// names the offset.
    /// </exception>
    /// <exception cref="UnsupportedFormatException">
    /// when the version is outside 2050..2052, the map is in text form (binarySave false), it is a save game
    /// (missionSave false), or it remaps its bounds (rescale true).
    /// </exception>
    public static BznMap Read(byte[] d)
    {
        var p = 0;
        string Line()
        {
            var end = Array.IndexOf(d, (byte)'\n', p);
            if (end < 0)
                throw new InvalidDataException($"not a BZN map: the text preamble is cut off (offset {d.Length})");
            var s = Encoding.ASCII.GetString(d, p, end - p).TrimEnd('\r');
            p = end + 1;

            return s;
        }

        if (Line() != "version [1] =" || !int.TryParse(Line(), out var version))
            throw new InvalidDataException("not a BZN map: no version line");
        if (version is < FirstVersion or > LastVersion)
            throw new UnsupportedFormatException(
                $"BZN version {version} is not read (only {FirstVersion} to {LastVersion})"
            );
        var desc = Line();
        if (!desc.StartsWith("saveGameDesc = ", StringComparison.Ordinal))
            throw new InvalidDataException("BZN has no saveGameDesc line");
        byte[] descBytes;
        try
        {
            descBytes = Convert.FromHexString(desc["saveGameDesc = ".Length..]);
        }
        catch (FormatException)
        {
            throw new InvalidDataException("BZN saveGameDesc is not hex text");
        }

        if (Line() != "binarySave [1] =")
            throw new InvalidDataException("BZN has no binarySave line");
        if (Line() != "true")
            throw new UnsupportedFormatException("BZN in text form (binarySave false) is not read");

        var c = new Cursor(d, p);
        var missionFile = c.Str(16, "msn_filename");
        if (!c.Bool("missionSave"))
            throw new UnsupportedFormatException("BZN save game (missionSave false) is not read");
        var terrain = c.Str(100, "TerrainName");

        var b = new float[6];
        for (var i = 0; i < 6; i++)
        {
            b[i] = c.Float("map bounds");
        }

        if (c.Bool("bounds rescale"))
            throw new UnsupportedFormatException("BZN with a bounds remap (rescale true) is not read");
        var bounds = new MapBounds
        {
            MinX = b[0],
            MaxX = b[1],
            MinY = b[2],
            MaxY = b[3],
            MinZ = b[4],
            MaxZ = b[5],
        };
        if (b.Any(f => !float.IsFinite(f)) || bounds.Width <= 0 || bounds.Depth <= 0)
            throw new InvalidDataException($"map bounds are not a finite, non-empty area (offset {c.Offset})");

        var ambient = new Vector3(c.Float("AmbientRed"), c.Float("AmbientGreen"), c.Float("AmbientBlue"));
        var background = c.Str(100, "BackgroundArt");
        var tech = c.Str(128, "techTreeFilename");
        var race = c.Str(128, "raceName");

        var objects = ReadObjects(c);
        var teams = new List<MapTeam>();
        for (var i = 0; i < TeamCount; i++)
        {
            var t = c.Sig(TeamSig, $"team {i}");
            teams.Add(
                new MapTeam
                {
                    Unknown1 = Cursor.F(t[0]),
                    Unknown2 = Cursor.F(t[1]),
                    Unknown3 = Cursor.I(t[2]),
                    Unknown4 = Cursor.F(t[3]),
                    Unknown5 = Cursor.F(t[4]),
                    Color = Cursor.I(t[5]),
                    SecondaryColor = Cursor.I(t[6]),
                }
            );
        }

        var missionClass = c.Str(40, "mission class");
        c.Next(8, 4, "mission pointer");
        var script = c.Str(64, "mission script");
        var rules = c.Str(64, "mission rules");
        var missionName = Cursor.Text(c.Next(0, 64, "mission name").Data);

        var aoiCount = c.Count("AOI count", AoiSig.Length);
        var aois = new List<IReadOnlyList<MapToken>>();
        for (var i = 0; i < aoiCount; i++)
        {
            aois.Add(c.Sig(AoiSig, $"AOI {i}"));
        }

        var pathCount = c.Count("path count", 6);
        var paths = new List<MapPath>();
        for (var i = 0; i < pathCount; i++)
        {
            c.Next(0, 4, "path pointer");
            var nameLen = c.Int("path name length");
            if (nameLen < 0 || nameLen > d.Length)
                throw new InvalidDataException($"path {i} name length {nameLen} does not fit (offset {c.Offset})");
            var name = nameLen > 0 ? Cursor.Text(c.Next(2, nameLen, "path name").Data) : "";
            var n = c.Int("path point count");
            if (n < 0 || n > d.Length / 12)
                throw new InvalidDataException($"path '{name}' point count {n} does not fit (offset {c.Offset})");
            var raw = c.Next(9, n * 12, "path points").Data;
            c.Next(0, 4, "path field");
            c.Next(5, 4, "path float");
            paths.Add(
                new MapPath { Name = name, Points = Enumerable.Range(0, n).Select(k => Vec(raw, k * 12)).ToList() }
            );
        }

        c.End();

        return new BznMap
        {
            Version = version,
            Description = Cursor.Text(descBytes),
            MissionFile = missionFile,
            TerrainName = terrain,
            Bounds = bounds,
            Ambient = ambient,
            BackgroundArt = background,
            TechTree = tech,
            Race = race,
            Objects = objects,
            Teams = teams,
            MissionClass = missionClass,
            MissionScript = script,
            MissionRules = rules,
            MissionName = missionName,
            Aois = aois,
            Paths = paths,
        };
    }

    static List<MapObject> ReadObjects(Cursor c)
    {
        var count = c.Count("object count", HeaderSig.Length + TailSig.Length);
        var objects = new List<MapObject>();
        for (var k = 0; k < count; k++)
        {
            var offset = c.Offset;
            var h = c.Sig(HeaderSig, $"object {k} header");
            var skip = 0;
            // The prefix ends at the tail; the next object's header inside the prefix means this record has no tail.
            while (skip <= MaxPrefix && !c.Matches(TailSig, skip) && !c.Matches(HeaderSig, skip))
            {
                skip++;
            }

            if (skip > MaxPrefix || !c.Matches(TailSig, skip))
                throw new InvalidDataException(
                    $"object {k} ({Cursor.Text(h[0].Data)}) at offset {offset}: no object tail before the next object or within {MaxPrefix} tokens, an unrecognized class record"
                );
            var prefix = c.Take(skip);
            var tail = c.Sig(TailSig, $"object {k} tail");

            var cls = Cursor.Text(h[0].Data);
            var label = Cursor.Text(h[4].Data);
            var team = BitConverter.ToInt32(h[3].Data);
            var m = h[6].Data;
            var vectors = new[] { Vec(h[2].Data, 0), Vec(m, 0), Vec(m, 12), Vec(m, 24), Vec(m, 36) };
            if (vectors.Any(v => !float.IsFinite(v.X) || !float.IsFinite(v.Y) || !float.IsFinite(v.Z)))
                throw new InvalidDataException(
                    $"object {k} '{label}' ({cls}) at offset {offset} has a non-finite position or orientation"
                );
            if (team is < 0 or >= TeamCount)
                throw new InvalidDataException(
                    $"object {k} '{label}' ({cls}) at offset {offset} has team {team}; the game's teams are 0..{TeamCount - 1}"
                );
            objects.Add(
                new MapObject
                {
                    Class = cls,
                    Handle = BitConverter.ToUInt32(h[1].Data),
                    Team = team,
                    Label = label,
                    Id = BitConverter.ToUInt32(h[5].Data),
                    Position = Vec(m, 36),
                    Right = Vec(m, 0),
                    Up = Vec(m, 12),
                    Front = Vec(m, 24),
                    DisplayName = Cursor.Text(tail[6].Data),
                    Race = Cursor.Text(tail[19].Data),
                    Prefix = prefix,
                    Tail = tail,
                    Offset = offset,
                }
            );
        }

        return objects;
    }

    static Vector3 Vec(byte[] d, int at) =>
        new(BitConverter.ToSingle(d, at), BitConverter.ToSingle(d, at + 4), BitConverter.ToSingle(d, at + 8));

    // The binary part as a list of tokens, walked once up front. Each token is type, 3 filler bytes, u32
    // payload size, payload; a token that does not fit the file ends the walk with an error, so no size or
    // count read later allocates more than the file holds.
    sealed class Cursor
    {
        readonly byte[] d;
        readonly List<MapToken> t = new();
        int pos;

        public Cursor(byte[] d, int start)
        {
            this.d = d;
            var p = start;
            while (p < d.Length)
            {
                if (d.Length - p < 8)
                    throw new InvalidDataException($"map is cut off in a token header at offset {p}");
                var type = d[p];
                if (type > 11)
                    throw new InvalidDataException($"unrecognized token type {type} at offset {p}");
                var size = BitConverter.ToUInt32(d, p + 4);
                if (size > d.Length - p - 8)
                    throw new InvalidDataException($"map is cut off in a token of {size} bytes at offset {p}");
                t.Add(
                    new MapToken
                    {
                        Type = type,
                        Offset = p,
                        Data = d[(p + 8)..(p + 8 + (int)size)],
                    }
                );
                p += 8 + (int)size;
            }
        }

        // File offset of the next token, or the end of the file.
        public int Offset => pos < t.Count ? t[pos].Offset : d.Length;

        public MapToken Next(byte type, int size, string what)
        {
            if (pos >= t.Count)
                throw new InvalidDataException($"map ends before {what} (offset {d.Length})");
            var k = t[pos];
            if (k.Type != type || k.Data.Length != size)
                throw new InvalidDataException(
                    $"{what}: expected type {type} of {size} bytes, found type {k.Type} of {k.Data.Length} bytes at offset {k.Offset}"
                );
            pos++;

            return k;
        }

        public MapToken[] Sig((byte Type, int Size)[] sig, string what) =>
            sig.Select(s => Next(s.Type, s.Size, what)).ToArray();

        public bool Matches((byte Type, int Size)[] sig, int skip)
        {
            if (pos + skip + sig.Length > t.Count)
                return false;
            for (var i = 0; i < sig.Length; i++)
            {
                var k = t[pos + skip + i];
                if (k.Type != sig[i].Type || k.Data.Length != sig[i].Size)
                    return false;
            }

            return true;
        }

        public MapToken[] Take(int n)
        {
            var r = t.GetRange(pos, n).ToArray();
            pos += n;

            return r;
        }

        public int Int(string what) => I(Next(4, 4, what));

        // An int token that counts records of at least `minTokens` tokens each: it cannot exceed what is left.
        public int Count(string what, int minTokens)
        {
            var n = Int(what);
            if (n < 0 || n > (t.Count - pos) / minTokens)
                throw new InvalidDataException($"{what} {n} does not fit in the rest of the file (offset {Offset})");

            return n;
        }

        public float Float(string what) => F(Next(5, 4, what));

        public bool Bool(string what) => Next(1, 1, what).Data[0] != 0;

        public string Str(int size, string what) => Text(Next(2, size, what).Data);

        public static float F(MapToken k) => BitConverter.ToSingle(k.Data);

        public static int I(MapToken k) => BitConverter.ToInt32(k.Data);

        // Text up to the first NUL; the bytes after it are uninitialised writer memory.
        public static string Text(byte[] b)
        {
            var end = Array.IndexOf(b, (byte)0);

            return Windows1252.Encoding.GetString(b, 0, end < 0 ? b.Length : end);
        }

        public void End()
        {
            if (pos < t.Count)
                throw new InvalidDataException(
                    $"unexpected token (type {t[pos].Type}, {t[pos].Data.Length} bytes) after the last section at offset {t[pos].Offset}"
                );
        }
    }
}
