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
/// sound at the firing unit, once per weapon volley; the explosions of hits and the repair sparks of repairing systems
/// at their nodes; the small explosions on a dying unit; the explosion of a destroyed unit where it was; the transport
/// effect, sounds, and voice line, and the new team color of a captured unit (the cues of CombatArt.TransportCues). A shot that
/// missed is drawn too, aimed beside its target, for its ordnance's life. Everything the install may lack is read once
/// when the node is built and reported through the CombatArt (INV-0002); a shot or a destruction without its art still
/// resolves.
/// </summary>
public partial class CombatView : Node3D
{
    // stand-in: the thickness of a drawn beam and the size of a drawn torpedo or pulse, in world units; the
    // ordnance's own radius is not read yet.
    const float BeamThickness = 6f;
    const float TorpedoSize = 16f;

    // stand-in: the size of a small explosion's billboard in world units; the game's size is not known.
    const float SmallExplosionSize = 24f;

    // stand-in: Godot's stereo panning keeps a sound's total power, so a sound straight ahead plays at -3 dB in each
    // channel, where the game's DirectSound plays it at full level in both (inferred). The lift gives back those 3 dB;
    // a sound off to one side plays up to 3 dB louder in its near channel than the game plays it.
    const float PanLiftDb = 3.0103f;

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

    sealed class Sounding
    {
        public required AudioStreamPlayer3D Player;
        public required PlacedObject? Follows; // the unit it follows; null: it stays where it started
        public required double MinimumScale;
    }

    readonly CombatArt _art;
    readonly Action<PlacedObject, int> _recolor; // gives a unit's node the team color of a team's slot, the unit alone
    readonly Dictionary<PlacedObject, Node3D> _effects = new(ReferenceEqualityComparer.Instance); // the transport effect each unit holds, a child of its node
    readonly RepeatedFaults _faults = new(Area.Combat);
    readonly CombatWorld _world;
    readonly Dictionary<PlacedObject, Node3D> _nodes;
    readonly Dictionary<string, ImageTexture?> _textures = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, AudioStreamWav> _sounds = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, Node3D> _models = new(StringComparer.OrdinalIgnoreCase);
    readonly List<Drawn> _drawn = new();
    readonly List<Sounding> _sounding = new();
    readonly GameFileSystem _files;
    readonly SoundFade _fade;

