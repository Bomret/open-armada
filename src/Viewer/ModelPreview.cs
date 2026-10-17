using System.Linq;
using Godot;
using OpenArmada.Assets;
using OpenArmada.Formats;
using OpenArmada.Rendering;

namespace OpenArmada.Viewer;

/// <summary>
/// Model preview: the SOD model in its own 3D viewport, a team color selector for its team materials
/// (colors from teamcolor.odf; <c>--team &lt;name&gt;</c> picks the first one), and what the model or the team
/// colors hold that cannot be drawn as the data says.
/// </summary>
public sealed class ModelPreview : KindPreview
{
    /// <inheritdoc/>
    public override string KindName => ModelKind.KindName;

    /// <inheritdoc/>
    public override Control Create(GameFileSystem files, AssetKind kind, string name)
    {
        var builder = new SodBuilder(files);
        var team = TeamColors.Load(files);
        var model = builder.Build((Sod)kind.Load(files, name));

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
        var problems = builder.Problems.Concat(team.Notes).ToList();
        if (problems.Count > 0)
        {
            bar.AddChild(new Label { Text = $"{problems.Count} problems: {string.Join("; ", problems)}" });
        }

        var box = new VBoxContainer();
        box.AddChild(bar);
        box.AddChild(new ModelPreviewPane(model));

        return box;
    }
}
