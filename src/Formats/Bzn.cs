using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;

namespace OpenArmada.Formats;

// One value of the binary part of a map: type code, offset of the token in the file, payload bytes.
public readonly record struct MapToken(byte Type, int Offset, byte[] Data);

// The playable area. X and Z are the ground plane, Y is up. The bounds start at 0 in every shipped map.
public sealed record MapBounds(float MinX, float MaxX, float MinY, float MaxY, float MinZ, float MaxZ)
{
    public float Width => MaxX - MinX;
    public float Depth => MaxZ - MinZ;

    // Where a ground position lands in the map's square overview image of `size` pixels: uniform scale,
    // the longer side fills the image, the shorter one is centred. Image right is +X and image top is
    // maximum Z (north), so the map is not mirrored; in Godot's frame, where models mirror Z, north is -Z.
    public (float Col, float Row) ToImage(float x, float z, float size)
    {
        var longest = Math.Max(Width, Depth);
        return ((x - MinX + (longest - Width) / 2) / longest * size, size - (z - MinZ + (longest - Depth) / 2) / longest * size);
    }

    public bool Contains(float x, float z) => x >= MinX && x <= MaxX && z >= MinZ && z <= MaxZ;
}

// One game object. Prefix holds the class-specific tokens before the shared tail, Tail the 20 shared
// tokens, both raw: the meaning of most of them is unknown (see the BZN maps page).
public sealed record MapObject(string Class, uint Handle, int Team, string Label, uint Id, Vector3 Position,
    Vector3 Right, Vector3 Up, Vector3 Front, string DisplayName, string Race,
    IReadOnlyList<MapToken> Prefix, IReadOnlyList<MapToken> Tail, int Offset);

// One of the 9 team records. Fields named Unknown are read but their meaning is not known.
public sealed record MapTeam(float Unknown1, float Unknown2, int Unknown3, float Unknown4, float Unknown5, int Color, int SecondaryColor);

// A named path of points in the map (AiPath), e.g. Camera_1.
public sealed record MapPath(string Name, IReadOnlyList<Vector3> Points);

public sealed record BznMap(int Version, string Description, string MissionFile, string TerrainName, MapBounds Bounds,
    Vector3 Ambient, string BackgroundArt, string TechTree, string Race,
    IReadOnlyList<MapObject> Objects, IReadOnlyList<MapTeam> Teams,
    string MissionClass, string MissionScript, string MissionRules, string MissionName,
    IReadOnlyList<IReadOnlyList<MapToken>> Aois, IReadOnlyList<MapPath> Paths);

// Reader of the .bzn map (layout: the BZN maps page of the knowledge base, REA report in the scope).
// Throws InvalidDataException for a corrupt or cut-off file, naming the offset; UnsupportedFormatException
// for a branch of the format no shipped map uses.
public static class Bzn
{
    const int FirstVersion = 2050, LastVersion = 2052; // the versions the shipped maps carry
    const int MaxPrefix = 64;
    public const int TeamCount = 9; // teams 0..8

    static readonly (byte, int)[] HeaderSig = { (7, 8), (4, 4), (9, 12), (4, 4), (2, 40), (8, 4), (11, 48) };
    static readonly (byte, int)[] TailSig =
    {
        (9, 12), (5, 4), (5, 4), (9, 12), (9, 12), (9, 12), (2, 32), (1, 1), (4, 4), (4, 4), (1, 1),
        (5, 4), (5, 4), (5, 4), (5, 4), (5, 4), (5, 4), (1, 1), (4, 4), (2, 64),
    };
    static readonly (byte, int)[] TeamSig = { (5, 4), (5, 4), (4, 4), (5, 4), (5, 4), (4, 4), (4, 4) };
    static readonly (byte, int)[] AoiSig = { (8, 4), (4, 4), (1, 1), (1, 1), (4, 4), (4, 4) };

    public static BznMap Read(byte[] d)
    {
        var p = 0;
        string Line()
        {
            var end = Array.IndexOf(d, (byte)'\n', p);
            if (end < 0) throw new InvalidDataException("not a BZN map: the text preamble is cut off");
            var s = Encoding.ASCII.GetString(d, p, end - p).TrimEnd('\r');
            p = end + 1;
            return s;
        }
        if (Line() != "version [1] =" || !int.TryParse(Line(), out var version)) throw new InvalidDataException("not a BZN map: no version line");
        if (version is < FirstVersion or > LastVersion)
            throw new UnsupportedFormatException($"BZN version {version} is not read (only {FirstVersion} to {LastVersion})");
        var desc = Line();
        if (!desc.StartsWith("saveGameDesc = ", StringComparison.Ordinal)) throw new InvalidDataException("BZN has no saveGameDesc line");
        byte[] descBytes;
        try { descBytes = Convert.FromHexString(desc["saveGameDesc = ".Length..]); }
        catch (FormatException) { throw new InvalidDataException("BZN saveGameDesc is not hex text"); }
        if (Line() != "binarySave [1] =") throw new InvalidDataException("BZN has no binarySave line");
        if (Line() != "true") throw new UnsupportedFormatException("BZN in text form (binarySave false) is not read");

        var c = new Cursor(d, p);
        var missionFile = c.Str(16, "msn_filename");
        if (!c.Bool("missionSave")) throw new UnsupportedFormatException("BZN save game (missionSave false) is not read");
        var terrain = c.Str(100, "TerrainName");

        var b = new float[6];
        for (var i = 0; i < 6; i++) b[i] = c.Float("map bounds");
        if (c.Bool("bounds rescale")) throw new UnsupportedFormatException("BZN with a bounds remap (rescale true) is not read");
        var bounds = new MapBounds(b[0], b[1], b[2], b[3], b[4], b[5]);
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
            teams.Add(new MapTeam(Cursor.F(t[0]), Cursor.F(t[1]), Cursor.I(t[2]), Cursor.F(t[3]), Cursor.F(t[4]), Cursor.I(t[5]), Cursor.I(t[6])));
        }

