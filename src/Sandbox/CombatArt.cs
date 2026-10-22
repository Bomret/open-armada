using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using OpenArmada.Assets;
using OpenArmada.Diagnostics;
using OpenArmada.Formats;

namespace OpenArmada.Sandbox;

/// <summary>
/// A shot's or a sprite node's sprite: its row, whose Material is its blend, and its frames in play order, each for
/// FrameSeconds.
/// </summary>
public sealed record SpriteArt
{
    /// <summary>The sprite table row; its Material is the blend.</summary>
    public required Sprite Sprite { get; init; }

    /// <summary>The frames in play order.</summary>
    public required IReadOnlyList<SpriteFrame> Frames { get; init; }

    /// <summary>
    /// Seconds each frame shows; 0 for a single frame, a sprite without an animation, or an animation of explicit
    /// keyframes (only a flipbook animates).
    /// </summary>
    public required double FrameSeconds { get; init; }

    /// <summary>
    /// Every sprite of the install's tables by name. Each table is read with the animations of all the others, as
    /// the sprite table kind does; the tables go in name order (ignoring case), and the first row of a name wins.
    /// A table that cannot be read (an I/O failure, a bad table, or a compressed archive entry) adds none.
    /// </summary>
    public static Dictionary<string, (SpriteTable Table, Sprite Sprite)> Rows(GameFileSystem files)
    {
        var rows = new Dictionary<string, (SpriteTable, Sprite)>(StringComparer.OrdinalIgnoreCase);
        foreach (var table in new SpriteTableKind().Loaded(files))
        {
            foreach (var sprite in table.Sprites)
            {
                rows.TryAdd(sprite.Name, (table, sprite));
            }
        }

        return rows;
    }

    /// <summary>The sprite's art from its table: its frames, and the seconds each shows.</summary>
    public static SpriteArt Of(SpriteTable table, Sprite sprite)
    {
        var frames = table.Frames(sprite);
        // stand-in: only a flipbook (@auto) animates; an animation of explicit keyframes shows its first frame.
        var seconds =
            frames.Count > 1 && sprite.Anim != null && table.Animations.TryGetValue(sprite.Anim, out var a)
                ? a.FrameSeconds
                : 0;

        return new SpriteArt
        {
            Sprite = sprite,
            Frames = frames,
            FrameSeconds = seconds,
        };
    }

    /// <summary>
    /// The frame shown <paramref name="seconds"/> after the sprite appeared: the flipbook loops; a still sprite shows
    /// its one frame.
    /// </summary>
    public SpriteFrame FrameAt(double seconds) =>
        FrameSeconds > 0 ? Frames[(int)(Math.Max(seconds, 0) / FrameSeconds) % Frames.Count] : Frames[0];
}

/// <summary>A unit's explosion: the sound and the model it shows for Length seconds. Each is null when the install lacks it.</summary>
public sealed record ExplosionArt
{
    /// <summary>The explosion definition's <c>explSound</c>; null when it names none or the install lacks it.</summary>
    public required string? Sound { get; init; }

    /// <summary>The explosion definition's <c>dbName</c>, without <c>.sod</c>; null when it names none or the install lacks it.</summary>
    public required string? Model { get; init; }

    /// <summary>The explosion definition's <c>length</c> in seconds; 0 when it names none, so the explosion ends at once.</summary>
    public required float Length { get; init; }
}

/// <summary>
/// What the fight looks and sounds like, resolved once from the install for the units of a map. Every lookup
/// that fails is a gap, reported once per cause with its names and the number of units it touches (INV-0002).
/// </summary>
public sealed class CombatArt
{
    const string NoSprite = "ordnance names no sprite, its shots are not drawn";
    const string MissingSprite = "sprite missing from the sprite tables, its shots are not drawn";
    const string NoSound = "weapon names no fire sound, its shots are silent";
    const string MissingSound = "sound missing from the install, it is not played";
    const string NoFireball = "unit names no fireball, its destruction shows no explosion";
    const string MissingExplosion = "explosion definition missing, the destruction shows no explosion";
    const string BadExplosion = "explosion definition cannot be read, the destruction shows no explosion";
    const string NoExplosionSound = "explosion names no sound, the destruction is silent";
    const string MissingExplosionSound = "explosion sound missing from the install, it is not played";
    const string NoExplosionModel = "explosion names no model that the install holds, it shows no model";
    const string MissingHardpoint = "hardpoint missing from the model, its shots leave from the unit's center";

