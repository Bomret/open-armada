using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using OpenArmada.Assets;

namespace OpenArmada.Viewer;

// The asset browser: categories, the assets of the selected category, and a preview of the selected
// asset.
//   godot-mono --path . -- --game <Armada dir> [--model Fente] [--screenshot out.png]
//   godot-mono --headless --path . -- --check --game <Armada dir>
// The game dir can also come from the ARMADA_DIR environment variable.
public partial class AssetBrowserView : Control
{
    AssetBrowser _browser = null!;
    IReadOnlyDictionary<string, KindPreview> _previews = null!;
    ItemList _kinds = null!, _assets = null!;
    Control _pane = null!;
    Label _message = null!;
    Node? _preview;
    string? _screenshot;
    int _frames;

    public override void _Ready()
    {
        var args = OS.GetCmdlineUserArgs();
        string? Arg(string name)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        var game = Arg("--game") ?? System.Environment.GetEnvironmentVariable("ARMADA_DIR");
        if (Array.IndexOf(args, "--check") >= 0)
        {
            // A Godot callback exception does not set the exit code, so the check owns it.
            int code;
            try { code = AssetCheck.Run(game, AssetKind.Discover(), Console.Out); }
            catch (Exception e)
            {
                Console.Out.WriteLine($"asset check crashed: {e}");
                code = 1;
            }
            GetTree().Quit(code);
            return;
        }

        _screenshot = Arg("--screenshot");
        _browser = AssetBrowser.Open(game, AssetKind.Discover());
        _previews = KindPreview.Discover(_browser.Kinds.Select(k => k.Name), out var previewProblems);

        var split = new HSplitContainer();
        split.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(split);
        var left = new VBoxContainer { CustomMinimumSize = new Vector2(280, 0) };
        split.AddChild(left);
        _kinds = new ItemList { CustomMinimumSize = new Vector2(0, 90) };
        _assets = new ItemList { SizeFlagsVertical = SizeFlags.ExpandFill };
        left.AddChild(_kinds);
        left.AddChild(_assets);
        if (previewProblems.Count > 0)
        {
            var text = string.Join("\n", previewProblems);
            GD.PrintErr(text);
            left.AddChild(new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart });
        }
        _pane = new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        split.AddChild(_pane);
        _message = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _message.SetAnchorsPreset(LayoutPreset.FullRect);
        _pane.AddChild(_message);

        if (_browser.Problem != null)
        {
            _message.Text = _browser.Problem;
            GD.PrintErr(_browser.Problem);
            return;
        }
        foreach (var kind in _browser.Kinds) _kinds.AddItem(kind.Name);
        _kinds.ItemSelected += i => { _browser.SelectKind((int)i); FillAssets(-1); };
        _assets.ItemSelected += i => ShowAsset((int)i);
        if (_browser.Kind != null) // the browser selects the first kind when there is one
        {
            var model = Arg("--model");
            int wanted = model == null ? -1 : _browser.SelectModel(model);
            _kinds.Select(Array.IndexOf(_browser.Kinds.ToArray(), _browser.Kind));
            FillAssets(wanted);
        }
    }

    // Lists the selected kind's assets and selects the one at `wanted`, or else the first.
    void FillAssets(int wanted)
    {
        _assets.Clear();
        foreach (var name in _browser.Assets) _assets.AddItem(name);
        if (_browser.Assets.Count == 0)
        {
            ShowMessage(_browser.Notice ?? $"no {_browser.Kind!.Name} in this install");
            return;
        }
        int index = Math.Max(0, wanted);
        _assets.Select(index);
        _assets.EnsureCurrentIsVisible();
        ShowAsset(index);
    }

    void ShowMessage(string text)
    {
        _preview?.QueueFree();
        _preview = null;
        _message.Text = text;
    }

    void ShowAsset(int index)
    {
        var kind = _browser.Kind!;
        var name = _browser.Assets[index];
        if (!_previews.TryGetValue(kind.Name, out var preview))
        {
            ShowMessage($"no preview for {kind.Name}");
            return;
        }
        try
        {
            var control = preview.Create(_browser.Files!, kind, name);
            ShowMessage("");
            _pane.AddChild(control);
            control.SetAnchorsPreset(LayoutPreset.FullRect);
            _preview = control;
        }
        catch (Exception e) // a failed asset never stops the browser (INV-0002)
        {
            ShowMessage(AssetBrowser.FailureMessage(name, e));
        }
    }

    public override void _Process(double delta)
    {
        // Wait a few frames so textures are uploaded before capturing.
        if (_screenshot == null || ++_frames < 5) return;
        GetViewport().GetTexture().GetImage().SavePng(_screenshot);
        GetTree().Quit();
    }
}
