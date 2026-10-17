using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using OpenArmada.Assets;
using OpenArmada.Formats;
using OpenArmada.Rendering;
using OpenArmada.Sandbox;

namespace OpenArmada.Viewer;

// Draws and sounds the fight on the combat events of the orders node: a beam from the firing hardpoint to its
// target for its life, a torpedo along its flight, each with the sprite its ordnance names; the weapon's fire
// sound at the firing unit; the explosion of a destroyed unit where it was. Everything the install may lack is
// read once when the node is built and reported through the CombatArt (INV-0002); a shot or a destruction
// without its art still resolves.
public partial class CombatView : Node3D
{
    // stand-in: the thickness of a drawn beam and the size of a drawn torpedo or pulse, in world units; the
    // ordnance's own radius is not read yet.
    const float BeamThickness = 6f;
    const float TorpedoSize = 16f;

    sealed record Tex(ImageTexture Texture, Vector2I Size);

    sealed class Drawn
    {
        public required Shot Shot;
        public required SpriteArt Art;
        public required Tex Tex;
        public required Node3D Root;
        public required StandardMaterial3D Material;
        public Vector3 Offset; // a flying shot: the hardpoint's offset from the center at launch
        public Vector3 Start; // a flying shot: where it was first drawn
        public float Distance; // a flying shot: its distance to the target then
        public bool Placed;
    }

    readonly CombatArt _art;
    readonly CombatWorld _world;
    readonly Dictionary<PlacedObject, Node3D> _nodes;
    readonly Dictionary<string, Tex?> _textures = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, AudioStreamWav> _sounds = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, Node3D> _models = new(StringComparer.OrdinalIgnoreCase);
    readonly List<Drawn> _drawn = new();
    readonly List<(Node3D Node, double Left)> _explosions = new();
    readonly GameFileSystem _files;

    // Reads every texture, sound, and model the fight can show now, so what fails is reported with the map's
    // gaps. `nodes` are the unit nodes of the scene; `models` build the explosion models.
    public CombatView(
        GameFileSystem files,
        CombatArt art,
        CombatWorld world,
        Dictionary<PlacedObject, Node3D> nodes,
        SodBuilder builder,
        UnitOrders orders
    )
    {
        _files = files;
        _art = art;
        _world = world;
        _nodes = nodes;
        foreach (var sprite in art.Sprites)
            Texture(sprite.Sprite.Texture);
        foreach (var name in art.Sounds)
            try
            {
                _sounds[name] = GodotDecoders.Wav(files.ReadAllBytes(name), name);
            }
            catch (Exception e) // a sound that cannot be decoded is a gap, whatever the reason (INV-0002)
            {
                art.Report($"sound cannot be decoded, it is not played ({e.Message})", name);
            }
        foreach (var name in art.ExplosionModels)
            try
            {
                _models[name] = builder.Build(Sod.Read(files.ReadAllBytes(name + ".sod")));
            }
            catch (Exception e) // a model that cannot be read is a gap, whatever the reason (INV-0002)
            {
                art.Report($"explosion model cannot be read, it shows no model ({e.Message})", name);
            }
        orders.Stepped += OnStep;
    }

    Tex? Texture(string name)
    {
        if (_textures.TryGetValue(name, out var known))
            return known;
        try
        {
            var image = GodotDecoders.Tga(new TextureKind().LoadBase(_files, name + ".tga").Data, name);
            return _textures[name] = new Tex(ImageTexture.CreateFromImage(image), image.GetSize());
        }
        catch (Exception e) // a texture that cannot be read is a gap, whatever the reason (INV-0002)
        {
            _art.Report($"sprite texture cannot be read, its shots are not drawn ({e.Message})", name);
            return _textures[name] = null;
        }
    }

    void OnStep(IReadOnlyList<CombatEvent> events)
    {
        foreach (var e in events)
            try
            {
                switch (e)
                {
                    case ShotFired { Shot: var shot }:
                        PlaySound(shot.Weapon.FireSound, At(shot.Shooter.Unit));
                        if (shot.Hits)
                            Draw(shot);
                        break;
                    case UnitDestroyed { Unit: var unit }:
                        Explode(unit);
                        break;
                }
            }
            catch (Exception ex) // a failure of one event's presentation never stops the fight (INV-0002)
            {
                GD.PrintErr($"combat presentation failed: {ex.Message}");
            }
    }

    Vector3 At(PlacedObject unit) =>
        _nodes.TryGetValue(unit, out var node) && IsInstanceValid(node) ? node.GlobalPosition : Vector3.Zero;

    // The hardpoint's place in the world; the unit's center when the model lacks the node (reported when built).
    Vector3 HardpointAt(PlacedObject unit, string? name)
    {
        if (
            name != null
            && _art.Hardpoint(unit, name)
            && _nodes.TryGetValue(unit, out var node)
            && IsInstanceValid(node)
            && node.FindChild(name, true, false) is Node3D hardpoint
        )
            return hardpoint.GlobalPosition;
        return At(unit);
    }

