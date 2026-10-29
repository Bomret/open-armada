using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using OpenArmada.Assets;
using OpenArmada.Diagnostics;
using OpenArmada.Formats;
using OpenArmada.Rendering;
using OpenArmada.Sandbox;

namespace OpenArmada.Viewer;

/// <summary>
/// Draws the damage emitters of a set of units, the units in the fight or a previewed model: each frame it steps every emitter's <see cref="SprayEmitter"/>
/// by the frame's seconds, from the emitter node's world frame and whether its damage node shows (the frame's
/// <see cref="UnitOrders.ShowingNodes"/>, decided by <see cref="DamageNodes.ShowingNodes"/>), and draws each live
/// particle as a billboard of its sprite at its size, with the sprite node's animation playing once from the particle's
/// birth (stand-in). An emitter under a hidden damage node sprays nothing, and the particles it sprayed before fly on
/// and are drawn until they die (0x58cb60). The simulation and the decisions are the sandbox's; this node only moves
/// quads. It comes after the orders node in the scene, so a frame's damage nodes and unit poses are current when it
/// runs. What the install lacks is reported once (INV-0002): the emitter gaps through
/// <c>gap</c>, a sprite texture as a log entry. A failure of one unit's sprays skips only that unit, and is logged
/// once with a count of its repeats (INV-0002, INV-0004).
/// </summary>
public partial class DamageSprayView : Node3D
{
    // One emitter of one unit: its simulation, its particle quads, and what it draws them with.
    sealed class Spray
    {
        public required SprayEmitter Emitter;
        public required DamageEmitter Damage;
        public required Texture2D? Texture;
        public required StandardMaterial3D Template;
        public readonly List<MeshInstance3D> Quads = new();
    }

    readonly GameFileSystem _files;
    readonly Func<IEnumerable<object>> _units;
    readonly Func<object, Node3D?> _root;
    readonly Func<object, Sod?> _model;
    readonly Func<object, IReadOnlySet<Sod.Node>> _showing;
    readonly Action<string> _gap;
    readonly RepeatedFaults _faults = new(Area.Combat);
    readonly Random _random;
    readonly Dictionary<Sod, DamageEmitters> _emitters = new();
    readonly Dictionary<string, Texture2D?> _textures = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<(object, Sod.Node), Spray> _sprays = new();
    readonly Dictionary<Node3D, Dictionary<int, Node3D>> _built = new();

    /// <summary>
    /// <c>units</c> are this frame's units, each a key such as a <see cref="PlacedObject"/>; <c>root</c> reads a unit's
    /// node in the scene; <c>model</c> reads a unit's model; <c>showing</c> reads the damage
    /// nodes that show on a unit this frame (<see cref="UnitOrders.ShowingNodes"/>); <c>seed</c> fixes the random source
    /// of the particles' start angles (null: seeded from the system).
    /// </summary>
    public DamageSprayView(
        GameFileSystem files,
        Func<IEnumerable<object>> units,
        Func<object, Node3D?> root,
        Func<object, Sod?> model,
        Func<object, IReadOnlySet<Sod.Node>> showing,
        Action<string> gap,
        int? seed
    )
    {
        _files = files;
        _units = units;
        _root = root;
        _model = model;
        _showing = showing;
        _gap = gap;
        _random = seed is { } s ? new Random(s ^ 0x5A17) : new Random();
    }

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        var live = new HashSet<(object, Sod.Node)>();
        foreach (var unit in _units())
        {
            try
            {
                StepSprays(unit, delta, live);
            }
            catch (Exception e) // a failure of one unit's sprays never stops the others or the cleanup (INV-0002)
            {
                _faults.Report($"damage spray failed: {e.Message}");
            }
        }

        foreach (var root in _built.Keys.Where(r => !IsInstanceValid(r)).ToList())
        {
            _built.Remove(root);
        }