        var missionClass = c.Str(40, "mission class");
        c.Next(8, 4, "mission pointer");
        var script = c.Str(64, "mission script");
        var rules = c.Str(64, "mission rules");
        var missionName = Cursor.Text(c.Next(0, 64, "mission name").Data);

        var aoiCount = c.Count("AOI count", AoiSig.Length);
        var aois = new List<IReadOnlyList<MapToken>>();
        for (var i = 0; i < aoiCount; i++) aois.Add(c.Sig(AoiSig, $"AOI {i}"));

        var pathCount = c.Count("path count", 6);
        var paths = new List<MapPath>();
        for (var i = 0; i < pathCount; i++)
        {
            c.Next(0, 4, "path pointer");
            var nameLen = c.Int("path name length");
            if (nameLen < 0 || nameLen > d.Length) throw new InvalidDataException($"path {i} name length {nameLen} does not fit (offset {c.Offset})");
            var name = nameLen > 0 ? Cursor.Text(c.Next(2, nameLen, "path name").Data) : "";
            var n = c.Int("path point count");
            if (n < 0 || n > d.Length / 12) throw new InvalidDataException($"path '{name}' point count {n} does not fit (offset {c.Offset})");
            var raw = c.Next(9, n * 12, "path points").Data;
            c.Next(0, 4, "path field");
            c.Next(5, 4, "path float");
            paths.Add(new MapPath(name, Enumerable.Range(0, n).Select(k => Vec(raw, k * 12)).ToList()));
        }
        c.End();

        return new BznMap(version, Cursor.Text(descBytes), missionFile, terrain, bounds, ambient, background, tech, race,
            objects, teams, missionClass, script, rules, missionName, aois, paths);
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
            while (skip <= MaxPrefix && !c.Matches(TailSig, skip)) skip++;
            if (skip > MaxPrefix)
                throw new InvalidDataException($"object {k} ({Cursor.Text(h[0].Data)}) at offset {offset}: no object tail within {MaxPrefix} tokens, an unrecognized class record");
            var prefix = c.Take(skip);
            var tail = c.Sig(TailSig, $"object {k} tail");

            var cls = Cursor.Text(h[0].Data);
            var label = Cursor.Text(h[4].Data);
            var team = BitConverter.ToInt32(h[3].Data);
            var m = h[6].Data;
            var floats = Enumerable.Range(0, 12).Select(i => BitConverter.ToSingle(m, i * 4)).Append(BitConverter.ToSingle(h[2].Data, 0)).ToList();
            if (floats.Any(f => !float.IsFinite(f)))
                throw new InvalidDataException($"object {k} '{label}' ({cls}) at offset {offset} has a non-finite position or orientation");
            if (team is < 0 or >= TeamCount)
                throw new InvalidDataException($"object {k} '{label}' ({cls}) at offset {offset} has team {team}; the game's teams are 0..{TeamCount - 1}");
            objects.Add(new MapObject(cls, BitConverter.ToUInt32(h[1].Data), team, label, BitConverter.ToUInt32(h[5].Data), Vec(m, 36),
                Vec(m, 0), Vec(m, 12), Vec(m, 24), Cursor.Text(tail[6].Data), Cursor.Text(tail[19].Data), prefix, tail, offset));
        }
        return objects;
    }

    static Vector3 Vec(byte[] d, int at) => new(BitConverter.ToSingle(d, at), BitConverter.ToSingle(d, at + 4), BitConverter.ToSingle(d, at + 8));

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
                if (d.Length - p < 8) throw new InvalidDataException($"map is cut off in a token header at offset {p}");
                var type = d[p];
                if (type > 11) throw new InvalidDataException($"unrecognized token type {type} at offset {p}");
                var size = BitConverter.ToUInt32(d, p + 4);
                if (size > d.Length - p - 8) throw new InvalidDataException($"map is cut off in a token of {size} bytes at offset {p}");
                t.Add(new MapToken(type, p, d[(p + 8)..(p + 8 + (int)size)]));
                p += 8 + (int)size;
            }
        }

        // File offset of the next token, or the end of the file.
        public int Offset => pos < t.Count ? t[pos].Offset : d.Length;

        public MapToken Next(byte type, int size, string what)
        {
            if (pos >= t.Count) throw new InvalidDataException($"map ends before {what} (offset {d.Length})");
            var k = t[pos];
            if (k.Type != type || k.Data.Length != size)
                throw new InvalidDataException($"{what}: expected type {type} of {size} bytes, found type {k.Type} of {k.Data.Length} bytes at offset {k.Offset}");
            pos++;
            return k;
        }

        public MapToken[] Sig((byte Type, int Size)[] sig, string what) => sig.Select(s => Next(s.Type, s.Size, what)).ToArray();

        public bool Matches((byte Type, int Size)[] sig, int skip)
        {
            if (pos + skip + sig.Length > t.Count) return false;
            for (var i = 0; i < sig.Length; i++)
            {
                var k = t[pos + skip + i];
                if (k.Type != sig[i].Type || k.Data.Length != sig[i].Size) return false;
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
            if (n < 0 || n > (t.Count - pos) / minTokens) throw new InvalidDataException($"{what} {n} does not fit in the rest of the file (offset {Offset})");
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
            if (pos < t.Count) throw new InvalidDataException($"unexpected token (type {t[pos].Type}, {t[pos].Data.Length} bytes) after the last section at offset {t[pos].Offset}");
        }
    }
}
