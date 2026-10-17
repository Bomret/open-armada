using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using OpenArmada.Assets;
using OpenArmada.Diagnostics;
using OpenArmada.Sandbox;

namespace OpenArmada.Viewer;

/// <summary>
/// The asset browser: categories, the assets of the selected category, and a preview of the selected
/// asset.
///   godot-mono --path . -- --game &lt;Armada dir&gt; [--model Fente] [--screenshot out.png]
///   godot-mono --path . -- --game &lt;Armada dir&gt; --map &lt;name&gt; [--screenshot out.png]   (the map sandbox)
///   godot-mono --headless --path . -- --check --game &lt;Armada dir&gt;
/// The game dir can also come from the ARMADA_DIR environment variable. <c>--check</c> wins over <c>--map</c>.
/// </summary>
public partial class AssetBrowserView : Control
{
    AssetBrowser _browser = null!;
    IReadOnlyDictionary<string, KindPreview> _previews = null!;
    ItemList _kinds = null!,
        _assets = null!;
    Control _pane = null!;
    Label _message = null!;
    Node? _preview;
    string? _screenshot;
    int _frames;
    int? _checkCode;

    /// <inheritdoc/>
    public override void _Ready()
    {
        // First statement: every entry of the run, the engine's own ones too, lands in godot.log.
        Log.Sink = line => GD.Print(line);
        var userArgs = OS.GetCmdlineUserArgs();
        var args = new LaunchArgs(userArgs);
        var gameArg = args.Value("--game");
        var envGame = System.Environment.GetEnvironmentVariable("ARMADA_DIR");
        var game = gameArg ?? envGame;
        _screenshot = args.Value("--screenshot");
        var model = args.Value("--model");
        var check = args.Has("--check");
        if (!check && args.Has("--map"))
        {
            args.Value("--map"); // looked up now so its problem is known at run start
        }

        var mode =
            check ? "check"
            : args.Has("--map") ? "map"
            : "browser";
        var source =
            gameArg != null ? "--game"
            : envGame != null ? "ARMADA_DIR"
            : "none";
        var logFile = ProjectSettings.GlobalizePath(
            (string)ProjectSettings.GetSetting("debug/file_logging/log_path", "user://logs/godot.log")
        );
        Log.Info(
            Area.Run,
            $"run start: mode {mode}, install {(game == null ? "none" : Log.Quote(game))} (from {source}), "
                + $"arguments [{string.Join(", ", userArgs.Select(Log.Quote))}], log {Log.Quote(logFile)}"
        );
        foreach (var problem in args.Problems)
        {
            Log.Warn(Area.Run, problem);
        }

        if (check)
        {
            // A Godot callback exception does not set the exit code, so the check owns it.
            int code;
            try
            {
                code = AssetCheck.Run(game, AssetKind.Discover(), GodotDecoders.Check);
            }
            catch (Exception e)
            {
                Log.Error(Area.Check, $"asset check crashed: {e}");
                code = 1;
            }

            _checkCode = code;
            GetTree().Quit(code);

            return;
        }

        if (args.Has("--map"))
        {
            var map = args.Value("--map");
            // The sandbox saves the screenshot; the browser's own _Process must not.
            // This full-window Control would stop every mouse event in the GUI pass, so the sandbox's
            // _UnhandledInput (selection, move orders) would never see a click.
            MouseFilter = MouseFilterEnum.Ignore;
            AddChild(new MapSandboxView(MapSandbox.Open(game, map, args.Problems), _screenshot));
            _screenshot = null;

            return;
        }

        var registered = AssetKind.Discover();
        _browser = AssetBrowser.Open(game, registered);
        _previews = KindPreview.Discover(registered.Select(k => k.Name), out var previewProblems);

        var split = new HSplitContainer();
        split.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(split);
        var left = new VBoxContainer { CustomMinimumSize = new Vector2(280, 0) };
        split.AddChild(left);
        _kinds = new ItemList { CustomMinimumSize = new Vector2(0, 90) };
        _assets = new ItemList { SizeFlagsVertical = SizeFlags.ExpandFill };
        left.AddChild(_kinds);
        left.AddChild(_assets);
        var startProblems = previewProblems.Concat(args.Problems).ToList();
        if (startProblems.Count > 0)
        {
            var text = string.Join("\n", startProblems);

            // The launch-argument problems are in the log since run start; the registry's are logged here.
            ViewerLog.StartProblems(previewProblems);

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

            ViewerLog.Failed(_browser.Problem);

            return;
        }

        foreach (var kind in _browser.Kinds)
        {
            _kinds.AddItem(kind.Name);
        }

        _kinds.ItemSelected += i =>
        {
            _browser.SelectKind((int)i);
            FillAssets(-1);
        };
        _assets.ItemSelected += i => ShowAsset((int)i);
        if (_browser.Kind != null) // the browser selects the first kind when there is one
        {
            int wanted = model == null ? -1 : _browser.SelectModel(model);
            _kinds.Select(Array.IndexOf(_browser.Kinds.ToArray(), _browser.Kind));
            FillAssets(wanted);
        }
    }

    // Lists the selected kind's assets and selects the one at `wanted`, or else the first.
    void FillAssets(int wanted)
    {
        ViewerLog.CategorySelected(_browser.Kind!.Name);
        _assets.Clear();
        foreach (var name in _browser.Assets)
        {
            _assets.AddItem(name);
        }

        ViewerLog.Failed(_browser.Notice);

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

        ViewerLog.AssetSelected(kind.Name, name);

        if (!_previews.TryGetValue(kind.Name, out var preview))
        {
            ViewerLog.NoPreview(kind.Name);
            ShowMessage($"no preview for {kind.Name}");

            return;
        }

        try
        {
            var control = preview.Create(_browser.Files!, kind, name);
            ShowMessage("");
            _pane.AddChild(control);
            // Offsets too: a control added after layout keeps its 0x0 size under SetAnchorsPreset.
            control.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            _preview = control;
        }
        catch (Exception e) // a failed asset never stops the browser (INV-0002)
        {
            ViewerLog.PreviewFailed(name, e);
            ShowMessage(AssetBrowser.FailureMessage(name, e));
        }
    }

    /// <inheritdoc/>
    public override void _ExitTree()
    {
        // The end entry does not count itself; a print from here reaches godot.log on quit.
        var counts = $"{Log.WarnCount} WARN, {Log.ErrorCount} ERROR";
        Log.Info(Area.Run, _checkCode is { } code ? $"run end: {counts}, exit code {code}" : $"run end: {counts}");
    }

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        // Wait a few frames so textures are uploaded before capturing.
        if (_screenshot == null || ++_frames < 5)
            return;
        var saved = GetViewport().GetTexture().GetImage().SavePng(_screenshot);
        // user:// and res:// paths are Godot's; the log names the file on disk.
        ViewerLog.Screenshot(ProjectSettings.GlobalizePath(_screenshot), saved == Error.Ok ? null : saved.ToString());
        GetTree().Quit();
    }
}