    /// <summary>
    /// Reads every texture, sound, and model the fight can show now, so what fails is reported with the map's
    /// gaps. <c>nodes</c> are the unit nodes of the scene; <c>models</c> build the explosion models, whose problems
    /// go to <c>gap</c>; <c>fade</c> is how every fire and explosion sound fades with its distance from the camera.
    /// </summary>
    public CombatView(
        GameFileSystem files,
        CombatArt art,
        CombatWorld world,
        Dictionary<PlacedObject, Node3D> nodes,
        SodBuilder builder,
        UnitOrders orders,
        SoundFade fade,
        Action<PlacedObject, int> recolor,
        Action<string> gap
    )
    {
        _recolor = recolor;
        _files = files;
        _fade = fade;
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
        LoadBeamModels(art, builder, gap);

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
                foreach (var cue in _art.TransportCues(e))
                {
                    Apply(cue);
                }

                switch (e)
                {
                    case ShotFired { Shot: var shot }:
                        if (sounded.Add((shot.Shooter, shot.Weapon.Slot)) && At(shot.Shooter.Unit) is { } at)
                        {
                            PlaySound(shot.Weapon.FireSound, at, SoundFade.FireMinimumScale, shot.Shooter.Unit);
                        }

                        Draw(shot);

                        break;
                    case SpecialWeaponStarted started:
                        StartSpecial(started);

                        break;
                    case ShieldHitEffect effect:
                        RaiseShieldEffect(effect);

                        break;
                    case HullHitSprite sprite:
                        RaiseHullSprite(sprite);

                        break;
                    case UnitDestroyed { Unit: var unit, Cause: DestroyCause.Fireball }:
                        Explode(unit);

                        break;
                    case RepairSpark spark:
                        ShowAtNode(
                            RepairSpark.SpriteName,
                            spark.Unit,
                            spark.Hardpoint,
                            spark.Size,
                            RepairSpark.Seconds
                        );

                        break;
                    case HitExplosion explosion:
                        ShowAtNode(
                            explosion.Name,
                            explosion.Unit,
                            explosion.Hardpoint,
                            explosion.Size,
                            HitExplosion.Seconds
                        );

                        break;
                    case SmallExplosion { Unit: var dying, Name: var name, Offset: var offset }:
                        if (At(dying) is { } center)
                        {
                            ShowSmallExplosion(name, center + Gd(offset));
                        }

                        break;
                }
            }
            catch (Exception ex) // a failure of one event's presentation never stops the fight (INV-0002)
            {
                _faults.Report($"combat presentation failed: {ex.Message}");
            }
        }
    }

    // Shows or plays one transport cue. The effect hangs from the unit's node, so it follows the unit and goes with it.
    void Apply(TransportCue cue)
    {
        switch (cue.Kind)
        {
            case CueKind.EffectOn:
                ShowEffect(cue.Unit);

                break;
            case CueKind.EffectOff:
                if (_effects.Remove(cue.Unit, out var effect) && IsInstanceValid(effect))
                {
                    effect.QueueFree();
                }

                break;
            case CueKind.Sound:
                if (At(cue.Unit) is { } at)
                {
                    PlaySound(cue.Name, at, SoundFade.FireMinimumScale, cue.Unit);
                }

                break;
            case CueKind.Recolor:
                _recolor(cue.Unit, cue.Team);

                break;
        }
    }

    // The transport effect (0x46db90): the xtransport flipbook loops on a billboard at the unit's center until the cue
    // takes it off. stand-in: the billboard is as wide as the unit's model; the game's size is not known.
    void ShowEffect(PlacedObject unit)
    {
        if (
            _art.Sprite(TransportCue.EffectSprite) is not { } art
            || Texture(art.Sprite.Texture) is not { } tex
            || UnitNode(unit) is not { } node
        )
            return;
        var material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoTexture = tex,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
        };
        SpriteMaterial.Blend(material, art.Sprite.Material);
        var width = Math.Max(_world.State(unit)?.ModelRadius ?? 0, 1f) * 2;
        var quad = new MeshInstance3D
        {
            Mesh = new QuadMesh { Size = new Vector2(width, width), Material = material },
        };
        node.AddChild(quad);
        var length = art.FrameSeconds > 0 ? art.FrameSeconds * art.Frames.Count : 1;
        var tween = quad.CreateTween().SetLoops();
        tween.TweenMethod(
            Callable.From<double>(age => SpriteMaterial.ShowFrame(material, art.Sprite, art.FrameAt(age), tex)),
            0.0,
            length,
            length
        );
        _effects[unit] = quad;
    }

    /// <summary>
    /// Logs the count of repeated presentation and shield-down overlay faults. Godot exits the children of a node before the node, so this
    /// runs before the run-end entry of the AssetBrowserView above the sandbox.
    /// </summary>
    public override void _ExitTree() => _faults.Flush();

    // A unit's place in the world; null when its node is gone, so nothing is drawn or sounded there.
    Vector3? At(PlacedObject unit) =>
        _nodes.TryGetValue(unit, out var node) && IsInstanceValid(node) ? node.GlobalPosition : null;

    // The unit's own node; null when it is gone.
    Node3D? UnitNode(PlacedObject unit) =>
        _nodes.TryGetValue(unit, out var node) && IsInstanceValid(node) ? node : null;

    // The node a shot leaves from: the hardpoint's node, or the unit's own node when the model lacks the hardpoint
    // (reported when built); null when the unit's node is gone.
    Node3D? Source(PlacedObject unit, string name)
    {
        if (UnitNode(unit) is not { } node)
            return null;

        return _art.Hardpoint(unit, name) is { } exact && node.FindChild(exact, true, false) is Node3D hardpoint
            ? hardpoint
            : node;
    }

    static System.Numerics.Vector3 Num(Vector3 v) => new(v.X, v.Y, v.Z);

    static Vector3 Gd(System.Numerics.Vector3 v) => new(v.X, v.Y, v.Z);

    // Plays the sound at the level the game's fade gives for its distance from the camera eye, positional so it pans;
    // Godot's own distance fade, cutoff, and filter apply nothing. A sound that starts beyond its maximum distance is
    // never played. A fire sound follows its unit (`follows`) while the unit's node lives; UpdateSounds keeps the level.
    void PlaySound(string name, Vector3 at, double minimumScale, PlacedObject? follows)
    {
        if (!_sounds.TryGetValue(name, out var stream)) // only a sound the install holds and Godot decodes is read
            return;
        if (GetViewport().GetCamera3D() is not { } camera)
            return;
        if (!_fade.StartsAt(camera.GlobalPosition.DistanceTo(at)))
            return;
        var player = new AudioStreamPlayer3D
        {
            Stream = stream,
            Position = at,
            VolumeDb = Volume(_fade.VolumeDbAt(Num(at), Num(camera.GlobalPosition), minimumScale)),
            MaxDb = 6, // Godot's highest cap, above the lift
            AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.Disabled,
            MaxDistance = 0,
            AttenuationFilterDb = 0,
        };
        player.Finished += player.QueueFree;
        AddChild(player);
        player.Play();
        _sounding.Add(
            new Sounding
            {
                Player = player,
                Follows = follows,
                MinimumScale = minimumScale,
            }
        );
    }

    // Each frame, every playing sound moves with the unit it follows (it stays at its last place once the unit's node is
    // gone, 0x5b09e0) and takes the fade's level at its distance from the camera eye, silent beyond its maximum.
    void UpdateSounds()
    {
        _sounding.RemoveAll(s => !IsInstanceValid(s.Player) || s.Player.IsQueuedForDeletion());
        if (GetViewport().GetCamera3D() is not { } camera)
            return;
        foreach (var s in _sounding)
        {
            if (s.Follows != null && At(s.Follows) is { } at)
            {
                s.Player.Position = at;
            }

            s.Player.VolumeDb = Volume(
                _fade.VolumeDbAt(Num(s.Player.Position), Num(camera.GlobalPosition), s.MinimumScale)
            );
        }
    }

    // The player's volume for a fade level: the level with the pan lift, or the silent level as it stands.
    static float Volume(double db) => (float)(db > SoundFade.SilentDb ? db + PanLiftDb : db);

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

    // A small explosion of a dying unit: its sprite's flipbook plays once on a billboard at the point; a sprite the
    // install lacks shows nothing (reported once, CombatArt.AddSmallExplosions).
    void ShowSmallExplosion(string name, Vector3 at)
    {
        if (_art.Sprite(name) is not { } art || Texture(art.Sprite.Texture) is not { } tex)
            return;
        var material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoTexture = tex,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
        };
        SpriteMaterial.Blend(material, art.Sprite.Material);
        var quad = new MeshInstance3D
        {
            Mesh = new QuadMesh { Size = new Vector2(SmallExplosionSize, SmallExplosionSize), Material = material },
            Position = at,
        };
        AddChild(quad);
        // stand-in: a sprite without a flipbook shows for half a second; the game's duration is not known.
        var length = art.FrameSeconds > 0 ? art.FrameSeconds * art.Frames.Count : 0.5;
        var tween = quad.CreateTween();
        tween.TweenMethod(
            Callable.From<double>(age => SpriteMaterial.ShowFrame(material, art.Sprite, art.FrameAt(age), tex)),
            0.0,
            length,
            length
        );
        tween.TweenCallback(Callable.From(quad.QueueFree));
    }

    // A sprite effect at a node of a unit, a repair spark or a hit explosion (0x515d50): the sprite's flipbook plays on
    // a billboard hung from the hardpoint's node, or the unit's own node for a null hardpoint, so it follows the unit,
    // for `seconds`; it goes with the unit's node. A sprite the install lacks shows nothing (reported once,
    // CombatArt.AddRepairSparks and CombatArt.AddSmallExplosions).
    void ShowAtNode(string sprite, PlacedObject unit, string? hardpoint, float size, double seconds)
    {
        if (
            _art.Sprite(sprite) is not { } art
            || Texture(art.Sprite.Texture) is not { } tex
            || (hardpoint == null ? UnitNode(unit) : Source(unit, hardpoint)) is not { } node
        )
            return;
        var material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoTexture = tex,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
        };
        SpriteMaterial.Blend(material, art.Sprite.Material);
        var width = size * 2; // the size reaches out from the center on each side (0x5975e0)
        var quad = new MeshInstance3D
        {
            Mesh = new QuadMesh { Size = new Vector2(width, width), Material = material },
        };
        node.AddChild(quad);
        var tween = quad.CreateTween();
        tween.TweenMethod(
            Callable.From<double>(age => SpriteMaterial.ShowFrame(material, art.Sprite, art.FrameAt(age), tex)),
            0.0,
            seconds,
            seconds
        );
        tween.TweenCallback(Callable.From(quad.QueueFree));
    }

    void Explode(PlacedObject unit)
    {
        if (_art.Explosion(unit) is not { } explosion || At(unit) is not { } at)
            return;
        PlaySound(explosion.Sound ?? "", at, SoundFade.ExplosionMinimumScale, null);
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
        UpdateBeams();
        UpdateSounds();
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
