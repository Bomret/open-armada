using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using OpenArmada.Formats;

namespace OpenArmada.Assets;

// How a texture material blends: opaque, alpha-tested at 128/255 (cutout), blended by alpha, or added.
public enum BlendMode { Opaque, Cutout, Alpha, Add }

// A texture material's draw state. Opacity is 1 - transparency; Nearest is point texture filtering.
public sealed record BlendState(BlendMode Mode, float Opacity, bool Wireframe = false, bool Nearest = false);

// What a model's mesh draws with: its texture (the entry's own for versions before 1.7), the blend
// state, or the reason the blend is not known. Exactly one of Blend and Problem is set.
public sealed record MeshLook(string Texture, BlendState? Blend, string? Problem);

public enum LightingModel { Constant, Lambert, Phong }

// A lighting material as the surface is shaded. Team marks a material whose name starts "team".
public sealed record Surface(LightingModel Model, Vector3 Ambient, Vector3 Diffuse, Vector3 Specular, float Power, bool Team)
{
    // The game replaces a team material's ambient with black and its diffuse and specular with the team
    // color; power and model stay (Armada.exe FUN_005a5b00, see reports of the asset-layer scope).
    public Surface WithTeam(Vector3 color) => this with { Ambient = Vector3.Zero, Diffuse = color, Specular = color };
}

// A texture flipbook: the node's texture coordinates are offset by one cell per frame.
public sealed record TextureAnimation(int Columns, int Rows, int Count, double FrameSeconds, double Offset)
{
    public Vector2 UvOffset(double time)
    {
        var frame = FrameSeconds > 0 ? (int)Math.Floor((time + Offset) / FrameSeconds) : 0;
        frame = ((frame % Count) + Count) % Count;
        return new Vector2((float)(frame % Columns) / Columns, (float)(frame / Columns) / Rows);
    }
}

// The rules a model is drawn by, apart from drawing: which blend a texture material means, how a
// lighting material shades, how an animation reference moves a texture, and what in a model cannot be
// drawn. Godot-free, so the tests cover them.
public static class ModelRules
{
    // Direct3D blend values of old texture material entries.
    const byte Zero = 1, One = 2, SrcAlpha = 5, InvSrcAlpha = 6;

    // The game's named render types (Armada.exe FUN_00589070). `cloaked` has two passes; the first is kept.
    static readonly Dictionary<string, BlendState> Named = new(StringComparer.OrdinalIgnoreCase)
    {
        ["default"] = new(BlendMode.Opaque, 1f),
        ["default_persp"] = new(BlendMode.Opaque, 1f),
        ["nofilter"] = new(BlendMode.Opaque, 1f, Nearest: true),
        ["additive"] = new(BlendMode.Add, 1f),
        ["add_nomipf"] = new(BlendMode.Add, 1f),
        ["translucent"] = new(BlendMode.Alpha, 0.5f),
        ["fog"] = new(BlendMode.Alpha, 0.7f),
        ["alphathreshold"] = new(BlendMode.Cutout, 1f),
        ["wireframe"] = new(BlendMode.Opaque, 1f, Wireframe: true),
        ["interface"] = new(BlendMode.Cutout, 1f, Nearest: true),
        ["cloaked"] = new(BlendMode.Alpha, 0.5f),
        ["wormhole"] = new(BlendMode.Alpha, 1f),
        ["alpha"] = new(BlendMode.Alpha, 1f),
        ["alpha_nof"] = new(BlendMode.Alpha, 1f, Nearest: true),
    };

    // The texture and blend of a mesh. Versions from 1.7 name the render type; older files name an entry of
    // their own texture material table. The game itself rejects files below 1.4 (Armada.exe FUN_0059b450);
    // the engine still draws the 1.2 models of the install, blended by their entries.
    public static MeshLook Look(Sod sod, Sod.Mesh mesh)
    {
        if (sod.Version >= 1.65f)
        {
            if (mesh.TextureMaterial.Length == 0) return new(mesh.Texture, Named["default"], null);
            return Named.TryGetValue(mesh.TextureMaterial, out var named)
                ? new(mesh.Texture, named, null)
                : new(mesh.Texture, null, $"texture material '{mesh.TextureMaterial}' is not a render type the engine knows");
        }
        if (mesh.Texture.Length == 0) return new(mesh.Texture, Named["default"], null);
        var entry = sod.TextureMaterials.FirstOrDefault(t => t.Name.Equals(mesh.Texture, StringComparison.OrdinalIgnoreCase));
        if (entry == null) return new(mesh.Texture, null, $"texture material '{mesh.Texture}' is not in the model's table");

        var opacity = Math.Clamp(1f - entry.Transparency, 0f, 1f);
        // The game turns alpha test on for every entry whose blend is not one/one (FUN_0059b230).
        BlendMode? mode = (entry.SrcBlend, entry.DstBlend) switch
        {
            (One, One) => BlendMode.Add,
            (SrcAlpha, InvSrcAlpha) => BlendMode.Alpha,
            (One, Zero) => BlendMode.Cutout,
            _ => null,
        };
        return mode is { } m
            ? new(entry.Texture, new BlendState(m, opacity, Nearest: entry.Filter == 1), null)
            : new(entry.Texture, null, $"texture material '{entry.Name}' has blend {entry.SrcBlend}/{entry.DstBlend}, which the engine does not know");
    }

