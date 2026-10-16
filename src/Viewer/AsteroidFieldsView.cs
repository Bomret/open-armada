using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using OpenArmada.Assets;
using OpenArmada.Formats;
using OpenArmada.Rendering;
using OpenArmada.Sandbox;

namespace OpenArmada.Viewer;

// The rocks of every asteroid field of the plan, spinning and circling by elapsed time. Each of the eight asteroid
// models is built once; each of its visible meshes is one MultiMeshInstance3D drawing all rocks of that model in
// the map, so the draw count follows the models and not the rocks (4mama holds up to 4,800 rocks).
public partial class AsteroidFieldsView : Node3D
{
    // One draw: a mesh of a model, the rocks that show it, and the buffer of their transforms.
    sealed record Draw(MultiMesh Multi, Transform3D Part, List<Rock> Rocks, float[] Buffer);

    readonly List<Draw> _draws = new();
    ulong _start;

    public AsteroidFieldsView(GameFileSystem files, SandboxPlan plan, Action<string> gap)
    {
        if (plan.Fields.Count == 0) return;
        var builder = new SodBuilder(files);
        var parts = new Dictionary<string, List<(Mesh Mesh, Transform3D Transform)>>(StringComparer.OrdinalIgnoreCase);
        var extent = 0f;
        foreach (var model in plan.Fields.SelectMany(f => f.Models).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var node = builder.Build(Sod.Read(files.ReadAllBytes(model + ".sod")));
                foreach (var problem in builder.Problems) gap($"{model}: {problem}");
                var found = new List<(Mesh, Transform3D)>();
                Collect(node, node, Transform3D.Identity, found);
                node.Free();
                parts[model] = found;
                foreach (var (mesh, t) in found)
                {
                    var box = t * mesh.GetAabb();
                    foreach (var i in Enumerable.Range(0, 8)) extent = MathF.Max(extent, box.GetEndpoint(i).Length());
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
            if (models.Count == 0) continue;
            foreach (var rock in (field with { Models = models }).Rocks(extent))
            {
                if (!byModel.TryGetValue(rock.Model, out var list)) byModel[rock.Model] = list = new List<Rock>();
                list.Add(rock);
            }
        }
        foreach (var (model, rocks) in byModel)
            foreach (var (mesh, transform) in parts[model])
            {
                var multi = new MultiMesh
                {
                    TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                    Mesh = mesh,
                    InstanceCount = rocks.Count,
                    CustomAabb = new Aabb(new Vector3(-1e5f, -1e5f, -1e5f), new Vector3(2e5f, 2e5f, 2e5f)), // rocks move, so never culled by a stale box
                };
                AddChild(new MultiMeshInstance3D { Multimesh = multi });
                _draws.Add(new Draw(multi, transform, rocks, new float[rocks.Count * 12]));
            }
        _start = Time.GetTicksMsec();    }

    // The visible meshes of the model with their transforms in the root's frame.
    static void Collect(Node3D root, Node3D node, Transform3D transform, List<(Mesh, Transform3D)> into)
    {
        if (!node.Visible) return;
        if (node != root) transform *= node.Transform;
        if (node is MeshInstance3D m && m.Mesh != null) into.Add((m.Mesh, transform));
        foreach (var child in node.GetChildren().OfType<Node3D>()) Collect(root, child, transform, into);
    }

    public override void _Process(double delta)
    {
        if (_draws.Count == 0) return;
        var t = (Time.GetTicksMsec() - _start) / 1000.0;
        var poses = new Dictionary<List<Rock>, Transform3D[]>();
        foreach (var draw in _draws)
        {
            if (!poses.TryGetValue(draw.Rocks, out var pose))
            {
                pose = new Transform3D[draw.Rocks.Count];
                for (var i = 0; i < pose.Length; i++)
                {
                    var (position, q) = draw.Rocks[i].Pose(t);
                    pose[i] = new Transform3D(new Basis(new Quaternion(q.X, q.Y, q.Z, q.W)), new Vector3(position.X, position.Y, position.Z));
                }
                poses[draw.Rocks] = pose;
            }
            for (var i = 0; i < pose.Length; i++)
            {
                var m = pose[i] * draw.Part;
                var b = draw.Buffer;
                var o = i * 12;
                b[o] = m.Basis.Row0.X; b[o + 1] = m.Basis.Row0.Y; b[o + 2] = m.Basis.Row0.Z; b[o + 3] = m.Origin.X;
                b[o + 4] = m.Basis.Row1.X; b[o + 5] = m.Basis.Row1.Y; b[o + 6] = m.Basis.Row1.Z; b[o + 7] = m.Origin.Y;
                b[o + 8] = m.Basis.Row2.X; b[o + 9] = m.Basis.Row2.Y; b[o + 10] = m.Basis.Row2.Z; b[o + 11] = m.Origin.Z;
            }
            draw.Multi.Buffer = draw.Buffer;
        }
    }
}