    void PlaySound(string name, Vector3 at)
    {
        if (!_sounds.TryGetValue(name, out var stream)) // only a sound the install holds and Godot decodes is read
            return;
        var player = new AudioStreamPlayer3D { Stream = stream, Position = at };
        player.Finished += player.QueueFree;
        AddChild(player);
        player.Play();
    }

    void Draw(Shot shot)
    {
        var name = shot.Weapon.Ordnance.Sprite;
        if (_art.Sprite(name) is not { } art || Texture(art.Sprite.Texture) is not { } tex)
            return;
        // stand-in: a sprite row of another render type than additive is drawn alpha blended.
        var material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoTexture = tex.Texture,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = art.Sprite.Material is "additive" or "add_nomipf"
                ? BaseMaterial3D.BlendModeEnum.Add
                : BaseMaterial3D.BlendModeEnum.Mix,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.Linear,
        };
        var root = new Node3D();
        if (shot.Kind == ShotKind.Beam)
            // two crossed quads: the beam shows from any side
            foreach (var roll in new[] { 0f, Mathf.Pi / 2 })
                root.AddChild(
                    new MeshInstance3D
                    {
                        Mesh = new QuadMesh { Size = new Vector2(1, BeamThickness), Material = material },
                        Rotation = new Vector3(roll, 0, 0),
                    }
                );
        else
        {
            material.BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled;
            root.AddChild(
                new MeshInstance3D
                {
                    Mesh = new QuadMesh { Size = new Vector2(TorpedoSize, TorpedoSize), Material = material },
                }
            );
        }
        var from = shot.Shooter.Unit;
        var drawn = new Drawn
        {
            Shot = shot,
            Art = art,
            Tex = tex,
            Root = root,
            Material = material,
            Offset = HardpointAt(from, shot.Hardpoint) - At(from),
        };
        AddChild(root);
        _drawn.Add(drawn);
        Place(drawn);
    }

    void Place(Drawn d)
    {
        var shot = d.Shot;
        var frames = d.Art.Frames;
        var frame = d.Art.FrameSeconds > 0 ? frames[(int)(shot.Age / d.Art.FrameSeconds) % frames.Count] : frames[0];
        var region = SpriteSheetView.Region(d.Art.Sprite, frame, d.Tex.Size);
        d.Material.Uv1Scale = new Vector3(region.Size.X / d.Tex.Size.X, region.Size.Y / d.Tex.Size.Y, 1);
        d.Material.Uv1Offset = new Vector3(region.Position.X / d.Tex.Size.X, region.Position.Y / d.Tex.Size.Y, 0);
        if (shot.Kind == ShotKind.Beam)
        {
            var start = HardpointAt(shot.Shooter.Unit, shot.Hardpoint);
            var end = At(shot.Target.Unit);
            var along = end - start;
            if (along.LengthSquared() < 1e-6f)
                return;
            var x = along.Normalized();
            var up = Mathf.Abs(x.Y) > 0.99f ? Vector3.Forward : Vector3.Up;
            var z = x.Cross(up).Normalized();
            d.Root.Basis = new Basis(x, z.Cross(x), z).Scaled(new Vector3(along.Length(), 1, 1));
            d.Root.Position = (start + end) / 2;
            return;
        }
        var at = new Vector3(shot.Position.X, shot.Position.Y, shot.Position.Z);
        if (!d.Placed)
        {
            d.Placed = true;
            d.Start = at;
            d.Distance = (At(shot.Target.Unit) - at).Length();
        }
        // The launch offset fades as the shot closes on its target, so it arrives at the target's center.
        var left = d.Distance > 0 ? 1 - Mathf.Clamp((at - d.Start).Length() / d.Distance, 0, 1) : 0;
        d.Root.Position = at + d.Offset * left;
    }

    void Explode(PlacedObject unit)
    {
        if (_art.Explosion(unit) is not { } explosion)
            return;
        var at = At(unit);
        PlaySound(explosion.Sound ?? "", at);
        if (explosion.Model == null || !_models.TryGetValue(explosion.Model, out var template))
            return;
        var node = (Node3D)template.Duplicate();
        node.Position = at;
        AddChild(node);
        _explosions.Add((node, explosion.Length));
    }

    public override void _Process(double delta)
    {
        var flying = _world.Shots.ToHashSet();
        foreach (var d in _drawn.ToList())
            if (!flying.Contains(d.Shot))
            {
                d.Root.QueueFree();
                _drawn.Remove(d);
            }
            else
                Place(d);
        for (var i = _explosions.Count - 1; i >= 0; i--)
        {
            var (node, left) = _explosions[i];
            if (left - delta > 0)
                _explosions[i] = (node, left - delta);
            else
            {
                node.QueueFree();
                _explosions.RemoveAt(i);
            }
        }
    }
}
