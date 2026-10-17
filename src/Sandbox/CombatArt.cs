using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenArmada.Assets;
using OpenArmada.Formats;

namespace OpenArmada.Sandbox;

// A shot's sprite: its row, whose Material is its blend, and its frames in play order, each for FrameSeconds.
public sealed record SpriteArt(Sprite Sprite, IReadOnlyList<SpriteFrame> Frames, double FrameSeconds);

// A unit's explosion: the sound and the model it shows for Length seconds. Each is null when the install lacks it.
public sealed record ExplosionArt(string? Sound, string? Model, float Length);

// What the fight looks and sounds like, resolved once from the install for the units of a map. Every lookup
// that fails is a gap, reported once per cause with its names and the number of units it touches (INV-0002).
public sealed class CombatArt
{
    const string NoSprite = "ordnance names no sprite, its shots are not drawn";
    const string MissingSprite = "sprite missing from the sprite tables, its shots are not drawn";
    const string NoSound = "weapon names no fire sound, its shots are silent";
    const string MissingSound = "sound missing from the install, it is not played";
    const string NoFireball = "unit names no fireball, its destruction shows no explosion";
    const string MissingExplosion = "explosion definition missing, the destruction shows no explosion";
    const string BadExplosion = "explosion definition cannot be read, the destruction shows no explosion";
    const string NoExplosionModel = "explosion names no model that the install holds, it shows no model";
    const string MissingHardpoint = "hardpoint missing from the model, its shots leave from the unit's center";

    readonly GameFileSystem _files;
    readonly Gaps _gaps = new();
    readonly Dictionary<string, SpriteArt> _sprites = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, string> _sounds = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<PlacedObject, ExplosionArt> _explosions = new(ReferenceEqualityComparer.Instance);
    readonly Dictionary<PlacedObject, HashSet<string>> _hardpoints = new(ReferenceEqualityComparer.Instance);
    readonly Dictionary<string, HashSet<string>> _nodesOfModel = new(StringComparer.OrdinalIgnoreCase);
    Dictionary<string, (SpriteTable Table, Sprite Sprite)>? _rows;

    CombatArt(GameFileSystem files) => _files = files;

    public static CombatArt Build(GameFileSystem files, IEnumerable<(PlacedObject Unit, CombatProfile Profile)> units)
    {
        var art = new CombatArt(files);
        foreach (var (unit, profile) in units)
        {
            art._hardpoints[unit] = art.NodesOf(unit.Model);
            foreach (var weapon in profile.Weapons)
            {
                art.ResolveSprite(weapon, unit);
                art.ResolveSound(weapon.FireSound, weapon.Name, NoSound, unit);
                foreach (var hardpoint in weapon.Hardpoints)
                    if (hardpoint != null && !art._hardpoints[unit].Contains(hardpoint))
                        art._gaps.Add(MissingHardpoint, hardpoint, unit);
            }
            if (art.ResolveExplosion(profile.Fireball, unit) is { } explosion)
                art._explosions[unit] = explosion;
        }
        return art;
    }

    // Every sprite, every sound (fire and explosion), and every explosion model the fight can show, each once.
    public IEnumerable<SpriteArt> Sprites => _sprites.Values;

    public IEnumerable<string> Sounds =>
        _sounds.Values.Concat(_explosions.Values.Select(e => e.Sound).OfType<string>()).Distinct();

