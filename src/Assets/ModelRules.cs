using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using OpenArmada.Formats;

namespace OpenArmada.Assets;

/// <summary>How a texture material blends: opaque, alpha-tested at 128/255 (cutout), blended by alpha, or added.</summary>
public enum BlendMode
{
    /// <summary>Drawn opaque.</summary>
    Opaque,

    /// <summary>Alpha-tested at 128/255.</summary>
    Cutout,

    /// <summary>Blended by alpha.</summary>
    Alpha,

    /// <summary>Added to what is behind it.</summary>
    Add,
}

/// <summary>
/// A texture material's draw state. Opacity is 1 - transparency; Nearest is point texture filtering.
/// AlphaTest drops texels below 128/255 before an Alpha blend (Cutout always does).
/// </summary>
public sealed record BlendState
{
    /// <summary>How the material blends.</summary>
    public required BlendMode Mode { get; init; }

    /// <summary>1 - transparency, clamped to 0..1 for an old texture material entry.</summary>
    public required float Opacity { get; init; }

    /// <summary>True when the material draws as wireframe (the <c>wireframe</c> render type).</summary>
    public bool Wireframe { get; init; } = false;

    /// <summary>True for point texture filtering.</summary>
    public bool Nearest { get; init; } = false;

    /// <summary>True when texels below 128/255 are dropped before an <see cref="BlendMode.Alpha"/> blend.</summary>
    public bool AlphaTest { get; init; } = false;
}

/// <summary>
/// What a model's mesh draws with: its texture (the entry's own for versions before 1.7), the blend
/// state, or the reason the blend is not known. Exactly one of Blend and Problem is set.
/// </summary>
public sealed record MeshLook
{
    /// <summary>
    /// The texture the mesh draws with: the mesh's own, or for versions before 1.7 the texture of its texture
    /// material entry.
    /// </summary>
    public required string Texture { get; init; }

    /// <summary>The blend state; null when <see cref="Problem"/> is set.</summary>
    public required BlendState? Blend { get; init; }

    /// <summary>The reason the blend is not known; null when <see cref="Blend"/> is set.</summary>
    public required string? Problem { get; init; }
}

/// <summary>How a lighting material shades; the values match the SOD model byte (0, 1, 2).</summary>
public enum LightingModel
{
    /// <summary>Constant shading.</summary>
    Constant,

    /// <summary>Lambert shading.</summary>
    Lambert,

    /// <summary>Phong shading.</summary>
    Phong,
}

/// <summary>A lighting material as the surface is shaded. Team marks a material whose name starts "team".</summary>
public sealed record Surface
{
    /// <summary>How the material shades.</summary>
    public required LightingModel Model { get; init; }

    /// <summary>The material's ambient color.</summary>
    public required Vector3 Ambient { get; init; }

    /// <summary>The material's diffuse color.</summary>
    public required Vector3 Diffuse { get; init; }

    /// <summary>The material's specular color.</summary>
    public required Vector3 Specular { get; init; }

    /// <summary>The material's specular power.</summary>
    public required float Power { get; init; }

    /// <summary>True when the material's name starts with "team" (any case); <see cref="WithTeam"/> applies.</summary>
    public required bool Team { get; init; }

    /// <summary>
    /// The game replaces a team material's ambient with black and its diffuse and specular with the team
    /// color; power and model stay (Armada.exe FUN_005a5b00, see reports of the asset-layer scope).
    /// </summary>
    public Surface WithTeam(Vector3 color) => this with { Ambient = Vector3.Zero, Diffuse = color, Specular = color };
}

/// <summary>A texture flipbook: the node's texture coordinates are offset by one cell per frame.</summary>
public sealed record TextureAnimation
{
    /// <summary>The number of cell columns in the texture.</summary>
    public required int Columns { get; init; }

    /// <summary>The number of cell rows in the texture.</summary>
    public required int Rows { get; init; }

    /// <summary>The number of frames; <see cref="UvOffset"/> needs at least 1, which <see cref="ModelRules.Animation"/> ensures.</summary>
    public required int Count { get; init; }

    /// <summary>The seconds each frame shows; 0 or less holds the first frame.</summary>
    public required double FrameSeconds { get; init; }

    /// <summary>The animation reference's time offset, added to the time before the frame is picked.</summary>
    public required double Offset { get; init; }