    const string MissingShieldGeometry = "shield geometry missing from the install, its shield hit effect is not shown";
    const string MissingShieldDownModel = "shield-down model missing from the install, the overlay is not shown";
    const string MissingHullSprite = "hull hit sprite missing from the sprite tables, a beam on a hull shows none";

    // The shield-down overlay's geometry (0x467b70).
    const string ShieldDownModelName = "xshlddie";

    readonly HashSet<string> _shieldGeometries = new(StringComparer.OrdinalIgnoreCase);
    readonly GameFileSystem _files;
    readonly Gaps _gaps = new();
    readonly HashSet<string> _unreadable = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, SpriteArt> _sprites = new(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> _fireSounds = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<PlacedObject, ExplosionArt> _explosions = new(ReferenceEqualityComparer.Instance);

    // Per model: each node name, matched without regard to case, to the name the built node carries.
    readonly Dictionary<string, Dictionary<string, string>> _nodesOfModel = new(StringComparer.OrdinalIgnoreCase);
    Dictionary<string, (SpriteTable Table, Sprite Sprite)>? _rows;

    // Per model: each node name, matched without regard to case, to the node's offset in the model.
    readonly Dictionary<string, Dictionary<string, Vector3>> _offsetsOfModel = new(StringComparer.OrdinalIgnoreCase);

    CombatArt(GameFileSystem files) => _files = files;

    /// <summary>
    /// Resolves the shot sprites, fire sounds, hardpoints, and explosions of the units from the install. Every failed
    /// lookup becomes a gap in <see cref="Reports"/>, logged once per cause as a WARN (an unreadable explosion definition: one ERROR). Reads the sprite tables once, on the first sprite, and each
    /// model once.
    /// </summary>
    public static CombatArt Build(GameFileSystem files, IEnumerable<(PlacedObject Unit, CombatProfile Profile)> units)
    {
        var art = new CombatArt(files);
        foreach (var (unit, profile) in units)
        {
            var nodes = art.NodesOf(unit.Model);
            foreach (var weapon in profile.Weapons)
            {
                art.ResolveSprite(weapon, unit);
                if (!weapon.Ordnance.IgnoreShield)
                {
                    art.ResolveShieldGeometry(weapon.Ordnance.ShieldGeometryName, unit);
                }

                if (weapon.Ordnance.ClassLabel == "phaser")
                {
                    art.ResolveHullSprite(unit);
                }

                if (art.ResolveSound(weapon.FireSound, weapon.Name, NoSound, MissingSound, unit))
                {
                    art._fireSounds.Add(weapon.FireSound);
                }

                foreach (var hardpoint in weapon.Hardpoints)
                {
                    if (!nodes.ContainsKey(hardpoint))
                    {
                        art._gaps.Add(MissingHardpoint, hardpoint, unit);
                    }
                }
            }

            if (profile.Systems.Any(s => s.Name == SystemNames.ShieldGenerator))
            {
                art.ResolveShieldDownModel(unit);
            }

            if (art.ResolveExplosion(profile.Fireball, unit) is { } explosion)
            {
                art._explosions[unit] = explosion;
            }
        }

        // Each cause is one WARN entry with the text of its screen line (the sandbox and the view print the same);
        // an unreadable explosion definition is an ERROR entry of its own instead (see Unreadable).
        foreach (var report in art._gaps.Entries.Where(r => r.Cause != BadExplosion))
        {
            Log.Warn(Area.Combat, report.Text);
        }

        return art;
    }

    /// <summary>Every sprite, every sound (fire and explosion), and every explosion model the fight can show, each once.</summary>
    public IEnumerable<SpriteArt> Sprites => _sprites.Values;

    /// <summary>Every fire and explosion sound the install holds, each once (ignoring case).</summary>
    public IEnumerable<string> Sounds =>
        _fireSounds
            .Concat(_explosions.Values.Select(e => e.Sound).OfType<string>())
            .Distinct(StringComparer.OrdinalIgnoreCase);

    /// <summary>Every explosion model the install holds, without <c>.sod</c>, each once (ignoring case).</summary>
    public IEnumerable<string> ExplosionModels =>
        _explosions.Values.Select(e => e.Model).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase);

