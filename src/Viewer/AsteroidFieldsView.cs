using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using OpenArmada.Assets;
using OpenArmada.Formats;
using OpenArmada.Rendering;
using OpenArmada.Sandbox;

namespace OpenArmada.Viewer;

/// <summary>
/// The rocks of every asteroid field of the plan, spinning and circling by elapsed time. Each of the eight asteroid
/// models is built once; each of its visible meshes is one MultiMeshInstance3D drawing all rocks of that model in
/// the map, so the draw count follows the models and not the rocks (4mama holds up to 4,800 rocks).
/// </summary>
public partial class AsteroidFieldsView : Node3D
{
    // One draw: a mesh of a model, the rocks that show it, and the buffer of their transforms.
    sealed record Draw
    {
        public required MultiMesh Multi { get; init; }
        public required Transform3D Part { get; init; }
        public required Transform3D[] Pose { get; init; }
        public required float[] Buffer { get; init; }
    }

    // The rocks of one model and their poses at the current time; every draw of the model reads the poses.
    sealed record Group
    {
        public required List<Rock> Rocks { get; init; }
        public required Transform3D[] Pose { get; init; }
    }

    readonly List<Draw> _draws = new();
    readonly List<Group> _groups = new();
    ulong _start;

    /// <summary>
    /// Builds each asteroid model of the plan's fields from <c>files</c> and adds one MultiMeshInstance3D child per
    /// visible mesh of a model. Each problem of a built model, and each model that cannot be read, goes to
    /// <c>gap</c> as one line; the rocks of a model that cannot be read are left out. A plan without fields adds
    /// nothing.
    /// </summary>
    public AsteroidFieldsView(GameFileSystem files, SandboxPlan plan, Action<string> gap)
    {
        if (plan.Fields.Count == 0)
            return;
        var builder = new SodBuilder(files);
        var parts = new Dictionary<string, List<(Mesh Mesh, Transform3D Transform)>>(StringComparer.OrdinalIgnoreCase);
        var extent = 0f;
        foreach (var model in plan.Fields.SelectMany(f => f.Models).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var node = builder.Build(Sod.Read(files.ReadAllBytes(model + ".sod")));
                foreach (var problem in builder.Problems)
                {
                    gap($"{model}: {problem}");
                }

                var found = VisibleMeshes.Of(node);
                node.Free();
                parts[model] = found;
                foreach (var (mesh, t) in found)
                {
                    var box = t * mesh.GetAabb();
                    foreach (var i in Enumerable.Range(0, 8))
                    {
                        extent = MathF.Max(extent, box.GetEndpoint(i).Length());
                    }
                }
            }
            catch (Exception e) // a model that cannot be read is a gap and its rocks are left out (INV-0002)
            {
                gap($"model '{model}' cannot be read: {e.Message}");
            }
        }

        var byModel = new Dictionary<string, List<Rock>>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in plan.Fields)
        {
            var models = field.Models.Where(parts.ContainsKey).ToList();
            if (models.Count == 0)
                continue;
            foreach (var rock in (field with { Models = models }).Rocks(extent))
            {
                if (!byModel.TryGetValue(rock.Model, out var list))
                {
                    byModel[rock.Model] = list = new List<Rock>();
                }

                list.Add(rock);
            }
        }

        // Rocks move within their field's sphere and reach past it by their extent, so the box of every draw is the union of the fields' draw boxes.
        Aabb? bounds = null;
        foreach (var f in plan.Fields)
        {
            var (min, max) = f.DrawBox(extent);
            var box = new Aabb(
                new Vector3(min.X, min.Y, min.Z),
                new Vector3(max.X - min.X, max.Y - min.Y, max.Z - min.Z)
            );
            bounds = bounds?.Merge(box) ?? box;
        }

        foreach (var (model, rocks) in byModel)
        {
            var group = new Group { Rocks = rocks, Pose = new Transform3D[rocks.Count] };
            _groups.Add(group);
            foreach (var (mesh, transform) in parts[model])
            {
                var multi = new MultiMesh
                {
                    TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                    Mesh = mesh,
                    InstanceCount = rocks.Count,
                    CustomAabb = bounds!.Value,
                };
                AddChild(new MultiMeshInstance3D { Multimesh = multi });
                _draws.Add(
                    new Draw
                    {
                        Multi = multi,
                        Part = transform,
                        Pose = group.Pose,
                        Buffer = new float[rocks.Count * 12],
                    }
                );
            }
        }

        _start = Time.GetTicksMsec();
    }

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        if (_draws.Count == 0)
            return;
        var t = (Time.GetTicksMsec() - _start) / 1000.0;
        foreach (var group in _groups)
        {
            for (var i = 0; i < group.Pose.Length; i++)
            {
                var (position, q) = group.Rocks[i].Pose(t);
                group.Pose[i] = new Transform3D(
                    new Basis(new Quaternion(q.X, q.Y, q.Z, q.W)),
                    new Vector3(position.X, position.Y, position.Z)
                );
            }
        }

        foreach (var draw in _draws)
        {
            for (var i = 0; i < draw.Pose.Length; i++)
            {
                var m = draw.Pose[i] * draw.Part;
                var b = draw.Buffer;
                var o = i * 12;
                b[o] = m.Basis.Row0.X;
                b[o + 1] = m.Basis.Row0.Y;
                b[o + 2] = m.Basis.Row0.Z;
                b[o + 3] = m.Origin.X;
                b[o + 4] = m.Basis.Row1.X;
                b[o + 5] = m.Basis.Row1.Y;
                b[o + 6] = m.Basis.Row1.Z;
                b[o + 7] = m.Origin.Y;
                b[o + 8] = m.Basis.Row2.X;
                b[o + 9] = m.Basis.Row2.Y;
                b[o + 10] = m.Basis.Row2.Z;
                b[o + 11] = m.Origin.Z;
            }

            draw.Multi.Buffer = draw.Buffer;
        }
    }
}