    /// <summary>
    /// The texture coordinate offset of the frame at <c>time</c> plus <see cref="Offset"/>, in seconds: frames run
    /// row by row and loop after <see cref="Count"/>, also for a negative time.
    /// </summary>
    public Vector2 UvOffset(double time)
    {
        var frame = FrameSeconds > 0 ? (int)Math.Floor((time + Offset) / FrameSeconds) : 0;
        frame = ((frame % Count) + Count) % Count;

        return new Vector2((float)(frame % Columns) / Columns, (float)(frame / Columns) / Rows);
    }
}

/// <summary>
/// The rules a model is drawn by, apart from drawing: which blend a texture material means, how a
/// lighting material shades, how an animation reference moves a texture, and what in a model cannot be
/// drawn. Godot-free, so the tests cover them.
/// </summary>
public static class ModelRules
{
    // Direct3D blend values of old texture material entries.
    const byte Zero = 1,
        One = 2,
        SrcAlpha = 5,
        InvSrcAlpha = 6;

    /// <summary>The animation reference type the installed models use: a texture flipbook.</summary>
    public const byte AnimationReferenceType = 4;

    // The game's named render types (Armada.exe FUN_00589070) that the reference install's models and
    // sprite tables use (default, additive, alphathreshold, wormhole, interface, add_nomipf), plus the
    // spec's translucent, alpha, and wireframe. The game also names default_persp, nofilter, fog, cloaked,
    // and alpha_nof; no installed file uses them, so one that appears is reported as unknown.
    static readonly Dictionary<string, BlendState> Named = new(StringComparer.OrdinalIgnoreCase)
    {
        ["default"] = new() { Mode = BlendMode.Opaque, Opacity = 1f },
        ["additive"] = new() { Mode = BlendMode.Add, Opacity = 1f },
        ["add_nomipf"] = new() { Mode = BlendMode.Add, Opacity = 1f },
        ["translucent"] = new() { Mode = BlendMode.Alpha, Opacity = 0.5f },
        ["alphathreshold"] = new() { Mode = BlendMode.Cutout, Opacity = 1f },
        ["wireframe"] = new()
        {
            Mode = BlendMode.Opaque,
            Opacity = 1f,
            Wireframe = true,
        },
        ["interface"] = new()
        {
            Mode = BlendMode.Cutout,
            Opacity = 1f,
            Nearest = true,
        },
        ["wormhole"] = new() { Mode = BlendMode.Alpha, Opacity = 1f },
        ["alpha"] = new() { Mode = BlendMode.Alpha, Opacity = 1f },
    };

    /// <summary>Whether a sprite's or mesh's texture material names a render type the engine draws.</summary>
    public static bool IsRenderType(string name) => Named.ContainsKey(name);

    /// <summary>The draw state of a named render type, whatever its case; null when the engine does not know the name.</summary>
    public static BlendState? Blend(string name) => Named.GetValueOrDefault(name);

    /// <summary>
    /// The texture and blend of a mesh. Versions from 1.7 name the render type; older files name an entry of
    /// their own texture material table. The game itself rejects files below 1.4 (Armada.exe FUN_0059b450);
    /// the engine still draws the 1.2 models of the install, blended by their entries.
    /// </summary>
    public static MeshLook Look(Sod sod, Sod.Mesh mesh)
    {
        if (sod.Version >= 1.65f)
        {
            if (mesh.TextureMaterial.Length == 0)
                return new()
                {
                    Texture = mesh.Texture,
                    Blend = Named["default"],
                    Problem = null,
                };

            return Named.TryGetValue(mesh.TextureMaterial, out var named)
                ? new()
                {
                    Texture = mesh.Texture,
                    Blend = named,
                    Problem = null,
                }
                : new()
                {
                    Texture = mesh.Texture,
                    Blend = null,
                    Problem = $"texture material '{mesh.TextureMaterial}' is not a render type the engine knows",
                };
        }

        if (mesh.Texture.Length == 0)
            return new()
            {
                Texture = mesh.Texture,
                Blend = Named["default"],
                Problem = null,
            };
        var entry = sod.TextureMaterials.FirstOrDefault(t =>
            t.Name.Equals(mesh.Texture, StringComparison.OrdinalIgnoreCase)
        );
        if (entry == null)
            return new()
            {
                Texture = mesh.Texture,
                Blend = null,
                Problem = $"texture material '{mesh.Texture}' is not in the model's table",
            };

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
            ? new()
            {
                Texture = entry.Texture,
                Blend = new BlendState
                {
                    Mode = m,
                    Opacity = opacity,
                    Nearest = entry.Filter == 1,
                    AlphaTest = m == BlendMode.Alpha,
                },
                Problem = null,
            }
            : new()
            {
                Texture = entry.Texture,
                Blend = null,
                Problem =
                    $"texture material '{entry.Name}' has blend {entry.SrcBlend}/{entry.DstBlend}, which the engine does not know",
            };
    }

