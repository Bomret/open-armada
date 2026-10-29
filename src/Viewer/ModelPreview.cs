using System.Collections.Generic;
using System.Linq;
using Godot;
using OpenArmada.Assets;
using OpenArmada.Formats;
using OpenArmada.Rendering;
using OpenArmada.Sandbox;

namespace OpenArmada.Viewer;

/// <summary>
/// Model preview: the SOD model in its own 3D viewport, a team color selector for its team materials
/// (colors from teamcolor.odf; <c>--team &lt;name&gt;</c> picks the first one), and what the model or the team
/// colors hold that cannot be drawn as the data says. A model whose damage group holds art gets a damage bar: a crew
/// loss slider, a checkbox per destroyed system, and an assimilated checkbox, each only where the model has art for it.
/// The shown nodes are the ones <see cref="DamageNodes.ShowingNodes(Sod, double, System.Func{string, bool}, bool)"/>
/// decides for that state, and the damage emitters under them spray.
/// </summary>
public sealed class ModelPreview : KindPreview
{
    /// <inheritdoc/>
    public override string KindName => ModelKind.KindName;

    /// <inheritdoc/>
    public override Preview Create(GameFileSystem files, AssetKind kind, string name)
    {
        var builder = new SodBuilder(files);
        var team = TeamColors.Load(files);
        var sod = (Sod)kind.Load(files, name);
        var model = builder.Build(sod, asset: name);

        var pick = new OptionButton();
        foreach (var slot in TeamColors.SlotNames)
        {
            pick.AddItem(slot);
        }

        void Choose(int index) => builder.SetTeamColor(team.Color(TeamColors.SlotNames[index]));
        pick.ItemSelected += i => Choose((int)i);
        var args = OS.GetCmdlineUserArgs().ToList();
        var asked = args.IndexOf("--team");
        var initial =
            asked >= 0 && asked + 1 < args.Count
                ? TeamColors
                    .SlotNames.ToList()
                    .FindIndex(n => n.Equals(args[asked + 1], System.StringComparison.OrdinalIgnoreCase))
                : -1;
        pick.Select(System.Math.Max(initial, 0));
        Choose(pick.Selected);

        var bar = new HBoxContainer();
        bar.AddChild(new Label { Text = "Team color" });
        bar.AddChild(pick);
        // The builder logged each texture that failed (ViewerLog.TextureFailed); its lines are shown, never returned as notes.
        var notes = builder.Problems.Concat(team.Notes).ToList();
        // What the damage emitters lack is a note; the spray view below reports the same gaps, which it drops.
        notes.AddRange(DamageEmitters.For(files, sod).Reports.Select(r => r.Text));
        var problems = notes.Concat(builder.TextureFailures).ToList();
        if (problems.Count > 0)
        {
            bar.AddChild(new Label { Text = $"{problems.Count} problems: {string.Join("; ", problems)}" });
        }

        var box = new VBoxContainer();
        box.AddChild(bar);
        var showing = new HashSet<Sod.Node>();
        if (DamageBar(sod, model, showing) is { } damage)
        {
            box.AddChild(damage);
        }

        // The model is the one unit whose damage emitters spray, from the nodes the damage bar shows.
        var sprays = new DamageSprayView(
            files,
            () => [model],
            _ => model,
            _ => sod,
            _ => showing,
            _ => { },
            seed: null
        );
        box.AddChild(new ModelPreviewPane(model, sprays));

        return new Preview { View = box, Notes = notes };
    }

    static readonly Dictionary<string, string> SystemLabels = new()
    {
        [SystemNames.Engines] = "Engines down",
        [SystemNames.LifeSupport] = "Life support down",
        [SystemNames.Sensors] = "Sensors down",
        [SystemNames.Weapons] = "Weapons down",
        [SystemNames.ShieldGenerator] = "Shields down",
    };

    // The damage controls for the model's art (DamageNodes.Art), or null when its damage group holds none. Each change
    // shows exactly the nodes DamageNodes decides and hides the rest of the group, as UnitOrders does in the sandbox.
    static Control? DamageBar(Sod sod, Node3D model, HashSet<Sod.Node> showing)
    {
        var art = DamageNodes.Art(sod);
        var group = model
            .FindChildren("*", "", true, false)
            .OfType<Node3D>()
            .FirstOrDefault(n => ((string)n.Name).Equals("damage", System.StringComparison.OrdinalIgnoreCase));
        if (group == null || (art.CrewStages == 0 && art.Systems.Count == 0 && !art.Borg))
            return null;

        var nodes = group
            .FindChildren("*", "", true, false)
            .OfType<Node3D>()
            .Where(n => n is not SpriteNodeQuad)
            .ToList();
        var crew = new HSlider
        {
            MaxValue = art.CrewStages,
            Step = 1,
            CustomMinimumSize = new Vector2(100, 0),
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        var down = new HashSet<string>();
        var borg = new CheckBox { Text = "Assimilated" };

        void Show()
        {
            showing.Clear();
            showing.UnionWith(
                DamageNodes.ShowingNodes(
                    sod,
                    DamageNodes.CrewRatioShowing((int)crew.Value),
                    down.Contains,
                    borg.ButtonPressed
                )
            );
            group.Visible = showing.Count > 0;
            foreach (var node in nodes)
            {
                // The builder stamps each node with its index in the model, so equal names under different parents differ.
                node.Visible =
                    node.HasMeta(SodBuilder.SodIndexMeta)
                    && (int)node.GetMeta(SodBuilder.SodIndexMeta) is var index
                    && index >= 0
                    && index < sod.Nodes.Count
                    && showing.Contains(sod.Nodes[index]);
            }
        }

        var bar = new HBoxContainer();
        bar.AddChild(new Label { Text = "Damage" });
        if (art.CrewStages > 0)
        {
            bar.AddChild(new Label { Text = "Crew loss" });
            bar.AddChild(crew);
            crew.ValueChanged += _ => Show();
        }

        foreach (var system in art.Systems)
        {
            var box = new CheckBox { Text = SystemLabels[system] };
            box.Toggled += on =>
            {
                if (on)
                    down.Add(system);
                else
                    down.Remove(system);
                Show();
            };
            bar.AddChild(box);
        }

        if (art.Borg)
        {
            borg.Toggled += _ => Show();
            bar.AddChild(borg);
        }

        return bar;
    }
}
