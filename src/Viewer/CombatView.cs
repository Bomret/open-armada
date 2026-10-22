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
/// Draws and sounds the fight on the combat events of the orders node: a beam from the firing hardpoint to its
/// target for its life, a torpedo along its flight, each with the sprite its ordnance names; the weapon's fire
/// sound at the firing unit, once per weapon volley; the explosion of a destroyed unit where it was. A shot that
/// missed is drawn too, aimed beside its target, for its ordnance's life. Everything the install may lack is
/// read once when the node is built and reported through the CombatArt (INV-0002); a shot or a destruction
/// without its art still resolves.
/// </summary>
public partial class CombatView : Node3D
{
    // stand-in: the thickness of a drawn beam and the size of a drawn torpedo or pulse, in world units; the
    // ordnance's own radius is not read yet.
    const float BeamThickness = 6f;
    const float TorpedoSize = 16f;

    sealed class Drawn
    {
        public required Shot Shot;
        public required SpriteArt Art;
        public required ImageTexture Texture;
        public required Node3D Root;
        public required StandardMaterial3D Material;
        public required Node3D Source; // the node the shot leaves from, resolved once when drawn
        public Vector3 Start; // a missed shot: where it leaves from, the chosen hardpoint's position
        public bool Missed; // the world dropped the shot; the view draws it on its own clock
        public double Age; // a missed shot: seconds drawn
        public Vector3 AimOffset; // a missed beam: where it ends, from the target's center
        public Vector3 Direction; // a missed torpedo or pulse: its straight flight
    }

    readonly CombatArt _art;
    readonly RepeatedFaults _faults = new(Area.Combat);
    readonly CombatWorld _world;
    readonly Dictionary<PlacedObject, Node3D> _nodes;
    readonly Dictionary<string, ImageTexture?> _textures = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, AudioStreamWav> _sounds = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, Node3D> _models = new(StringComparer.OrdinalIgnoreCase);
    readonly List<Drawn> _drawn = new();
    readonly GameFileSystem _files;
    readonly SoundFalloff _falloff;

    /// <summary>
    /// Reads every texture, sound, and model the fight can show now, so what fails is reported with the map's
    /// gaps. <c>nodes</c> are the unit nodes of the scene; <c>models</c> build the explosion models, whose problems
    /// go to <c>gap</c>; <c>falloff</c> is how every fire and explosion sound fades with distance.
    /// </summary>
    public CombatView(
        GameFileSystem files,
        CombatArt art,
        CombatWorld world,
        Dictionary<PlacedObject, Node3D> nodes,
        SodBuilder builder,
        UnitOrders orders,
        SoundFalloff falloff,
        Action<string> gap
    )
    {
        _files = files;
        _falloff = falloff;
        _art = art;
        _world = world;
        _nodes = nodes;
        foreach (var sprite in art.Sprites)
        {
            Texture(sprite.Sprite.Texture);
        }

        foreach (var name in art.Sounds)
        {
            try
            {
                _sounds[name] = GodotDecoders.Wav(files.ReadAllBytes(name), name);
            }
            catch (Exception e) // a sound that cannot be decoded is a gap, whatever the reason (INV-0002)
            {
                art.Fail("sound", name, e.Message, "sound cannot be decoded, it is not played");
            }
        }

        foreach (var name in art.ExplosionModels)
        {
            try
            {
                _models[name] = builder.Build(Sod.Read(files.ReadAllBytes(name + ".sod")));
                foreach (var problem in builder.Problems)
                {
                    // The model is built and shown; each Problems entry is logged here, once per cause.
                    Log.Warn(Area.Combat, $"{name}: {problem}");
                    gap($"{name}: {problem}");
                }

                // The builder logged each texture that failed (ViewerLog.TextureFailed); these lines are only shown.
                foreach (var failure in builder.TextureFailures)
                {
                    gap($"{name}: {failure}");
                }
            }
            catch (Exception e) // a model that cannot be read is a gap, whatever the reason (INV-0002)
            {
                art.Fail("explosion model", name, e.Message, "explosion model cannot be read, it shows no model");
            }
        }

        LoadShieldModels(art, builder, gap);

        orders.Stepped += OnStep;
    }