    /// <summary>True when a lighting material's name starts with "team" (any case).</summary>
    public static bool IsTeam(string materialName) =>
        materialName.StartsWith("team", StringComparison.OrdinalIgnoreCase);

    /// <summary>Null when the material's model is not constant, Lambert or Phong.</summary>
    public static Surface? Lighting(Sod.LightingMaterial m) =>
        m.Model > (byte)LightingModel.Phong
            ? null
            : new Surface
            {
                Model = (LightingModel)m.Model,
                Ambient = m.Ambient,
                Diffuse = m.Diffuse,
                Specular = m.Specular,
                Power = m.SpecularPower,
                Team = IsTeam(m.Name),
            };

    /// <summary>
    /// A flipbook of the sprite tables as a texture animation shifted by a reference's time offset.
    /// Null, with the reason, for the animations that are not UV flipbooks.
    /// </summary>
    public static TextureAnimation? Animation(SpriteAnimation a, float offset, out string? problem)
    {
        problem = null;
        if (a.Field != "offset")
        {
            problem = $"animation '{a.Name}' changes '{a.Field}', which the engine does not draw on models";
        }
        else if (a.Auto == null)
        {
            problem = $"animation '{a.Name}' has explicit keyframes, which the engine does not draw on models";
        }
        else if (a.Count < 1)
        {
            problem = $"animation '{a.Name}' has no frames";
        }

        if (problem != null)
            return null;
        var (columns, rows) = SpriteTable.Grid(a);

        return new TextureAnimation
        {
            Columns = columns,
            Rows = rows,
            Count = a.Count,
            FrameSeconds = a.FrameSeconds,
            Offset = offset,
        };
    }

    /// <summary>
    /// The animation an animation reference names: a table's animation, or the animation of a sprite of
    /// that name ("borgate"). Null when no sprite table has either.
    /// </summary>
    public static SpriteAnimation? FindAnimation(GameFileSystem files, string name)
    {
        var tables = new SpriteTableKind().Tables(files).ToList();
        foreach (var t in tables)
        {
            if (t.Animations.TryGetValue(name, out var found))
                return found;
        }

        foreach (var t in tables)
        {
            var sprite = t.Sprites.FirstOrDefault(s =>
                s.Anim != null && s.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
            );
            if (sprite == null)
                continue;
            foreach (var other in tables)
            {
                if (other.Animations.TryGetValue(sprite.Anim!, out var played))
                    return played;
            }
        }

        return null;
    }

    // Lighting materials are global by name in the game: the first SOD loaded that defines a name supplies
    // it to every model (Armada.exe FUN_0059bbe0; REA report of the asset-layer scope). The table of the
    // models the game loads, built once per install in enumeration order: each bare name, as its lookup
    // resolves it (the override copy first). A copy listed with its folder (a software-renderer copy or a
    // stock model an override replaces) is one the game never loads and supplies no material.
    static readonly ConditionalWeakTable<GameFileSystem, Dictionary<string, Sod.LightingMaterial>> Global = new();

    static Dictionary<string, Sod.LightingMaterial> InstallLightingMaterials(GameFileSystem files)
    {
        var table = new Dictionary<string, Sod.LightingMaterial>();
        var kind = new ModelKind();
        foreach (var model in kind.Enumerate(files).Where(n => !n.Contains('/')))
        {
            try
            {
                foreach (var m in ((Sod)kind.Load(files, model)).LightingMaterials)
                {
                    table.TryAdd(m.Name, m);
                }
            }
            // A model that cannot be read adds none; its own check reports it.
            catch (Exception e)
                when (e
                        is System.IO.IOException
                            or UnauthorizedAccessException
                            or UnsupportedFormatException
                            or System.IO.InvalidDataException
                ) { }
        }

        return table;
    }

    /// <summary>
    /// The lighting material a group names: the model's own, else the one another model the game loads
    /// defines. Null when no model defines it, and for an empty name (a group naming none; a material
    /// named "" is never looked up).
    /// </summary>
    public static Sod.LightingMaterial? FindLightingMaterial(GameFileSystem files, Sod sod, string name) =>
        name.Length == 0
            ? null
            : sod.LightingMaterials.FirstOrDefault(l => l.Name == name)
                ?? (Global.GetValue(files, InstallLightingMaterials).TryGetValue(name, out var found) ? found : null);

