using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using OpenArmada.Diagnostics;
using OpenArmada.Formats;
using OpenArmada.Rendering;
using OpenArmada.Sandbox;

namespace OpenArmada.Viewer;

// The beam models of the special weapons (the Holding Beam and the Auto-Assimilator). The world decides when a weapon
// runs (SpecialWeaponStarted, UnitCombatState.SpecialOrder); this part draws the weapon's model from the firing
// hardpoint to the target for as long as the order runs, and plays its fire sound once at the start (INV-0011). A model
// or sound the install lacks is reported by the CombatArt and skipped, and the weapon works without it.
// docs/rules/combat-rules.md, "Special weapons", states the rules.
public partial class CombatView
{
    // A running special weapon's beam: the order it draws, its node, and the node it leaves from.
    sealed class BeamShown
    {
        public required SpecialWeaponOrder Order;
        public required Node3D Root;
        public required Node3D Source;
    }

    readonly Dictionary<PlacedObject, BeamShown> _beams = new(ReferenceEqualityComparer.Instance);

    // Builds the template of each beam model the install holds. A model that cannot be read is a gap and one ERROR
    // entry, and its weapon draws no beam (INV-0002, INV-0005).
    void LoadBeamModels(CombatArt art, SodBuilder builder, Action<string> gap)
    {
        foreach (var name in art.BeamModels)
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
                art.Fail("beam model", name, e.Message, "beam model cannot be read, its beam is not drawn");
            }
        }
    }

    // A special weapon started: its fire sound plays once at its unit and its beam model is drawn while its order runs.
    void StartSpecial(SpecialWeaponStarted started)
    {
        if (At(started.Unit) is { } at)
        {
            PlaySound(started.Weapon.FireSound, at, SoundFade.FireMinimumScale, started.Unit);
        }

        if (
            _world.State(started.Unit)?.SpecialOrder is not { } order
            || !ReferenceEquals(order.Weapon, started.Weapon)
            || !_models.TryGetValue(Path.GetFileNameWithoutExtension(started.Weapon.Geometry), out var template)
            || At(order.Target.Unit) is not { } target
            || BeamSource(started.Unit, started.Weapon, target) is not { } source
        )
            return;
        // the model sits in a node of its own, so the model's own transform stays as it was built
        var root = new Node3D();
        root.AddChild(template.Duplicate());
        AddChild(root);
        // an order that ends and a new one that starts within one frame leave the old beam to be freed here
        if (_beams.Remove(started.Unit, out var old))
        {
            old.Root.QueueFree();
        }

        var beam = new BeamShown
        {
            Order = order,
            Root = root,
            Source = source,
        };
        _beams[started.Unit] = beam;
        PlaceBeam(beam);
    }

    // stand-in: the beam leaves from the weapon's hardpoint nearest the target when it starts; the game's choice is not
    // traced. A weapon without a hardpoint the model holds leaves from the unit's own node.
    Node3D? BeamSource(PlacedObject unit, SpecialWeaponProfile weapon, Vector3 target)
    {
        Node3D? best = null;
        foreach (var name in weapon.Hardpoints)
        {
            if (Source(unit, name) is { } node && (best == null || Closer(node, best, target)))
            {
                best = node;
            }
        }

        return best ?? UnitNode(unit);
    }

    static bool Closer(Node3D a, Node3D b, Vector3 to) =>
        a.GlobalPosition.DistanceSquaredTo(to) < b.GlobalPosition.DistanceSquaredTo(to);

    // Puts the beam's model at its hardpoint, turned toward the target's center and scaled to span the distance
    // (0x4ddf80, 0x4da7a0). The builder mirrors the model's Z, so the model's length axis is Godot's -Z.
    void PlaceBeam(BeamShown beam)
    {
        if (At(beam.Order.Target.Unit) is not { } target || !IsInstanceValid(beam.Source))
            return;
        var start = beam.Source.GlobalPosition;
        var along = target - start;
        if (along.LengthSquared() < 1e-6f)
            return;
        var direction = along.Normalized();
        var up = Math.Abs(direction.Y) > 0.99f ? Vector3.Back : Vector3.Up;
        var weapon = beam.Order.Weapon;
        var scale = ShotGeometry.BeamModelScale(along.Length(), weapon.RadiusScale, weapon.LengthScale);
        beam.Root.Transform = new Transform3D(
            Basis.LookingAt(direction, up) * Basis.FromScale(new Vector3(scale.X, scale.Y, scale.Z)),
            start
        );
    }

    // Each frame, every beam follows its hardpoint and its target; it goes when its unit's order is no longer the one it
    // draws (the weapon ended or was ordered off) or a node is gone.
    void UpdateBeams()
    {
        foreach (var (unit, beam) in _beams.ToList())
        {
            if (
                !ReferenceEquals(_world.State(unit)?.SpecialOrder, beam.Order)
                || At(unit) is null
                || !IsInstanceValid(beam.Source)
            )
            {
                beam.Root.QueueFree();
                _beams.Remove(unit);
            }
            else
            {
                PlaceBeam(beam);
            }
        }
    }
}
