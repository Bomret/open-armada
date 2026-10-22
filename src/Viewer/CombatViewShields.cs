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

// The shield hit effects, the hull hit sprites of beams, and the shield-down overlays of the combat view. The combat
// world decides each (ShieldHitEffect, HullHitSprite, UnitCombatState.ShieldDownOverlay); this part only draws them
// (INV-0011). A geometry or sprite the install lacks is reported by the CombatArt and skipped. docs/rules/
// shield-effects.md states the rules.
public partial class CombatView
{
    // stand-in: a hull hit sprite of size 1 is this many world units wide; the game's unit is not recovered.
    const float HullSpriteUnit = 8f;

    // A beam's hull hit sprite: it follows the beam's contact point until the beam ends.
    sealed class Following
    {
        public required Shot Shot;
        public required Node3D Node;
        public required SpriteArt Art;
        public required StandardMaterial3D Material;
        public required ImageTexture Texture;
    }

    // A shield effect: its node, the world time it ends at (the world's clock, not the frame's, so the duration holds
    // at any frame rate and step size), its scale and centre, and, for a beam's, the beam and the contact direction it
    // was last turned to.
    sealed class ShieldShown
    {
        public required Node3D Node;
        public required double End;
        public required System.Numerics.Vector3 Scale;
        public required System.Numerics.Vector3 Center;
        public Shot? Beam;
        public System.Numerics.Vector3 Direction;
    }

    readonly List<Following> _following = new();
    readonly List<ShieldShown> _shields = new();
    readonly Dictionary<PlacedObject, Node3D> _overlays = new(ReferenceEqualityComparer.Instance);

    // Per shield geometry: the flipbook of each mesh node, by the node's index in the model (SodBuilder.SodIndexMeta).
    readonly Dictionary<string, Dictionary<int, TextureAnimation>> _flipbooks = new(StringComparer.OrdinalIgnoreCase);

    // The flipbook materials of each shown shield model; their offsets follow the world's clock.
    readonly List<(Node3D Node, List<(ShaderMaterial Material, TextureAnimation Flipbook)> Surfaces)> _flipping = new();

    // Builds the templates of the shield geometries and the shield-down model the install holds.
    void LoadShieldModels(CombatArt art, SodBuilder builder, Action<string> gap)
    {
        foreach (var name in art.ShieldGeometries.Concat(art.ShieldDownModel is { } m ? new[] { m } : []))
        {
            if (_models.ContainsKey(name))
                continue;
            try
            {
                var sod = Sod.Read(_files.ReadAllBytes(name + ".sod"));
                _models[name] = builder.Build(sod);
                _flipbooks[name] = ModelRules
                    .Analyze(_files, sod)
                    .Runs.Where(r => sod.IndexOf(r.Node) >= 0)
                    .GroupBy(r => sod.IndexOf(r.Node))
                    .ToDictionary(g => g.Key, g => g.First().Flipbook);
                foreach (var problem in builder.Problems)
                {
                    Log.Warn(Area.Combat, $"{name}: {problem}");
                    gap($"{name}: {problem}");
                }

                foreach (var failure in builder.TextureFailures)
                {
                    gap($"{name}: {failure}");
                }
            }
            catch (Exception e) // a model that cannot be read is a gap, whatever the reason (INV-0002)
            {
                art.Fail("shield model", name, e.Message, "shield model cannot be read, it is not shown");
            }
        }
    }

    // Draws the model in one color, unlit (SurfaceMaterial.Colorize). Every surface gets its own copy of its material,
    // since a duplicated node shares the template's materials; the model's surfaces are shader materials
    // (SurfaceMaterial). The copies of the surfaces that run a flipbook follow the world's clock from here on.
    void Colorize(Node3D root, string geometry, Color color)
    {
        var flipbooks = _flipbooks.GetValueOrDefault(geometry);
        var surfaces = new List<(ShaderMaterial, TextureAnimation)>();
        var meshes = root.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>();
        if (root is MeshInstance3D own)
        {
            meshes = meshes.Prepend(own);
        }

        foreach (var mesh in meshes)
        {
            var flipbook =
                flipbooks != null && mesh.HasMeta(SodBuilder.SodIndexMeta)
                    ? flipbooks.GetValueOrDefault((int)mesh.GetMeta(SodBuilder.SodIndexMeta))
                    : null;
            for (var i = 0; i < mesh.GetSurfaceOverrideMaterialCount(); i++)
            {
                if (mesh.GetActiveMaterial(i) is ShaderMaterial material)
                {
                    var copy = (ShaderMaterial)material.Duplicate();
                    SurfaceMaterial.Colorize(copy, color);
                    mesh.SetSurfaceOverrideMaterial(i, copy);
                    if (flipbook != null)
                    {
                        surfaces.Add((copy, flipbook));
                    }
                }
            }
        }

        if (surfaces.Count > 0)
        {
            _flipping.Add((root, surfaces));
            Flip(surfaces);
        }
    }