    /// <summary>The shield hit effect geometries the install holds, without <c>.sod</c>, each once (ignoring case).</summary>
    public IEnumerable<string> ShieldGeometries => _shieldGeometries;

    /// <summary>
    /// The shield-down overlay model (<c>xshlddie</c>, without <c>.sod</c>) when a unit has a shield generator and
    /// the install holds it; else null (a gap when a unit needs it).
    /// </summary>
    public string? ShieldDownModel { get; private set; }

    /// <summary>The shot sprite of an ordnance's <c>Sprite</c> name; null when it is missing (a gap).</summary>
    public SpriteArt? Sprite(string name) => _sprites.GetValueOrDefault(name);

    /// <summary>The explosion of a unit's fireball; null when its definition is missing or unreadable (a gap).</summary>
    public ExplosionArt? Explosion(PlacedObject unit) => _explosions.GetValueOrDefault(unit);

    /// <summary>
    /// The name the built model node carries for the SOD node a hardpoint name matches (the match ignores case,
    /// a node lookup does not; Godot sanitizes the name, see NodeName); null when the model holds none.
    /// </summary>
    public string? Hardpoint(PlacedObject unit, string name) => NodesOf(unit.Model).GetValueOrDefault(name);

    /// <summary>
    /// Where the hardpoint is in the world when the unit stands in <paramref name="pose"/> (take it from the same
    /// tick as the unit's position). A hardpoint the model lacks, or a model that cannot be read, gives the unit's
    /// center; that is the gap <see cref="Build"/> reports.
    /// </summary>
    public Vector3 HardpointPosition(PlacedObject unit, string name, ShipPose pose)
    {
        // stand-in: a hardpoint the model lacks, or a model that cannot be read, sits at the unit's center.
        var offset = OffsetsOf(unit.Model).GetValueOrDefault(name);

        return HardpointGeometry.WorldPosition(unit, pose, offset);
    }

    /// <summary>A failure of the view (a texture or a sound that cannot be decoded) joins the gaps, once per cause.</summary>
    public void Report(string cause, string name) => _gaps.Add(cause, name);

    /// <summary>
    /// A failure of the view: joins the gaps like <see cref="Report"/> (the grouped line is display only) and is one
    /// ERROR entry of its own, <c>&lt;kind&gt; &lt;name&gt;: &lt;reason&gt;</c>.
    /// </summary>
    public void Fail(string kind, string name, string reason, string cause)
    {
        Report(cause, name);
        Log.Error(Area.Combat, $"{kind} {name}: {reason}");
    }

    /// <summary>The gaps found by <see cref="Build"/> and <see cref="Report"/>, once per cause.</summary>
    public IReadOnlyList<ReportEntry> Reports => _gaps.Entries;

    // The nodes of a model by name; none when the model cannot be read (the sandbox reports that).
    Dictionary<string, string> NodesOf(string model)
    {
        Load(model);

        return _nodesOfModel[model];
    }

    // The offsets of a model's nodes by name; none when the model cannot be read.
    Dictionary<string, Vector3> OffsetsOf(string model)
    {
        Load(model);

        return _offsetsOfModel[model];
    }

