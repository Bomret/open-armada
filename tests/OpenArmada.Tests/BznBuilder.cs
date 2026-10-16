using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;

namespace OpenArmada.Tests;

// Builds .bzn maps in code (INV-0001): the ASCII preamble, then binary tokens of
// type byte, 3 filler bytes, u32 payload size, payload.
internal sealed class BznBuilder
{
    internal sealed record Obj(string Class, int Team, Vector3 Position, string Label = "obj", uint Pointer = 1,
        byte[][]? Prefix = null, float Yaw = 0);

    internal int Version = 2051;
    internal string Description = "Test Map";
    internal float[] Bounds = { 0, 6400, -28.5f, 105.5f, 0, 6400 };
    internal List<Obj> Objects = new();
    internal List<(string Name, Vector3[] Points)> Paths = new();
    internal int? CountOverride; // the object count written, to build a map whose count lies
    internal List<byte[]>? Extra; // tokens appended after the AiPaths, to build corrupt maps

    internal static byte[] Token(byte type, byte[] payload)
    {
        var t = new byte[8 + payload.Length];
        t[0] = type;
        t[1] = t[2] = t[3] = 0xCD; // the writer leaves these uninitialised
        BitConverter.GetBytes((uint)payload.Length).CopyTo(t, 4);
        payload.CopyTo(t, 8);
        return t;
    }

    internal static byte[] Int(int v) => Token(4, BitConverter.GetBytes(v));
    internal static byte[] Float(float v) => Token(5, BitConverter.GetBytes(v));
    internal static byte[] Bool(bool v) => Token(1, new[] { (byte)(v ? 1 : 0) });
    internal static byte[] Ptr(uint v) => Token(8, BitConverter.GetBytes(v));
    internal static byte[] Str(string s, int size) { var b = new byte[size]; Encoding.Latin1.GetBytes(s).CopyTo(b, 0); return Token(2, b); }
    internal static byte[] Name8(string s) { var b = new byte[8]; Encoding.Latin1.GetBytes(s).CopyTo(b, 0); return Token(7, b); }
    internal static byte[] Vec(Vector3 v) => Token(9, Floats(v.X, v.Y, v.Z));
    internal static byte[] Floats(params float[] f) => f.SelectMany(BitConverter.GetBytes).ToArray();

    static byte[] ObjectTokens(Obj o)
    {
        var (s, c) = MathF.SinCos(o.Yaw);
        var matrix = Floats(c, 0, -s, 0, 1, 0, s, 0, c, o.Position.X, o.Position.Y, o.Position.Z);
        var tokens = new List<byte[]>
        {
            Name8(o.Class), Int(unchecked((int)0x80008001)), Vec(o.Position), Int(o.Team), Str(o.Label, 40), Ptr(o.Pointer), Token(11, matrix),
        };
        tokens.AddRange(o.Prefix ?? Array.Empty<byte[]>());
        tokens.AddRange(new[]
        {
            Vec(o.Position), Float(0), Float(1e30f), Vec(default), Vec(default), Vec(default), Str("Display " + o.Class, 32), Bool(false),
            Int(0), Int(0), Bool(true), Float(1), Float(0), Float(100), Float(0), Float(0), Float(0), Bool(false), Int(o.Team), Str("federation", 64),
        });
        return tokens.SelectMany(t => t).ToArray();
    }

    internal byte[] Build()
    {
        var text = $"version [1] =\r\n{Version}\r\nsaveGameDesc = {Convert.ToHexString(Encoding.Latin1.GetBytes(Description).Concat(new byte[128]).Take(128).ToArray()).ToLowerInvariant()}\r\nbinarySave [1] =\r\ntrue\r\n";
        var tokens = new List<byte[]>
        {
            Str("test.bzn", 16), Bool(true), Str("test.bzn", 100),
        };
        tokens.AddRange(new[] { 0, 1, 2, 3, 4, 5 }.Select(i => Float(Bounds[i])));
        tokens.Add(Bool(false));
        tokens.AddRange(new[] { Float(0), Float(0.25f), Float(0), Str("mbgstars.sod", 100), Str("tech1.tt", 128), Str("federation", 128) });
        tokens.Add(Int(CountOverride ?? Objects.Count));
        var body = tokens.SelectMany(t => t).Concat(Objects.SelectMany(ObjectTokens)).ToList();
        for (var i = 0; i < 9; i++) // one team record each
            body.AddRange(new[] { Float(0), Float(3500), Int(0), Float(0), Float(4000), Int(i), Int(i) }.SelectMany(t => t));
        body.AddRange(new[] { Str("EmptyMission", 40), Ptr(7), Str("", 64), Str("", 64), Token(0, new byte[64]), Int(0), Int(Paths.Count) }.SelectMany(t => t));
        foreach (var (name, points) in Paths)
        {
            body.AddRange(new[] { Token(0, new byte[4]), Int(name.Length) }.SelectMany(t => t));
            if (name.Length > 0) body.AddRange(Token(2, Encoding.Latin1.GetBytes(name)));
            body.AddRange(Int(points.Length));
            body.AddRange(Token(9, Floats(points.SelectMany(p => new[] { p.X, p.Y, p.Z }).ToArray())));
            body.AddRange(Token(0, new byte[4]));
            body.AddRange(Float(0.01f));
        }
        if (Extra != null) foreach (var t in Extra) body.AddRange(t);
        return Encoding.Latin1.GetBytes(text).Concat(body).ToArray();
    }
}