    // The flipbook frame of the world's clock (TextureAnimation.UvOffset): the game times a shield effect's flipbook by
    // its game clock from a start of 0 (0x58cc3e reads node +0x14, which 0x516540 never sets), so a new effect shows
    // the frame the clock is at, not the first.
    void Flip(List<(ShaderMaterial Material, TextureAnimation Flipbook)> surfaces)
    {
        foreach (var (material, flipbook) in surfaces)
        {
            var o = flipbook.UvOffset(_world.Time);
            material.SetShaderParameter("uv_offset", new Vector2(o.X, o.Y));
        }
    }

    // The orientation the game builds from a contact direction u (0x55d840): rows a, b, u, with a the normalized
    // (u.z, 0, −u.x), or the X axis when u lies near the Y axis (u.x² + u.z² below 0.02, and then a is not made square
    // to u), and b = u × a. The effect's own +Z axis points along u.
    static (System.Numerics.Vector3 A, System.Numerics.Vector3 B, System.Numerics.Vector3 U) Rows(
        System.Numerics.Vector3 u
    )
    {
        u = System.Numerics.Vector3.Normalize(u);
        var a =
            u.X * u.X + u.Z * u.Z < 0.02f
                ? System.Numerics.Vector3.UnitX
                : System.Numerics.Vector3.Normalize(new System.Numerics.Vector3(u.Z, 0, -u.X));

        return (a, System.Numerics.Vector3.Cross(u, a), u);
    }

    // Places a shield effect in its unit's model (0x516540, 0x516c40): a point v of the geometry goes to
    // (v.x·a + v.y·b + v.z·u) ⊙ scale + center in the unit's SOD coordinates, with scale the ellipsoid's radii over the
    // geometry's bounding radius and center the ellipsoid's. The node's transform is that map in Godot's frame, Z
    // mirrored as SodBuilder.ToGodot mirrors it: S·M·S with S = diag(1, 1, −1).
    static void Place(
        Node3D node,
        System.Numerics.Vector3 direction,
        System.Numerics.Vector3 scale,
        System.Numerics.Vector3 center
    )
    {
        var (a, b, u) = Rows(direction);
        // the images of Godot's X, Y, and Z axes
        var x = Mirror(a * scale);
        var y = Mirror(b * scale);
        var z = -Mirror(u * scale);
        node.Transform = new Transform3D(new Basis(x, y, z), Mirror(center));
    }

    static Vector3 Mirror(System.Numerics.Vector3 v) => new(v.X, v.Y, -v.Z);

    void RaiseShieldEffect(ShieldHitEffect effect)
    {
        if (!_models.TryGetValue(effect.Geometry, out var template))
            return;
        if (_world.State(effect.Unit) is not { Shield: { } shield } || !_nodes.TryGetValue(effect.Unit, out var unit))
            return;
        var radius = _art.ShieldGeometryRadius(effect.Geometry);
        if (!IsInstanceValid(unit) || radius <= 0)
            return;
        var node = (Node3D)template.Duplicate();
        unit.AddChild(node);
        var shown = new ShieldShown
        {
            Node = node,
            End = effect.Raised + effect.Duration,
            Scale = shield.Radii / radius,
            Center = shield.Center,
            Beam = effect.FollowsBeam ? effect.Shot : null,
            Direction = effect.Direction,
        };
        Place(node, shown.Direction, shown.Scale, shown.Center);
        Colorize(node, effect.Geometry, new Color((float)(1 - effect.Fraction), (float)effect.Fraction, 0));
        _shields.Add(shown);
    }

