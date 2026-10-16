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
        var animations = sod.References.Where(r => r.Type == ModelRules.AnimationReferenceType).Select(r => r.Animation);
        return AssetReferences.Missing(textures, n => AssetReferences.HasTexture(files, n))
            .Concat(AssetReferences.Missing(animations, n => ModelRules.FindAnimation(files, n) != null)).ToList();
    }

    // What the model holds that the engine cannot draw as the data says (ModelRules.Check): notes, no failure.
    // A missing animation is left out; Unresolved lists it.
    public override IReadOnlyList<string> Check(GameFileSystem files, string name) =>
        ModelRules.Check(files, (Sod)Load(files, name)).Where(p => !p.EndsWith(ModelRules.InNoSpriteTable)).ToList();
}
