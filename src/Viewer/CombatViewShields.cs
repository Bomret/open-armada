using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using OpenArmada.Diagnostics;
using OpenArmada.Formats;
using OpenArmada.Rendering;
using OpenArmada.Sandbox;

namespace OpenArmada.Viewer;

// The shield hit effects, the hull hit sprites of beams, and the shield-down overlays of the combat view. The combat
// world decides each (ShieldHitEffect, HullHitSprite, UnitCombatState.ShieldDownOverlay); this part only draws them
// (INV-0011). A geometry or sprite the install lacks is reported by the CombatArt and skipped.
public partial class CombatView
{
    // stand-in: a hull hit sprite of size 1 is this many world units wide; the game's unit is not recovered.
    const float HullSpriteUnit = 8f;

    // A beam's effect: it follows the beam's contact point until the beam ends.
    sealed class Following
    {
        public required Shot Shot;
        public required Node3D Node;
        public required Node3D? Unit; // the unit node a shield effect is a child of; null for a hull sprite
        public SpriteArt? Art;
        public StandardMaterial3D? Material;
        public ImageTexture? Texture;
    }

    readonly List<Following> _following = new();
    readonly Dictionary<PlacedObject, Node3D> _overlays = new(ReferenceEqualityComparer.Instance);

    // A projectile's effect and the world time it ends at; the world's clock, not the frame's, so the duration holds at
    // any frame rate and step size.
    readonly List<(Node3D Node, double End)> _expiring = new();

    // Builds the templates of the shield geometries and the shield-down model the install holds.
    void LoadShieldModels(CombatArt art, SodBuilder builder, Action<string> gap)
    {
        foreach (var name in art.ShieldGeometries.Concat(art.ShieldDownModel is { } m ? new[] { m } : []))
        {
            if (_models.ContainsKey(name))
                continue;
            try
            {
                _models[name] = builder.Build(Sod.Read(_files.ReadAllBytes(name + ".sod")));
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

    // The half of the template's longest extent, so a scale of reach over it fits the model to the shield bubble.
    static float ModelRadius(Node3D template)
    {
        Aabb? box = null;
        foreach (var mesh in template.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>())
        {
            box = box is { } b ? b.Merge(mesh.GetAabb()) : mesh.GetAabb();
        }

        var radius = box is { } a ? Math.Max(a.Size.X, Math.Max(a.Size.Y, a.Size.Z)) / 2 : 0;

        // stand-in: a model without extent is drawn unscaled
        return radius > 0 ? radius : 1;
    }

    // Draws the model in one color. Every surface gets its own copy of its material, since a duplicated node shares
    // the template's materials; the model's surfaces are shader materials (SurfaceMaterial).
    static void Colorize(Node3D root, Color color)
    {
        foreach (var mesh in root.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>())
        {
            for (var i = 0; i < mesh.GetSurfaceOverrideMaterialCount(); i++)
            {
                if (mesh.GetActiveMaterial(i) is ShaderMaterial material)
                {
                    var own = (ShaderMaterial)material.Duplicate();
                    SurfaceMaterial.Colorize(own, color);
                    mesh.SetSurfaceOverrideMaterial(i, own);
                }
            }
        }
    }

    // Turns the effect so its +Z axis points along `direction` (world), scaled; the effect is a child of the unit node.
    // stand-in: which axis of the game's geometry points at the contact is not recovered.
    static void Aim(Node3D effect, Node3D unit, Vector3 direction, float scale)
    {
        var local = unit.GlobalTransform.Basis.Inverse() * direction;
        if (local.LengthSquared() < 1e-12f)
        {
            local = Vector3.Forward;
        }

        local = local.Normalized();
        var up = Math.Abs(local.Y) > 0.999f ? Vector3.Right : Vector3.Up;
        effect.Basis = Basis.LookingAt(-local, up).Scaled(Vector3.One * scale);
    }

    static float ShieldScale(Node3D template, UnitCombatState target) =>
        target.Radius > 0 ? target.Radius * CombatWorld.BubbleRadii / ModelRadius(template) : 1;

    void RaiseShieldEffect(ShieldHitEffect effect)
    {
        if (!_models.TryGetValue(effect.Geometry, out var template))
            return;
        if (_world.State(effect.Unit) is not { } target || !_nodes.TryGetValue(effect.Unit, out var unit))
            return;
        if (!IsInstanceValid(unit))
            return;
        var node = (Node3D)template.Duplicate();
        Colorize(node, new Color((float)(1 - effect.Fraction), (float)effect.Fraction, 0));
        unit.AddChild(node);
        var scale = ShieldScale(template, target);
        Aim(node, unit, Gd(effect.Contact) - unit.GlobalPosition, scale);
        if (effect.FollowsBeam)
        {
            _following.Add(
                new Following
                {
                    Shot = effect.Shot,
                    Node = node,
                    Unit = unit,
                }
            );

            return;
        }

        _expiring.Add((node, effect.Raised + effect.Duration));
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
            Unit = null,
            Art = art,
            Material = material,
            Texture = tex,
        };
        _following.Add(following);
        PlaceFollowing(following);
    }

    // Moves a beam's effect to the beam's current contact point; false when the beam or its target is gone.
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
        if (f.Unit is { } unit)
        {
            if (!IsInstanceValid(unit))
                return false;
            Aim(f.Node, unit, toward, f.Node.Basis.Scale.X);

            return true;
        }

        // the hull hit sprite sits on the hull, not on the shield bubble
        var radius = f.Shot.Target.Radius;
        f.Node.Position = center + (toward.LengthSquared() < 1e-12f ? Vector3.Zero : toward.Normalized() * radius);
        SpriteMaterial.ShowFrame(f.Material!, f.Art!.Sprite, f.Art.FrameAt(f.Shot.Age), f.Texture!);

        return true;
    }

    // Per frame: moves the beams' effects, drops the ones of beams that ended, and shows or removes the shield-down
    // overlay of each unit as its state says. A failure of one unit's overlay skips only that unit and is reported
    // through the view's repeated faults.
    void UpdateShieldEffects()
    {
        _expiring.RemoveAll(e =>
        {
            var gone = !IsInstanceValid(e.Node) || _world.Time >= e.End;
            if (gone && IsInstanceValid(e.Node))
            {
                e.Node.QueueFree();
            }

            return gone;
        });

        var live = _world.Shots.ToHashSet();
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
        // white and centered; the overlay stays until the state says otherwise
        var node = (Node3D)template.Duplicate();
        try
        {
            Colorize(node, Colors.White);
            node.Basis = Basis.Identity.Scaled(Vector3.One * ShieldScale(template, state));
        }
        catch
        {
            // the duplicate is in no tree yet: free it here, or each frame's retry would leak one node
            node.Free();
            throw;
        }

        unit.AddChild(node);
        _overlays[state.Unit] = node;
    }
}