    /// <summary>
    /// What a model cannot be drawn as the data says (Problems, one line each: what the reader found,
    /// texture materials, lighting materials and models it does not know, animation references it cannot
    /// run), and the texture animations it does run (Runs: a mesh node and its flipbook, one per node of
    /// the referenced name). MissingAnimations holds each animation reference no sprite table answers, with
    /// the line it adds to Problems (null when the reference names no mesh node, so it adds none);
    /// ModelKind lists such an animation as an unresolved reference and leaves the line out of its check.
    /// </summary>
    public sealed record Analysis
    {
        /// <summary>What the model cannot be drawn as the data says, one line each.</summary>
        public required IReadOnlyList<string> Problems { get; init; }

        /// <summary>The texture animations the model runs: a mesh node and its flipbook, one per node.</summary>
        public required IReadOnlyList<(Sod.Node Node, TextureAnimation Flipbook)> Runs { get; init; }

        /// <summary>
        /// Each animation reference no sprite table answers, with the line it adds to <see cref="Problems"/>, or
        /// null when the reference names no mesh node.
        /// </summary>
        public required IReadOnlyList<(string Animation, string? Problem)> MissingAnimations { get; init; }
    }

    /// <summary>
    /// Analyzes what the model cannot draw and which texture animations it runs; starts with the reader's own
    /// <see cref="Sod.Problems"/>. Side effects: a lighting material the model lacks loads every model the game
    /// loads, and an animation reference loads the install's sprite tables; both are cached per install.
    /// </summary>
    public static Analysis Analyze(GameFileSystem files, Sod sod)
    {
        var problems = new List<string>(sod.Problems);
        var runs = new List<(Sod.Node, TextureAnimation)>();
        var missing = new List<(string, string?)>();
        var unnamed = 0;
        foreach (var n in sod.Nodes.Where(n => n.Mesh != null))
        {
            var look = Look(sod, n.Mesh!);
            if (look.Problem != null)
            {
                problems.Add($"node '{n.Name}': {look.Problem}");
            }

            unnamed += n.Mesh!.Groups.Count(g => g.Material.Length == 0);
            foreach (var g in n.Mesh!.Groups.Select(g => g.Material).Where(g => g.Length > 0).Distinct())
            {
                var m = FindLightingMaterial(files, sod, g);
                if (m == null)
                {
                    problems.Add(
                        $"node '{n.Name}': lighting material '{g}' is defined in no model; drawn Lambert white"
                    );
                }
                else if (Lighting(m) == null)
                {
                    problems.Add(
                        $"node '{n.Name}': lighting material '{g}' has model byte {m.StoredModel}, which the engine does not know"
                    );
                }
            }
        }

        // The game gives a group without a lighting material its default material (FUN_0058e850); its
        // values are not decoded, so the engine stands in with Lambert white.
        if (unnamed > 0)
        {
            problems.Add(
                $"{unnamed} groups name no lighting material; drawn Lambert white as the engine's stand-in for the game's default material"
            );
        }

        foreach (var r in sod.References)
        {
            if (r.Type != AnimationReferenceType)
            {
                problems.Add($"animation reference to '{r.Node}' has type {r.Type}, which the engine does not know");

                continue;
            }

            var targets = sod.Nodes.Where(n => n.Mesh != null && n.Name == r.Node).ToList();
            var anim = FindAnimation(files, r.Animation);
            TextureAnimation? flipbook = null;
            string? why = null;
            if (anim != null)
            {
                flipbook = Animation(anim, r.Offset, out why);
            }

            string? line = null;
            if (targets.Count == 0)
            {
                problems.Add(
                    $"animation '{r.Animation}' names the node '{r.Node}', which is no mesh node of the model"
                );
            }
            else if (anim == null)
            {
                problems.Add(line = $"animation '{r.Animation}' of node '{r.Node}' is in no sprite table");
            }
            else if (flipbook == null)
            {
                problems.Add($"node '{r.Node}': {why}");
            }
            else
            {
                runs.AddRange(targets.Select(t => (t, flipbook)));
            }

            if (anim == null)
            {
                missing.Add((r.Animation, line));
            }
        }

        return new Analysis
        {
            Problems = problems,
            Runs = runs,
            MissingAnimations = missing,
        };
    }

    /// <summary>The <see cref="Analysis.Problems"/> of <see cref="Analyze"/>.</summary>
    public static IReadOnlyList<string> Check(GameFileSystem files, Sod sod) => Analyze(files, sod).Problems;
}