    public IEnumerable<string> ExplosionModels =>
        _explosions.Values.Select(e => e.Model).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase);

    // The shot sprite of an ordnance's `Sprite` name; null when it is missing (a gap).
    public SpriteArt? Sprite(string name) => _sprites.GetValueOrDefault(name);

    // The fire sound file of a weapon's `fireSound` name; null when the install lacks it (a gap).
    public string? Sound(string name) => _sounds.GetValueOrDefault(name);

    // The explosion of a unit's fireball; null when its definition is missing or unreadable (a gap).
    public ExplosionArt? Explosion(PlacedObject unit) => _explosions.GetValueOrDefault(unit);

    // Whether the unit's model holds a node of the name.
    public bool Hardpoint(PlacedObject unit, string name) =>
        _hardpoints.TryGetValue(unit, out var nodes) && nodes.Contains(name);

    // A failure of the view (a texture or a sound that cannot be decoded) joins the gaps, once per cause.
    public void Report(string cause, string name) => _gaps.Add(cause, name);

    public IReadOnlyList<ReportEntry> Reports => _gaps.Entries;

    // The names of the nodes of a unit's model; none when the model cannot be read (the sandbox reports that).
    HashSet<string> NodesOf(string model)
    {
        if (!_nodesOfModel.TryGetValue(model, out var nodes))
        {
            nodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                nodes.UnionWith(Sod.Read(_files.ReadAllBytes(model + ".sod")).Nodes.Select(n => n.Name));
            }
            catch (Exception e) when (e is IOException or InvalidDataException or UnsupportedFormatException) { }
            _nodesOfModel[model] = nodes;
        }
        return nodes;
    }

    void ResolveSprite(WeaponProfile weapon, PlacedObject unit)
    {
        var name = weapon.Ordnance.Sprite;
        if (name.Length == 0)
        {
            _gaps.Add(NoSprite, weapon.Name, unit);
            return;
        }
        _rows ??= LoadRows();
        if (!_rows.TryGetValue(name, out var row))
        {
            _gaps.Add(MissingSprite, name, unit);
            return;
        }
        var frames = row.Table.Frames(row.Sprite);
        var seconds =
            frames.Count > 1 && row.Sprite.Anim != null && row.Table.Animations.TryGetValue(row.Sprite.Anim, out var a)
                ? a.FrameSeconds
                : 0;
        _sprites[name] = new SpriteArt(row.Sprite, frames, seconds);
    }

    // Every sprite of the install's tables by name; the first row of a name wins. A table that cannot be read adds none.
    Dictionary<string, (SpriteTable, Sprite)> LoadRows()
    {
        var rows = new Dictionary<string, (SpriteTable, Sprite)>(StringComparer.OrdinalIgnoreCase);
        foreach (var table in new SpriteTableKind().Tables(_files))
        foreach (var sprite in table.Sprites)
            rows.TryAdd(sprite.Name, (table, sprite));
        return rows;
    }

    // The name when the install holds the sound, else null with a gap; an empty name is the cause `none`.
    string? ResolveSound(string name, string subjectName, string none, PlacedObject unit)
    {
        if (name.Length == 0)
        {
            _gaps.Add(none, subjectName, unit);
            return null;
        }
        if (_files.Find(name) == null)
        {
            _gaps.Add(MissingSound, name, unit);
            return null;
        }
        return _sounds[name] = name;
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
            _gaps.Add(e is FileNotFoundException ? MissingExplosion : BadExplosion, fireball, unit);
            return null;
        }
        string sound = "",
            model = "";
        var length = 0f; // stand-in: an explosion that names no length ends at once
        try
        {
            sound = definition.Has("explSound") ? definition.GetString("explSound") : "";
            model = definition.Has("dbName") ? definition.GetString("dbName") : "";
            length = definition.Has("length") ? definition.GetFloat("length") : 0;
        }
        catch (DefinitionException)
        {
            _gaps.Add(BadExplosion, fireball, unit);
            return null;
        }
        var art = new ExplosionArt(
            sound.Length > 0 && _files.Find(sound) != null ? sound : null,
            model.Length > 0 && _files.Find(model + ".sod") != null ? model : null,
            length
        );
        if (art.Sound == null)
            _gaps.Add(sound.Length == 0 ? NoSound : MissingSound, sound.Length == 0 ? fireball : sound, unit);
        if (art.Model == null)
            _gaps.Add(NoExplosionModel, model.Length == 0 ? fireball : model, unit);
        return art;
    }
}