        // A unit that left the world takes its particles with it.
        foreach (var key in _sprays.Keys.Where(k => !live.Contains(k)).ToList())
        {
            foreach (var quad in _sprays[key].Quads)
            {
                quad.QueueFree();
            }

            _sprays.Remove(key);
        }
    }

    /// <summary>
    /// Logs the count of repeated spray faults. Godot exits the children of a node before the node, so this runs
    /// before the run-end entry of the AssetBrowserView above the sandbox.
    /// </summary>
    public override void _ExitTree() => _faults.Flush();

    // Steps and draws the sprays of one unit, and adds each of its emitters to `live` before any of them runs, so a
    // failure part way keeps the unit's particles out of the cleanup.
    void StepSprays(object unit, double delta, HashSet<(object, Sod.Node)> live)
    {
        if (_root(unit) is not { } root || !IsInstanceValid(root) || _model(unit) is not { } sod)
            return;
        var emitters = Emitters(sod);
        if (emitters.Emitters.Count == 0)
            return;
        live.UnionWith(emitters.Emitters.Select(d => (unit, d.Node)));
        var showing = _showing(unit);
        var built = BuiltNodes(root);
        foreach (var damage in emitters.Emitters)
        {
            var key = (unit, damage.Node);
            if (!built.TryGetValue(sod.IndexOf(damage.Node), out var node))
                continue;
            if (!IsInstanceValid(node))
            {
                _built.Remove(root);

                continue;
            }
            if (!_sprays.TryGetValue(key, out var spray))
            {
                _sprays[key] = spray = NewSpray(damage);
            }

            var on = showing.Contains(damage.Node);
            spray.Emitter.Step(delta, on, FrameOf(node.GlobalTransform));
            Draw(spray);
        }
    }

    DamageEmitters Emitters(Sod sod)
    {
        if (_emitters.TryGetValue(sod, out var known))
            return known;
        var resolved = _emitters[sod] = DamageEmitters.For(_files, sod);
        foreach (var report in Gaps.Logged(resolved.Reports))
        {
            _gap(report.Text);
        }

        return resolved;
    }

    // The built nodes of a model by their index in the model (SodBuilder.SodIndexMeta), walked once per root: the walk
    // is recursive, so it is not repeated each frame. A root with no built node yet is walked again next frame.
    Dictionary<int, Node3D> BuiltNodes(Node3D root)
    {
        if (_built.TryGetValue(root, out var known))
            return known;
        var built = root.FindChildren("*", "", true, false)
            .OfType<Node3D>()
            .Append(root)
            .Where(n => n.HasMeta(SodBuilder.SodIndexMeta))
            .GroupBy(n => (int)n.GetMeta(SodBuilder.SodIndexMeta))
            .ToDictionary(g => g.Key, g => g.First());
        if (built.Count > 0)
            _built[root] = built;

        return built;
    }

    // The node's own axes in the world. The builder mirrors the model's Z (SodBuilder.ToGodot), so a model's +Z is the
    // built node's -Z.
    static EmitterFrame FrameOf(Transform3D world)
    {
        static System.Numerics.Vector3 Unit(Vector3 v) => new(v.Normalized().X, v.Normalized().Y, v.Normalized().Z);

        return new EmitterFrame(
            new System.Numerics.Vector3(world.Origin.X, world.Origin.Y, world.Origin.Z),
            Unit(world.Basis.X),
            Unit(world.Basis.Y),
            Unit(-world.Basis.Z)
        );
    }

    Spray NewSpray(DamageEmitter damage)
    {
        var texture = Texture(damage.Sprite.Sprite.Sprite.Texture);
        // The quad is made only for its mesh and material: Create sets the sprite's blend and billboard.
        var template = SpriteNodeQuad.Create(damage.Sprite, texture ?? new PlaceholderTexture2D());
        var material = (StandardMaterial3D)template.MaterialOverride;
        template.Free();

        return new Spray
        {
            Emitter = new SprayEmitter(damage.Definition, _random.NextDouble),
            Damage = damage,
            Texture = texture,
            Template = material,
        };
    }

    void Draw(Spray spray)
    {
        var particles = spray.Emitter.Particles;
        var sprite = spray.Damage.Sprite;
        while (spray.Quads.Count < particles.Count)
        {
            var quad = new MeshInstance3D
            {
                Mesh = new QuadMesh(),
                MaterialOverride = (StandardMaterial3D)spray.Template.Duplicate(),
            };
            AddChild(quad);
            spray.Quads.Add(quad);
        }

        for (var i = 0; i < spray.Quads.Count; i++)
        {
            var quad = spray.Quads[i];
            if (spray.Texture == null || i >= particles.Count)
            {
                quad.Visible = false;

                continue;
            }

            var particle = particles[i];
            var width = spray.Emitter.Size(sprite.Width, particle);
            var height = spray.Emitter.Size(sprite.Height, particle);
            quad.Position = new Vector3(particle.Position.X, particle.Position.Y, particle.Position.Z);
            ((QuadMesh)quad.Mesh).Size = new Vector2((float)Math.Max(width, 0), (float)Math.Max(height, 0));
            var material = (StandardMaterial3D)quad.MaterialOverride;
            // stand-in: the animation plays once from the particle's birth and holds its last keyframe (the game's
            // looping of a particle animation is not recovered).
            var draw = SpriteNodeQuad.Show(
                material,
                sprite,
                spray.Texture,
                spray.Template.AlbedoColor.A,
                particle.Age,
                loop: false
            );
            quad.Visible = draw && SprayEmitter.Drawn(width, height);
        }
    }

    Texture2D? Texture(string name)
    {
        if (_textures.TryGetValue(name, out var known))
            return known;
        try
        {
            var image = GodotDecoders.Tga(new TextureKind().LoadBase(_files, name + ".tga").Data, name);

            return _textures[name] = ImageTexture.CreateFromImage(image);
        }
        catch (Exception e) // a texture that did not load is a gap, whatever the reason (INV-0002)
        {
            ViewerLog.TextureFailed(Area.Combat, name, e);

            return _textures[name] = null;
        }
    }
}