    void RaiseHullSprite(HullHitSprite sprite)
    {
        if (_art.Sprite(HullHitSprite.SpriteName) is not { } art || Texture(art.Sprite.Texture) is not { } tex)
            return;
        var material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoTexture = tex,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
        };
        SpriteMaterial.Blend(material, art.Sprite.Material);
        var size = sprite.Scale * HullSpriteUnit;
        var node = new MeshInstance3D
        {
            Mesh = new QuadMesh { Size = new Vector2(size, size), Material = material },
        };
        AddChild(node);
        var following = new Following
        {
            Shot = sprite.Shot,
            Node = node,
            Art = art,
            Material = material,
            Texture = tex,
        };
        _following.Add(following);
        PlaceFollowing(following);
    }

    // Moves a beam's hull hit sprite to the beam's current contact point; false when the beam or its target is gone.
    bool PlaceFollowing(Following f)
    {
        if (!IsInstanceValid(f.Node) || At(f.Shot.Target.Unit) is not { } center)
            return false;
        // toward the hardpoint the beam draws from now (Place), not the one it fired from
        var origin =
            _drawn.Find(d => ReferenceEquals(d.Shot, f.Shot)) is { } drawn && IsInstanceValid(drawn.Source)
                ? drawn.Source.GlobalPosition
                : Gd(f.Shot.Position);
        var toward = origin - center;
        // the hull hit sprite sits on the hull, not on the shield
        var radius = f.Shot.Target.Radius;
        f.Node.Position = center + (toward.LengthSquared() < 1e-12f ? Vector3.Zero : toward.Normalized() * radius);
        SpriteMaterial.ShowFrame(f.Material, f.Art.Sprite, f.Art.FrameAt(f.Shot.Age), f.Texture);

        return true;
    }

    // Per frame: ends the shield effects whose time is up (0x516fa0), turns a live beam's effect to the beam's latest
    // contact (0x516c40), steps the flipbooks, moves the beams' hull hit sprites and drops those of beams that ended,
    // and shows or removes the shield-down overlay of each unit as its state says. A failure of one unit's overlay
    // skips only that unit and is reported through the view's repeated faults.
    void UpdateShieldEffects()
    {
        var live = _world.Shots.ToHashSet();
        _shields.RemoveAll(e =>
        {
            if (!IsInstanceValid(e.Node) || _world.Time >= e.End)
            {
                if (IsInstanceValid(e.Node))
                {
                    e.Node.QueueFree();
                }

                return true;
            }

            if (e.Beam is { ShieldDirection: { } latest } beam && live.Contains(beam) && latest != e.Direction)
            {
                e.Direction = latest;
                Place(e.Node, e.Direction, e.Scale, e.Center);
            }

            return false;
        });

        _flipping.RemoveAll(f => !IsInstanceValid(f.Node));
        foreach (var (_, surfaces) in _flipping)
        {
            Flip(surfaces);
        }

        _following.RemoveAll(f =>
        {
            var gone = !live.Contains(f.Shot) || !PlaceFollowing(f);
            if (gone && IsInstanceValid(f.Node))
            {
                f.Node.QueueFree();
            }

            return gone;
        });

        var present = new HashSet<PlacedObject>(ReferenceEqualityComparer.Instance);
        foreach (var state in _world.Units)
        {
            present.Add(state.Unit);
            try
            {
                var shown = _overlays.TryGetValue(state.Unit, out var overlay) && IsInstanceValid(overlay);
                if (state.ShieldDownOverlay && !shown)
                {
                    AddOverlay(state);
                }
                else if (!state.ShieldDownOverlay && shown)
                {
                    overlay!.QueueFree();
                    _overlays.Remove(state.Unit);
                }
            }
            catch (Exception e) // a failure of one unit's overlay never stops the others or the cleanup (INV-0002)
            {
                _faults.Report($"shield-down overlay failed: {e.Message}");
            }
        }

        foreach (var gone in _overlays.Keys.Where(u => !present.Contains(u)).ToList())
        {
            if (IsInstanceValid(_overlays[gone]))
            {
                _overlays[gone].QueueFree();
            }

            _overlays.Remove(gone);
        }
    }

    void AddOverlay(UnitCombatState state)
    {
        if (_art.ShieldDownModel is not { } name || !_models.TryGetValue(name, out var template))
            return;
        if (!_nodes.TryGetValue(state.Unit, out var unit) || !IsInstanceValid(unit))
            return;
        var radius = _art.ShieldGeometryRadius(name);
        if (state.Shield is not { } shield || radius <= 0)
            return;
        // white, on the shield ellipsoid; the overlay stays until the state says otherwise
        // inferred: the overlay's spawns pass the identity orientation (0x68de08), so its +Z is the model's +Z
        var node = (Node3D)template.Duplicate();
        try
        {
            Place(node, System.Numerics.Vector3.UnitZ, shield.Radii / radius, shield.Center);
            unit.AddChild(node);
            Colorize(node, name, Colors.White);
        }
        catch
        {
            // a node that failed before it joined the tree is freed here, or each frame's retry would leak one node
            if (node.GetParent() == null)
            {
                node.Free();
            }

            throw;
        }

        _overlays[state.Unit] = node;
    }
}
