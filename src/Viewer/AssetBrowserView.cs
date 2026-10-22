using System;
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
///   godot-mono --path . -- --game &lt;Armada dir&gt; --layout &lt;file&gt; [--screenshot out.png]   (the map sandbox, from a layout)
///   mkdir -p play-out/&lt;stem&gt;; godot-mono --path . --write-movie play-out/&lt;stem&gt;/frame.png --fixed-fps 30 -- --game &lt;Armada dir&gt; (--map &lt;name&gt; | --layout &lt;file&gt;) --play &lt;file&gt;   (a play run)
///   godot-mono --headless --path . -- --check --game &lt;Armada dir&gt;
///   godot-mono --headless --path . -- --maps --game &lt;Armada dir&gt;   (the map summary of every map)
/// The game dir can also come from the ARMADA_DIR environment variable. <c>--check</c> wins over <c>--maps</c>,
/// <c>--map</c> and <c>--layout</c>; <c>--maps</c> wins over <c>--map</c>, <c>--layout</c> and <c>--play</c>;
/// <c>--map</c> and <c>--layout</c> together are a problem.
/// </summary>
public partial class AssetBrowserView : Control
{
    AssetBrowser _browser = null!;
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
        var mode = args.Mode();
        var check = mode == "check";
        var maps = mode == "maps";
        if (!check && !maps && args.Has("--map"))
        {
            args.Value("--map"); // looked up now so its problem is known at run start
        }

        if (!check && !maps && args.Has("--layout"))
        {
            args.Value("--layout"); // likewise
            args.Exclusive("--layout", "--map");
        }

        if (!check && !maps && args.Has("--play"))
        {
            args.Value("--play"); // likewise: a bare --play is logged here, once
        }

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

        if (maps)
        {
            int code;
            try
            {
                code = MapsRun.Run(game);
            }
            catch (Exception e)
            {
                Log.Error(Area.Maps, $"maps listing crashed: {e}");
                code = 1;
            }

            _checkCode = code;
            GetTree().Quit(code);

            return;
        }

        if (mode == "play")
        {
            StartPlayRun(args, game);

            return;
        }

        if (mode is "map" or "layout")
        {
            var sandbox = OpenSandbox(args, game);
            // The sandbox saves the screenshot; the browser's own _Process must not.
            // This full-window Control would stop every mouse event in the GUI pass, so the sandbox's
            // _UnhandledInput (selection, move orders) would never see a click.
            MouseFilter = MouseFilterEnum.Ignore;
            AddChild(new MapSandboxView(sandbox, _screenshot));
            _screenshot = null;

            return;
        }

        var registered = AssetKind.Discover();
        var previews = KindPreview.Discover(registered.Select(k => k.Name), out var previewProblems);
        _browser = AssetBrowser.Open(
            game,
            registered,
            previews.ToDictionary(p => p.Key, p => AssetBrowser.Showing<Control>(p.Value.Create))
        );

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

        // The browser writes every entry of what it shows; the view only shows it.
        var opened = _browser.Start(model);
        if (_browser.Problem != null)
        {
            ShowPane(opened);

            return;
        }

        foreach (var kind in _browser.Kinds)
        {
            _kinds.AddItem(kind.Name);
        }

        _kinds.ItemSelected += i => FillAssets(_browser.ShowKind((int)i));
        _assets.ItemSelected += i => ShowPane(_browser.ShowAsset((int)i));
        if (_browser.Kind != null)
        {
            _kinds.Select(Array.IndexOf(_browser.Kinds.ToArray(), _browser.Kind));
            FillAssets(opened);
        }
    }

    // Lists the selected kind's assets, selects the one the pane shows, and shows the pane.
    void FillAssets(Pane pane)
    {
        _assets.Clear();
        foreach (var name in _browser.Assets)
        {
            _assets.AddItem(name);
        }

        if (pane.Asset >= 0)
        {
            _assets.Select(pane.Asset);
            _assets.EnsureCurrentIsVisible();
        }

        ShowPane(pane);
    }

    void ShowPane(Pane pane)
    {
        _preview?.QueueFree();
        _preview = null;
        _message.Text = pane.Text;
        if (pane.Preview is Control control)
        {
            _pane.AddChild(control);
            // Offsets too: a control added after layout keeps its 0x0 size under SetAnchorsPreset.
            control.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            _preview = control;
        }
    }

    // Opens the map sandbox of --layout, resolved against the directory the command was started in, or of --map.
    static MapSandbox OpenSandbox(LaunchArgs args, string? game)
    {
        if (!args.Has("--layout"))
            return MapSandbox.Open(game, args.Value("--map"), args.Problems);
        var layout = args.Value("--layout");
        var start = Layout.Start(System.Environment.GetEnvironmentVariable("PWD"), System.Environment.CurrentDirectory);

        return MapSandbox.OpenLayout(game, layout == null ? null : Layout.Resolve(layout, start), args.Problems);
    }

    // Starts a play run of the map sandbox on a map or a layout, or logs why it cannot and quits with exit code 1
    // (INV-0007); the window never stays open. A run that cannot start is one log entry: a launch-argument problem
    // (a flag without a value, or --map with --layout) is the entry the run start wrote, and a launch problem that
    // comes with it stays unlogged.
    void StartPlayRun(LaunchArgs args, string? game)
    {
        var problem = PlayRun.LaunchProblem(
            OS.HasFeature("movie"),
            args.Has("--map"),
            args.Has("--layout"),
            args.Has("--screenshot")
        );
        if (problem == null)
        {
            problem = PlayRun.MovieProblem(Engine.GetWriteMoviePath());
        }

        var file = args.Value("--play");
        if (problem != null && args.Problems.Count == 0)
        {
            Log.Error(Area.Run, problem);
        }

        if (problem != null || args.Problems.Count > 0 || file == null)
        {
            GetTree().Quit(1);

            return;
        }

        var script = PlayScript.Read(file);
        foreach (var scriptProblem in script.Problems)
        {
            Log.Error(Area.Sandbox, scriptProblem);
        }

        // MapSandbox logs a map or a layout that fails to open.
        var sandbox = OpenSandbox(args, game);
        if (script.Problems.Count > 0 || sandbox.Problem != null)
        {
            GetTree().Quit(1);

            return;
        }

        // See the map branch of _Ready: a full-window Control would stop the sandbox's mouse events.
        MouseFilter = MouseFilterEnum.Ignore;
        var view = new MapSandboxView(sandbox, null, script);
        AddChild(view);

        // The view adds the driver last; a scene that failed to build has none, and would stay open forever.
        if (!view.GetChildren().Any(c => c is PlayDriver))
        {
            Log.Error(Area.Sandbox, $"{script.Path}: play run cannot start, the map sandbox failed to build its scene");
            GetTree().Quit(1);
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
        // A viewport can have no image (a headless run has no renderer); that and a failed save are one ERROR.
        string? saveError = null;
        var captured = false;
        try
        {
            var image = GetViewport().GetTexture().GetImage();
            if (image != null)
            {
                captured = true;
                var saved = image.SavePng(_screenshot);
                saveError = saved == Error.Ok ? null : saved.ToString();
            }
        }
        catch (Exception e) // a capture that throws is a failed save, never an endless retry
        {
            captured = true;
            saveError = e.Message;
        }

        // user:// and res:// paths are Godot's; the log names the file on disk.
        ViewerLog.Screenshot(
            Area.Browser,
            ProjectSettings.GlobalizePath(_screenshot),
            ViewerLog.CaptureFailure(captured, saveError)
        );
        GetTree().Quit();
    }
}