    public static bool IsTeam(string materialName) => materialName.StartsWith("team", StringComparison.OrdinalIgnoreCase);

    // Null when the material's model is not constant, Lambert or Phong.
    public static Surface? Lighting(Sod.LightingMaterial m) =>
        m.Model > (byte)LightingModel.Phong
            ? null
            : new Surface((LightingModel)m.Model, m.Ambient, m.Diffuse, m.Specular, m.SpecularPower, IsTeam(m.Name));

    // A flipbook of the sprite tables as a texture animation shifted by a reference's time offset.
    // Null, with the reason, for the animations that are not UV flipbooks.
    public static TextureAnimation? Animation(SpriteAnimation a, float offset, out string? problem)
    {
        problem = null;
        if (a.Field != "offset") problem = $"animation '{a.Name}' changes '{a.Field}', which the engine does not draw on models";
        else if (a.Auto == null) problem = $"animation '{a.Name}' has explicit keyframes, which the engine does not draw on models";
        else if (a.Count < 1) problem = $"animation '{a.Name}' has no frames";
        if (problem != null) return null;
        var (columns, rows) = SpriteTable.Grid(a);
        return new TextureAnimation(columns, rows, a.Count, a.FrameSeconds, offset);
    }

    // The animation an animation reference names: a table's animation, or the animation of a sprite of
    // that name ("borgate"). Null when no sprite table has either.
    public static SpriteAnimation? FindAnimation(GameFileSystem files, string name)
    {
        var kind = new SpriteTableKind();
        foreach (var table in kind.Enumerate(files))
        {
            SpriteTable t;
            try { t = (SpriteTable)kind.Load(files, table); }
            catch (System.IO.InvalidDataException) { continue; }
            if (t.Animations.TryGetValue(name, out var found)) return found;
            var sprite = t.Sprites.FirstOrDefault(s => s.Anim != null && s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (sprite != null && t.Animations.TryGetValue(sprite.Anim!, out found)) return found;
        }
        return null;
    }

    // Everything in the model the engine cannot draw as the data says, one line each: what the reader
    // found, texture materials and lighting models it does not know, and animation references it cannot run.
    public static IReadOnlyList<string> Check(GameFileSystem files, Sod sod)
    {
        var problems = new List<string>(sod.Problems);
        foreach (var n in sod.Nodes.Where(n => n.Mesh != null))
        {
            var look = Look(sod, n.Mesh!);
            if (look.Problem != null) problems.Add($"node '{n.Name}': {look.Problem}");
            foreach (var g in n.Mesh!.Groups.Select(g => g.Material).Distinct())
            {
                var m = sod.LightingMaterials.FirstOrDefault(l => l.Name == g);
                if (m != null && Lighting(m) == null) problems.Add($"node '{n.Name}': lighting material '{g}' has model {m.Model}, which the engine does not know");
            }
        }
        foreach (var r in sod.References)
        {
            var anim = r.Type == AnimationReferenceType.Texture ? FindAnimation(files, r.Animation) : null;
            if (r.Type != AnimationReferenceType.Texture) problems.Add($"animation reference to '{r.Node}' has type {r.Type}, which the engine does not know");
            else if (anim == null) problems.Add($"animation '{r.Animation}' of node '{r.Node}' is in no sprite table");
            else if (Animation(anim, r.Offset, out var why) == null) problems.Add($"node '{r.Node}': {why}");
        }
        return problems;
    }
}

// The one animation reference type the installed models use: a texture flipbook.
public static class AnimationReferenceType
{
    public const byte Texture = 4;
}
