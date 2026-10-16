using System.Linq;
using Godot;
using OpenArmada.Assets;
using OpenArmada.Formats;
using OpenArmada.Rendering;

namespace OpenArmada.Viewer;

// Model preview: the SOD model in its own 3D viewport, a team color selector for its team materials
// (colors from teamcolor.odf; `--team <name>` picks the first one), and what the model holds that
// cannot be drawn as the data says.
public sealed class ModelPreview : KindPreview
{
    public override string KindName => ModelKind.KindName;

    public override Control Create(GameFileSystem files, AssetKind kind, string name)
    {
        var builder = new SodBuilder(files);
        var team = TeamColors.Load(files);
        var model = builder.Build((Sod)kind.Load(files, name));

        var pick = new OptionButton();
        foreach (var slot in team.Names) pick.AddItem(slot);
        void Choose(int index) => builder.SetTeamColor(team.Color(team.Names[index]));
        pick.ItemSelected += i => Choose((int)i);
        var args = OS.GetCmdlineUserArgs().ToList();
        var asked = args.IndexOf("--team");
        var initial = asked >= 0 && asked + 1 < args.Count ? team.Names.ToList().FindIndex(n => n.Equals(args[asked + 1], System.StringComparison.OrdinalIgnoreCase)) : -1;
        pick.Select(System.Math.Max(initial, 0));
        Choose(pick.Selected);

        var bar = new HBoxContainer();
        bar.AddChild(new Label { Text = "Team color" });
        bar.AddChild(pick);
        if (builder.Problems.Count > 0) bar.AddChild(new Label { Text = $"{builder.Problems.Count} problems: {string.Join("; ", builder.Problems)}" });

        var box = new VBoxContainer();
        box.AddChild(bar);
        box.AddChild(new ModelPreviewPane(model));
        return box;
    }
}
