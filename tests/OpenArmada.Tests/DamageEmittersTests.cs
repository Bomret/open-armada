using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenArmada.Assets;
using OpenArmada.Formats;
using OpenArmada.Sandbox;

namespace OpenArmada.Tests;

// The damage emitters of a model, resolved against sprite tables of an install built in code (INV-0001).
[TestClass]
public class DamageEmittersTests
{
    readonly TempInstall temp = new();

    [TestCleanup]
    public void CleanUp() => temp.CleanUp();

    // Shaped like the install's emitter.spr.
    const string EmitterTable =
        "sprite_table\n@reference=128\n@tmaterial=additive\nplasma xsmoke 0 0 32 32\n"
        + "@sprite_node ventplas plasma const (2,2) (1,1,1) billboard\n"
        + "@emitter plasmasml\ntype=spray\nsprite=ventplas\nexpansion_rate=1.1\nfiring_period=0.1\nlifespan=2.999\nvelocity=8.0\nspread_angle=5.0\n@end_emitter\n"
        + "@emitter nosprite\ntype=spray\nsprite=nosuchnode\nfiring_period=0.1\nlifespan=1\nvelocity=1\n@end_emitter\n"
        + "@emitter plasmacloud\ntype=cloud\nsprite=ventplas\n@end_emitter\n";

    GameFileSystem Install() => new(temp.MakeInstall(new[] { ("Sprites/emitter.spr", EmitterTable) }));

    static Sod.Node Node(string name, string parent, string emitter = "") =>
        new()
        {
            Name = name,
            Parent = parent,
            Type = emitter.Length > 0 ? Sod.NodeType.Emitter : Sod.NodeType.Null,
            Emitter = emitter,
        };

    static Sod Model(params Sod.Node[] nodes) =>
        new()
        {
            Version = 1.8f,
            Nodes = [Node("root", ""), Node("damage", "root"), Node("engines", "damage"), .. nodes],
        };

    [TestMethod]
    public void AnEmitterNodeUnderTheDamageGroupResolvesToItsDefinitionAndSprite()
    {
        var model = Model(Node("plasmasml_1", "engines", "plasmasml"));
        var resolved = DamageEmitters.For(Install(), model);
        var emitter = resolved.Emitters.Single();
        Assert.AreSame(model.Nodes[3], emitter.Node);
        Assert.AreEqual(8.0, emitter.Definition.Velocity);
        Assert.AreEqual((2f, 2f, "ventplas"), (emitter.Sprite.Width, emitter.Sprite.Height, emitter.Definition.Sprite));
        Assert.AreEqual(0, resolved.Reports.Count);
    }

    [TestMethod]
    public void AnEmitterNodeOutsideTheDamageGroupIsNotADamageEmitter()
    {
        var model = Model(Node("elsewhere", "root", "plasmasml"));
        Assert.AreEqual(0, DamageEmitters.For(Install(), model).Emitters.Count);
    }

    [TestMethod]
    public void AMissingEmitterDefinitionIsReportedOnceAndTheOthersStillResolve()
    {
        var model = Model(
            Node("ghost_1", "engines", "ghost"),
            Node("ghost_2", "engines", "ghost"),
            Node("plasmasml_1", "engines", "plasmasml")
        );
        var resolved = DamageEmitters.For(Install(), model);
        Assert.AreEqual(1, resolved.Emitters.Count);
        var report = resolved.Reports.Single();
        CollectionAssert.AreEqual(new[] { "ghost" }, report.Names.ToArray());
        Assert.AreEqual(2, report.Count);
    }

    [TestMethod]
    public void AMissingSpriteNodeIsReportedOnceAndTheEmitterIsSkipped()
    {
        var model = Model(Node("a_1", "engines", "nosprite"), Node("a_2", "engines", "nosprite"));
        var resolved = DamageEmitters.For(Install(), model);
        Assert.AreEqual(0, resolved.Emitters.Count);
        var report = resolved.Reports.Single();
        CollectionAssert.AreEqual(new[] { "nosuchnode" }, report.Names.ToArray());
        Assert.AreEqual(2, report.Count);
    }

    [TestMethod]
    public void ACloudEmitterIsReportedAsNotPlayedAndSkipped()
    {
        var model = Model(Node("cloud_1", "engines", "plasmacloud"));
        var resolved = DamageEmitters.For(Install(), model);
        Assert.AreEqual(0, resolved.Emitters.Count);
        CollectionAssert.AreEqual(new[] { "plasmacloud (cloud)" }, resolved.Reports.Single().Names.ToArray());
    }

    [TestMethod]
    public void AModelWithoutADamageGroupHasNoDamageEmitters() =>
        Assert.AreEqual(
            0,
            DamageEmitters
                .For(Install(), new Sod { Version = 1.8f, Nodes = [Node("root", ""), Node("p", "root", "plasmasml")] })
                .Emitters.Count
        );
}