    ImageTexture? Texture(string name)
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
            // The grouped gap is display only; the texture is one entry per run, WARN when missing, else ERROR.
            _art.Report(
                Log.IsAbsent(e)
                    ? "sprite texture missing, its shots are not drawn"
                    : "sprite texture cannot be read, its shots are not drawn",
                name
            );
            ViewerLog.TextureFailed(Area.Combat, name, e);

            return _textures[name] = null;
        }
    }

    void OnStep(IReadOnlyList<CombatEvent> events)
    {
        var sounded = new HashSet<(UnitCombatState, int)>(); // the weapons whose fire sound this step played
        foreach (var e in events)
        {
            try
            {
                switch (e)
                {
                    case ShotFired { Shot: var shot }:
                        if (sounded.Add((shot.Shooter, shot.Weapon.Slot)) && At(shot.Shooter.Unit) is { } at)
                        {
                            PlaySound(shot.Weapon.FireSound, at);
                        }

                        Draw(shot);

                        break;
                    case ShieldHitEffect effect:
                        RaiseShieldEffect(effect);

                        break;
                    case HullHitSprite sprite:
                        RaiseHullSprite(sprite);

                        break;
                    case UnitDestroyed { Unit: var unit }:
                        Explode(unit);

                        break;
                }
            }
            catch (Exception ex) // a failure of one event's presentation never stops the fight (INV-0002)
            {
                _faults.Report($"combat presentation failed: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Logs the count of repeated presentation faults. Godot exits the children of a node before the node, so this
    /// runs before the run-end entry of the AssetBrowserView above the sandbox.
    /// </summary>
    public override void _ExitTree() => _faults.Flush();

    // A unit's place in the world; null when its node is gone, so nothing is drawn or sounded there.
    Vector3? At(PlacedObject unit) =>
        _nodes.TryGetValue(unit, out var node) && IsInstanceValid(node) ? node.GlobalPosition : null;

    // The node a shot leaves from: the hardpoint's node, or the unit's own node when the model lacks the hardpoint
    // (reported when built); null when the unit's node is gone.
    Node3D? Source(PlacedObject unit, string name)
    {
        if (!_nodes.TryGetValue(unit, out var node) || !IsInstanceValid(node))
            return null;

        return _art.Hardpoint(unit, name) is { } exact && node.FindChild(exact, true, false) is Node3D hardpoint
            ? hardpoint
            : node;
    }

    static System.Numerics.Vector3 Num(Vector3 v) => new(v.X, v.Y, v.Z);

    static Vector3 Gd(System.Numerics.Vector3 v) => new(v.X, v.Y, v.Z);

    void PlaySound(string name, Vector3 at)
    {
        if (!_sounds.TryGetValue(name, out var stream)) // only a sound the install holds and Godot decodes is read
            return;
        var player = new AudioStreamPlayer3D
        {
            Stream = stream,
            Position = at,
            UnitSize = _falloff.UnitSize,
            MaxDb = SoundFalloff.MaxDb,
            AttenuationFilterCutoffHz = SoundFalloff.FilterCutoffHz,
        };
        player.Finished += player.QueueFree;
        AddChild(player);
        player.Play();
    }

    void Draw(Shot shot)
    {
        var name = shot.Weapon.Ordnance.Sprite;
        if (_art.Sprite(name) is not { } art || Texture(art.Sprite.Texture) is not { } tex)
            return;
        var from = shot.Shooter.Unit;
        if (At(from) is null || Source(from, shot.Hardpoint) is not { } source)
            return;
        if (At(shot.Target.Unit) is not { } targetAt)
            return;
        var material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoTexture = tex,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        SpriteMaterial.Blend(material, art.Sprite.Material);
        var root = new Node3D();
        if (shot.Kind == ShotKind.Beam)
        {
            // two crossed quads: the beam shows from any side
            foreach (var roll in new[] { 0f, Mathf.Pi / 2 })
            {
                root.AddChild(
                    new MeshInstance3D
                    {
                        Mesh = new QuadMesh { Size = new Vector2(1, BeamThickness), Material = material },
                        Rotation = new Vector3(roll, 0, 0),
                    }
                );
            }
        }
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

        var at = new Vector3(shot.Position.X, shot.Position.Y, shot.Position.Z);
        var drawn = new Drawn
        {
            Shot = shot,
            Art = art,
            Texture = tex,
            Root = root,
            Material = material,
            Source = source,
            Start = at,
            Missed = !shot.Hits,
        };
        if (drawn.Missed)
        {
            // The world drops a missed shot at once; it is drawn aimed beside the target, off the line from the
            // hardpoint it leaves from, and flies on straight.
            drawn.AimOffset = Gd(ShotGeometry.MissAim(Num(at), Num(targetAt), shot.Target.Radius)) - targetAt;
            drawn.Direction = (targetAt + drawn.AimOffset - at).Normalized();
        }

        AddChild(root);
        _drawn.Add(drawn);
        Place(drawn);
    }

    void Place(Drawn d)
    {
        var shot = d.Shot;
        SpriteMaterial.ShowFrame(d.Material, d.Art.Sprite, d.Art.FrameAt(d.Missed ? d.Age : shot.Age), d.Texture);
        if (shot.Kind == ShotKind.Beam)
        {
            if (At(shot.Target.Unit) is not { } target)
                return;
            if (!IsInstanceValid(d.Source))
                return;
            var start = d.Source.GlobalPosition;
            if (ShotGeometry.Beam(Num(start), Num(d.Missed ? target + d.AimOffset : target)) is not { } pose)
                return;
            d.Root.Basis = new Basis(Gd(pose.X), Gd(pose.Y), Gd(pose.Z));
            d.Root.Position = Gd(pose.Center);

            return;
        }

        if (d.Missed)
        {
            d.Root.Position = d.Start + d.Direction * shot.Weapon.Ordnance.ShotSpeed * (float)d.Age;

            return;
        }

        // the world's shot starts at the hardpoint, so it is drawn at its own position
        d.Root.Position = new Vector3(shot.Position.X, shot.Position.Y, shot.Position.Z);
    }

    void Explode(PlacedObject unit)
    {
        if (_art.Explosion(unit) is not { } explosion || At(unit) is not { } at)
            return;
        PlaySound(explosion.Sound ?? "", at);
        if (explosion.Model == null || !_models.TryGetValue(explosion.Model, out var template))
            return;
        // Each explosion plays its sprite nodes once from the moment of destruction, on its own clock.
        // stand-in: the install does not say whether explosions loop.
        var node = (Node3D)template.Duplicate();
        SpriteNodeQuad.PlayOnce(node);
        node.Position = at;
        AddChild(node);
        GetTree().CreateTimer(explosion.Length).Timeout += () =>
        {
            if (IsInstanceValid(node))
            {
                node.QueueFree();
            }
        };
    }

    /// <summary>Frees the explosion templates with the view: they are never in the scene.</summary>
    public override void _Notification(int what)
    {
        if (what != NotificationPredelete)
            return;

        foreach (var template in _models.Values)
        {
            if (IsInstanceValid(template))
            {
                template.Free();
            }
        }
    }

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        UpdateShieldEffects();
        var live = _world.Shots.ToHashSet();
        _drawn.RemoveAll(d =>
        {
            // stand-in: a missed shot ages on the frame time and a hit in whole world ticks, so the clocks differ by under a tick.
            var gone = d.Missed ? (d.Age += delta) >= d.Shot.Weapon.Ordnance.LifeSpan : !live.Contains(d.Shot);
            if (gone)
            {
                d.Root.QueueFree();
            }
            else
            {
                Place(d);
            }

            return gone;
        });
    }
}
