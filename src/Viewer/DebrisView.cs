using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using OpenArmada.Diagnostics;
using OpenArmada.Formats;
using OpenArmada.Rendering;
using OpenArmada.Sandbox;

namespace OpenArmada.Viewer;

/// <summary>
/// Draws the combat world's debris pieces: each is its face's triangle with the material of the unit's mesh it came
/// from, placed and turned where the world says, and gone when the world drops it, never faded. The material is taken
/// from the unit's node when the piece first shows, in the frame the unit ends (the orders node frees the node after it
/// raises the step's events), so this node reads the pieces then too. The simulation is the sandbox's; this node only
/// moves triangles. A piece whose mesh the unit's node does not hold is not drawn and is logged once with a count of
/// its repeats (INV-0002, INV-0004).
/// </summary>
public partial class DebrisView : Node3D
{
    readonly CombatWorld _world;
    readonly Dictionary<PlacedObject, Node3D> _nodes;
    readonly Func<PlacedObject, Sod?> _model;
    readonly RepeatedFaults _faults = new(Area.Combat);
    readonly Dictionary<DebrisPiece, MeshInstance3D?> _drawn = new();

    /// <summary>
    /// <c>nodes</c> are the unit nodes of the scene and <c>model</c> reads a unit's model, whose mesh nodes the pieces'
    /// materials are taken from; <c>orders</c> raises the step each new piece is first seen in.
    /// </summary>
    public DebrisView(
        CombatWorld world,
        Dictionary<PlacedObject, Node3D> nodes,
        Func<PlacedObject, Sod?> model,
        UnitOrders orders
    )
    {
        _world = world;
        _nodes = nodes;
        _model = model;
        orders.Stepped += _ => Draw();
    }

    // Draws every piece the world has that this node does not yet. The mesh a piece takes its material from is looked
    // up once per unit and model node in a call: a shatter brings many pieces of each.
    void Draw()
    {
        var sources = new Dictionary<(PlacedObject, Sod.Node), MeshInstance3D?>();
        foreach (var piece in _world.Debris.Where(p => !_drawn.ContainsKey(p)).ToList())
        {
            try
            {
                var triangle = Triangle(piece, sources);
                _drawn[piece] = triangle;
                if (triangle != null)
                {
                    AddChild(triangle);
                }
            }
            catch (Exception e) // a failure of one piece's drawing never stops the fight (INV-0002)
            {
                _drawn[piece] = null;
                _faults.Report($"debris piece failed: {e.Message}");
            }
        }
    }

    // The piece's triangle with the material of its mesh's group; null when the unit's node has no such mesh. The mesh
    // instance of each unit and model node is found once and kept in `sources`.
    MeshInstance3D? Triangle(DebrisPiece piece, Dictionary<(PlacedObject, Sod.Node), MeshInstance3D?> sources)
    {
        if (
            !_nodes.TryGetValue(piece.Unit, out var root)
            || !IsInstanceValid(root)
            || _model(piece.Unit) is not { } sod
        )
            return null;
        if (!sources.TryGetValue((piece.Unit, piece.Node), out var source))
        {
            var index = sod.Nodes.ToList().IndexOf(piece.Node);
            source = root.FindChildren("*", "", true, false)
                .Prepend(root)
                .OfType<MeshInstance3D>()
                .FirstOrDefault(n =>
                    n.HasMeta(SodBuilder.SodIndexMeta) && (int)n.GetMeta(SodBuilder.SodIndexMeta) == index
                );
            sources[(piece.Unit, piece.Node)] = source;
        }

        if (source?.Mesh == null || piece.Group >= source.Mesh.GetSurfaceCount())
        {
            _faults.Report("debris piece names a mesh the unit's node lacks, it is not drawn");

            return null;
        }

        var surface = new SurfaceTool();
        surface.Begin(Mesh.PrimitiveType.Triangles);
        surface.SetMaterial(source.Mesh.SurfaceGetMaterial(piece.Group));
        for (var i = 0; i < 3; i++)
        {
            var uv = piece.TexCoords[i];
            var v = piece.Vertices[i];
            surface.SetUV(new Vector2(uv.X, uv.Y));
            surface.AddVertex(new Vector3(v.X, v.Y, v.Z));
        }

        // stand-in: the face's own flat normal; the game keeps the face's vertex normals (0x57d270), and the model's
        // data carries none (SodBuilder derives its normals too), so a piece would need the mesh's smoothed ones.
        surface.GenerateNormals();

        return new MeshInstance3D { Mesh = surface.Commit() };
    }

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        var live = _world.Debris.ToHashSet();
        foreach (var (piece, triangle) in _drawn.ToList())
        {
            if (!live.Contains(piece))
            {
                _drawn.Remove(piece);
                triangle?.QueueFree();
            }
            else if (triangle != null)
            {
                triangle.Transform = new Transform3D(
                    new Basis(Gd(piece.AxisX), Gd(piece.AxisY), Gd(piece.AxisZ)),
                    Gd(piece.Position)
                );
            }
        }
    }

    /// <summary>Logs the count of repeated faults.</summary>
    public override void _ExitTree() => _faults.Flush();

    static Vector3 Gd(System.Numerics.Vector3 v) => new(v.X, v.Y, v.Z);
}