    // Reads a model once, filling both per-model caches.
    void Load(string model)
    {
        if (_nodesOfModel.ContainsKey(model))
            return;

        var nodes = _nodesOfModel[model] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        _offsetsOfModel[model] = new Dictionary<string, Vector3>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var sod = Sod.Read(_files.ReadAllBytes(model + ".sod"));
            foreach (var node in sod.Nodes)
                nodes.TryAdd(node.Name, NodeName.Of(node.Name));
            _offsetsOfModel[model] = HardpointGeometry.Offsets(sod);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnsupportedFormatException) { }
    }

    void ResolveSprite(WeaponProfile weapon, PlacedObject unit)
    {
        var name = weapon.Ordnance.Sprite;
        if (name.Length == 0)
        {
            _gaps.Add(NoSprite, weapon.Name, unit);

            return;
        }

        _rows ??= SpriteArt.Rows(_files);
        if (!_rows.TryGetValue(name, out var row))
        {
            _gaps.Add(MissingSprite, name, unit);

            return;
        }

        _sprites[name] = SpriteArt.Of(row.Table, row.Sprite);
    }

    // The shield hit effect geometry of an ordnance: listed when the install holds it, else a gap with its name.
    void ResolveShieldGeometry(string name, PlacedObject unit)
    {
        if (_files.Find(name + ".sod") == null)
        {
            _gaps.Add(MissingShieldGeometry, name, unit);
        }
        else
        {
            _shieldGeometries.Add(name);
        }
    }

    void ResolveShieldDownModel(PlacedObject unit)
    {
        if (_files.Find(ShieldDownModelName + ".sod") == null)
        {
            _gaps.Add(MissingShieldDownModel, ShieldDownModelName, unit);
        }
        else
        {
            ShieldDownModel = ShieldDownModelName;
        }
    }

    // The hull hit sprite a beam shows on a target without shields: resolved like a shot sprite.
    void ResolveHullSprite(PlacedObject unit)
    {
        _rows ??= SpriteArt.Rows(_files);
        if (_rows.TryGetValue(HullHitSprite.SpriteName, out var row))
        {
            _sprites[HullHitSprite.SpriteName] = SpriteArt.Of(row.Table, row.Sprite);
        }
        else
        {
            _gaps.Add(MissingHullSprite, HullHitSprite.SpriteName, unit);
        }
    }

    // Whether the install holds the sound; else a gap, `none` for an empty name and `missing` for a name it lacks.
    bool ResolveSound(string name, string subjectName, string none, string missing, PlacedObject unit)
    {
        if (name.Length == 0)
        {
            _gaps.Add(none, subjectName, unit);
        }
        else if (_files.Find(name) == null)
        {
            _gaps.Add(missing, name, unit);
        }
        else
            return true;

        return false;
    }

    // An explosion definition that cannot be read is a gap (the grouped line is display only) and one ERROR entry
    // of its own per definition: INV-0005 (an asset that failed to load), INV-0002 (name and reason).
    void Unreadable(string fireball, string reason, PlacedObject unit)
    {
        _gaps.Add(BadExplosion, fireball, unit);
        if (_unreadable.Add(fireball))
        {
            Log.Error(Area.Combat, $"explosion definition {fireball}: {reason}");
        }
    }

    ExplosionArt? ResolveExplosion(string fireball, PlacedObject unit)
    {
        if (fireball.Length == 0)
        {
            _gaps.Add(NoFireball, unit.Class, unit);

            return null;
        }

        ResolvedDefinition definition;
        try
        {
            definition = ResolvedDefinition.Resolve(_files, fireball + ".odf");
        }
        catch (Exception e) // unreadable for whatever reason: reported, the destruction shows none (INV-0002)
        {
            if (Log.IsAbsent(e))
            {
                _gaps.Add(MissingExplosion, fireball, unit);
            }
            else
            {
                Unreadable(fireball, e.Message, unit);
            }

            return null;
        }

        string sound,
            model;
        ExplosionArt art;
        try
        {
            sound = definition.Has("explSound") ? definition.GetString("explSound") : "";
            model = definition.Has("dbName") ? definition.GetString("dbName") : "";
            art = new ExplosionArt
            {
                Sound = sound.Length > 0 && _files.Find(sound) != null ? sound : null,
                Model = model.Length > 0 && _files.Find(model + ".sod") != null ? model : null,
                Length = definition.Has("length") ? definition.GetFloat("length") : 0, // stand-in: an explosion naming no length ends at once
            };
        }
        catch (DefinitionException e)
        {
            Unreadable(fireball, e.Message, unit);

            return null;
        }

        if (art.Sound == null)
        {
            _gaps.Add(
                sound.Length == 0 ? NoExplosionSound : MissingExplosionSound,
                sound.Length == 0 ? fireball : sound,
                unit
            );
        }

        if (art.Model == null)
        {
            _gaps.Add(NoExplosionModel, model.Length == 0 ? fireball : model, unit);
        }

        return art;
    }
}
