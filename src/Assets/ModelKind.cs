using System.Linq;
using System.Collections.Generic;
using OpenArmada.Formats;

namespace OpenArmada.Assets;

// Models: the .sod files of the SOD folder and its software-renderer copies.
public sealed class ModelKind : AssetKind
{
    public const string KindName = "models";

    public override string Name => KindName;

    public override IEnumerable<string> Enumerate(GameFileSystem files) => files.List(".sod", "SOD", "SOD/Software");

    public override object Load(GameFileSystem files, string name) => Sod.Read(files.ReadAllBytes(name));

    // The textures its meshes name and the animations its references name that the install lacks.
    public override IReadOnlyList<string> Unresolved(GameFileSystem files, string name)
    {
        var sod = (Sod)Load(files, name);
        var textures = sod.Nodes.Where(n => n.Mesh != null).Select(n => ModelRules.Look(sod, n.Mesh!).Texture);
        var animations = ModelRules.Analyze(files, sod).MissingAnimations.Select(m => m.Animation);
        return AssetReferences.Missing(textures, n => AssetReferences.HasTexture(files, n))
            .Concat(AssetReferences.Missing(animations, _ => false)).ToList();
    }

    // What the model holds that the engine cannot draw as the data says (ModelRules.Check): notes, no failure.
    // A missing animation is left out; Unresolved lists it.
    public override IReadOnlyList<string> Check(GameFileSystem files, string name)
    {
        var analysis = ModelRules.Analyze(files, (Sod)Load(files, name));
        // One occurrence per missing animation, so an unrelated line with the same text stays.
        var lines = analysis.Problems.ToList();
        foreach (var m in analysis.MissingAnimations)
            if (m.Problem != null) lines.Remove(m.Problem);
        return lines;
    }
}
