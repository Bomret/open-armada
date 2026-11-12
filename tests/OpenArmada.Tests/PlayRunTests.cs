using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenArmada.Tests;

// tools/play-run.sh runs in a throwaway git repository against a fake Godot set through PLAY_RUN_GODOT and a
// fake dotnet set through PLAY_RUN_DOTNET. The fake Godot counts its calls, writes a dump, two named shots,
// numbered frames, a WAV and log lines, and exits with FAKE_STATUS after FAKE_SLEEP seconds. The fake dotnet
// exits with FAKE_BUILD_STATUS. Both append their name and arguments to the order file.
// tools/play-run.sh is a POSIX shell script.
[TestClass]
[OSCondition(ConditionMode.Exclude, OperatingSystems.Windows)]
[System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
public class PlayRunTests
{
    const string Armada = "/games/Star Trek - Armada";

    const string FakeGodot = """
        #!/bin/sh
        if [ "$1" = --version ]; then
          [ "${FAKE_VERSION_FAIL:-}" = 1 ] && exit 1
          echo "${FAKE_VERSION:-4.5.stable.mono}"
          exit 0
        fi
        echo call >> "$FAKE_DIR/calls"
        echo godot >> "$FAKE_DIR/order"
        printf '%s\n' "$@" > "$FAKE_DIR/args"
        prev=
        for a; do
          [ "$prev" = --write-movie ] && out=$(dirname "$a")
          prev=$a
        done
        sleep "${FAKE_SLEEP:-0}"
        echo "state ${FAKE_MARK:-1}" > "$out/end.txt"
        echo shot > "$out/hull.png"
        echo shot > "$out/42.png"
        echo frame > "$out/frame00000000.png"
        echo frame > "$out/frame00000001.png"
        echo wav > "$out/frame.wav"
        echo "WARN: a warning"
        echo "plain output"
        echo "ERROR: an error" >&2
        exit "${FAKE_STATUS:-0}"
        """;

    const string FakeDotnet = """
        #!/bin/sh
        echo "dotnet $*" >> "$FAKE_DIR/order"
        echo "build output"
        exit "${FAKE_BUILD_STATUS:-0}"
        """;

    string root = "";
    string repo = "";
    string fake = "";

    [TestInitialize]
    public void Init()
    {
        root = Path.Combine(Path.GetTempPath(), "play-run-" + Guid.NewGuid().ToString("N"));
        repo = Path.Combine(root, "repo");
        fake = Path.Combine(root, "fake");
        Directory.CreateDirectory(fake);
        var godot = Path.Combine(fake, "godot");
        File.WriteAllText(godot, FakeGodot);
        File.SetUnixFileMode(godot, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var dotnet = Path.Combine(fake, "dotnet");
        File.WriteAllText(dotnet, FakeDotnet);
        File.SetUnixFileMode(dotnet, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        Write("tools/play-run.sh", File.ReadAllText(Path.Combine(RepoRoot(), "tools", "play-run.sh")));
        Write("src/Game.cs", "class Game {}");
        Write("scenes/main.tscn", "[scene]");
        Write("project.godot", "config");
        Write("OpenArmada.csproj", "<Project />");
        Write("global.json", "{}");
        Write("Directory.Build.props", "<Project />");
        Write("play/demo.play", "wait 1");
        Write("layouts/demo.jsonc", "{}");
        Write("README.md", "readme");
        Write("tests/T.cs", "class T {}");
        Write(".gitignore", "play-out/\nsrc/ignored.txt\n");
        Write("src/ignored.txt", "ignored");
        Git("init", "-q");
        Git("add", "-A");
        Git("-c", "user.name=t", "-c", "user.email=t@t", "commit", "-q", "-m", "init");
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(root, true);

    static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(dir, "tools", "play-run.sh")))
        {
            dir = Path.GetDirectoryName(dir) ?? throw new InvalidOperationException("tools/play-run.sh not found");
        }

        return dir;
    }

    void Write(string path, string text)
    {
        var full = Path.Combine(repo, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text);
    }

    string Out(string path) => Path.Combine(repo, "play-out", path);

    int Calls => File.Exists(Path.Combine(fake, "calls")) ? File.ReadAllLines(Path.Combine(fake, "calls")).Length : 0;

    string[] Order => File.Exists(Path.Combine(fake, "order")) ? File.ReadAllLines(Path.Combine(fake, "order")) : [];

    string[] LogLines => File.Exists(Out("runs.log")) ? File.ReadAllLines(Out("runs.log")) : [];

    static readonly string[] Layout = ["demo", "--layout", "layouts/demo.jsonc", "--fps", "10"];

    void Git(params string[] args) => Exec("git", args, new Dictionary<string, string?>());

    (int Status, string Stdout, string Stderr) Exec(string file, string[] args, Dictionary<string, string?> env)
    {
        var info = new ProcessStartInfo(file)
        {
            WorkingDirectory = repo,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in args)
        {
            info.ArgumentList.Add(arg);
        }

        foreach (var (key, value) in env)
        {
            if (value is null)
            {
                info.Environment.Remove(key);
            }
            else
            {
                info.Environment[key] = value;
            }
        }

        using var process = Process.Start(info)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(30000))
        {
            process.Kill(true);
            Assert.Fail($"{file} did not exit within 30 s");
        }

        return (process.ExitCode, stdout.Result, stderr.Result);
    }

    (int Status, string Stdout, string Stderr) Run(string[] args, Dictionary<string, string?>? env = null)
    {
        var all = new Dictionary<string, string?>
        {
            ["PLAY_RUN_GODOT"] = Path.Combine(fake, "godot"),
            ["PLAY_RUN_DOTNET"] = Path.Combine(fake, "dotnet"),
            ["ARMADA_DIR"] = Armada,
            ["FAKE_DIR"] = fake,
        };
        foreach (var (key, value) in env ?? [])
        {
            all[key] = value;
        }

        return Exec("sh", ["tools/play-run.sh", .. args], all);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    public void SecondCallOnUnchangedInputsReusesTheStoredResult(int status)
    {
        var env = new Dictionary<string, string?> { ["FAKE_STATUS"] = status.ToString() };
        Assert.AreEqual(status, Run(Layout, env).Status);
        File.WriteAllText(Out("demo/stray.txt"), "stray");
        File.Delete(Out("demo/end.txt"));

        var (second, stdout, _) = Run(Layout, env);

        Assert.AreEqual(1, Calls);
        Assert.AreEqual(status, second);
        StringAssert.Contains(stdout, "reused");
        StringAssert.Contains(stdout, "WARN: a warning");
        StringAssert.Contains(stdout, "ERROR: an error");
        Assert.IsFalse(stdout.Contains("plain output"));
        Assert.AreEqual("state 1\n", File.ReadAllText(Out("demo/end.txt")));
        Assert.IsTrue(File.Exists(Out("demo/hull.png")));
        Assert.IsTrue(File.Exists(Out("demo/frame.wav")));
        Assert.IsFalse(File.Exists(Out("demo/stray.txt")));
    }

    [TestMethod]
    [DataRow("src")]
    [DataRow("untracked-scenes")]
    [DataRow("deleted")]
    [DataRow("renamed")]
    [DataRow("project")]
    [DataRow("build-props")]
    [DataRow("script")]
    [DataRow("layout")]
    [DataRow("map")]
    [DataRow("fps")]
    [DataRow("version")]
    [DataRow("armada")]
    public void AChangedHashedInputStartsGodotAgain(string input)
    {
        var first = input == "map" ? new[] { "demo", "--map", "kling1", "--fps", "10" } : Layout;
        Run(first);
        var second = first;
        var env = new Dictionary<string, string?>();
        switch (input)
        {
            case "src":
                Write("src/Game.cs", "class Game { int x; }");
                break;
            case "untracked-scenes":
                Write("scenes/new.tscn", "[scene]");
                break;
            case "deleted":
                File.Delete(Path.Combine(repo, "scenes/main.tscn"));
                break;
            case "renamed":
                File.Move(Path.Combine(repo, "src/Game.cs"), Path.Combine(repo, "src/Game2.cs"));
                break;
            case "project":
                Write("project.godot", "config 2");
                break;
            case "build-props":
                Write("Directory.Build.props", "<Project><!-- changed --></Project>");
                break;
            case "script":
                Write("play/demo.play", "wait 2");
                break;
            case "layout":
                Write("layouts/demo.jsonc", "{ }");
                break;
            case "map":
                second = ["demo", "--map", "kling4", "--fps", "10"];
                break;
            case "fps":
                second = ["demo", "--layout", "layouts/demo.jsonc", "--fps", "30"];
                break;
            case "version":
                env["FAKE_VERSION"] = "4.6.stable.mono";
                break;
            case "armada":
                env["ARMADA_DIR"] = "/games/other";
                break;
        }

        Run(second, env);

        Assert.AreEqual(2, Calls);
    }

    [TestMethod]
    [DataRow("README.md")]
    [DataRow("tests/T.cs")]
    [DataRow("src/ignored.txt")]
    public void AnEditOutsideTheHashedInputsReuses(string path)
    {
        Run(Layout);
        Write(path, "edited");

        Run(Layout);

        Assert.AreEqual(1, Calls);
    }

    [TestMethod]
    public void TheLayoutPathSpellingDoesNotChangeTheHash()
    {
        Run(Layout);

        Run(["demo", "--layout", "./layouts/demo.jsonc", "--fps", "10"]);
        Run(["demo", "--layout", Path.Combine(repo, "layouts/demo.jsonc"), "--fps", "10"]);

        Assert.AreEqual(1, Calls);
    }

    [TestMethod]
    public void FreshStartsGodotOnAHitAndReplacesTheEntry()
    {
        Run(Layout);

        Run([.. Layout, "--fresh"], new Dictionary<string, string?> { ["FAKE_MARK"] = "2" });
        File.Delete(Out("demo/end.txt"));
        Run(Layout);

        Assert.AreEqual(2, Calls);
        Assert.AreEqual("state 2\n", File.ReadAllText(Out("demo/end.txt")));
    }

    [TestMethod]
    public void AnExitOtherThanZeroOrOneStoresNothing()
    {
        var env = new Dictionary<string, string?> { ["FAKE_STATUS"] = "3" };

        Assert.AreEqual(3, Run(Layout, env).Status);
        Assert.IsFalse(Directory.Exists(Out(".cache")) && Directory.EnumerateFileSystemEntries(Out(".cache")).Any());
        Run(Layout, env);

        Assert.AreEqual(2, Calls);
    }

    [TestMethod]
    public void ARunBuildsTheGameBeforeGodotAndAReuseDoesNot()
    {
        Run(Layout);
        Run(Layout);

        CollectionAssert.AreEqual(new[] { "dotnet build OpenArmada.csproj", "godot" }, Order);
    }

    [TestMethod]
    public void AFailedBuildStartsNoGodotStoresNothingAndExitsThree()
    {
        var env = new Dictionary<string, string?> { ["FAKE_BUILD_STATUS"] = "1" };

        var (status, stdout, _) = Run(Layout, env);

        Assert.AreEqual(3, status);
        StringAssert.Contains(stdout, "build output");
        Assert.AreEqual(0, Calls);
        Assert.IsFalse(Directory.Exists(Out(".cache")) && Directory.EnumerateFileSystemEntries(Out(".cache")).Any());
        CollectionAssert.AreEqual(new[] { "exit=3", "build" }, LogLines.Single().Split('\t')[7..]);
    }

    [TestMethod]
    public void NumberedFramesAreNotStoredAndAnAllDigitShotIs()
    {
        Run(Layout);
        var entry = Directory.GetDirectories(Out(".cache")).Single();
        var stored = Directory.GetFiles(entry, "*", SearchOption.AllDirectories).Select(Path.GetFileName).ToList();

        CollectionAssert.Contains(stored, "42.png");
        CollectionAssert.DoesNotContain(stored, "frame00000000.png");
        CollectionAssert.DoesNotContain(stored, "frame00000001.png");
    }

    [TestMethod]
    public void EachCallAppendsOneLogLineWithEveryField()
    {
        Run(Layout);
        Write("notes.txt", "untracked");
        Run(Layout);
        Run(["demo", "--map", "kling1", "--fps", "10"]);

        var lines = LogLines;
        var head = "";
        Assert.AreEqual(3, lines.Length);
        using (
            var git = Process.Start(
                new ProcessStartInfo("git", "rev-parse HEAD") { WorkingDirectory = repo, RedirectStandardOutput = true }
            )!
        )
        {
            head = git.StandardOutput.ReadToEnd().Trim();
            git.WaitForExit();
        }

        var first = lines[0].Split('\t');
        Assert.AreEqual(9, first.Length, lines[0]);
        StringAssert.Matches(first[0], new System.Text.RegularExpressions.Regex(@"^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\dZ$"));
        StringAssert.Matches(first[1], new System.Text.RegularExpressions.Regex("^[0-9a-f]{40}$"));
        CollectionAssert.AreEqual(
            new[] { head, "clean", "demo", "layout=layouts/demo.jsonc", "fps=10", "exit=0", "run" },
            first[2..]
        );
        var second = lines[1].Split('\t');
        Assert.AreEqual(first[1], second[1]);
        CollectionAssert.AreEqual(new[] { "dirty", "reused" }, new[] { second[3], second[8] });
        Assert.AreEqual("map=kling1", lines[2].Split('\t')[5]);
    }

    [TestMethod]
    [DataRow("none", "demo", "--fps", "10")]
    [DataRow("both", "demo", "--layout", "layouts/demo.jsonc", "--map", "kling1", "--fps", "10")]
    [DataRow("no value", "demo", "--layout")]
    [DataRow("no fps", "demo", "--layout", "layouts/demo.jsonc")]
    [DataRow("zero fps", "demo", "--layout", "layouts/demo.jsonc", "--fps", "0")]
    [DataRow("negative fps", "demo", "--layout", "layouts/demo.jsonc", "--fps", "-5")]
    [DataRow("text fps", "demo", "--layout", "layouts/demo.jsonc", "--fps", "ten")]
    [DataRow("slash stem", "play/demo", "--layout", "layouts/demo.jsonc", "--fps", "10")]
    [DataRow("dots stem", "..", "--layout", "layouts/demo.jsonc", "--fps", "10")]
    [DataRow("missing script", "nothing", "--layout", "layouts/demo.jsonc", "--fps", "10")]
    [DataRow("missing layout", "demo", "--layout", "layouts/nothing.jsonc", "--fps", "10")]
    [DataRow("unknown flag", "demo", "--layout", "layouts/demo.jsonc", "--fps", "10", "--fast")]
    [DataRow("no args")]
    public void BadArgumentsExitTwoAndLeaveNoTrace(string reason, params string[] args) =>
        AssertUsageError(Run(args), reason);

    [TestMethod]
    public void AnUnsetArmadaDirExitsTwo() =>
        AssertUsageError(Run(Layout, new Dictionary<string, string?> { ["ARMADA_DIR"] = null }), "unset");

    [TestMethod]
    public void AFailingVersionReadExitsTwo() =>
        AssertUsageError(Run(Layout, new Dictionary<string, string?> { ["FAKE_VERSION_FAIL"] = "1" }), "version");

    void AssertUsageError((int Status, string Stdout, string Stderr) result, string reason)
    {
        Assert.AreEqual(2, result.Status, reason);
        Assert.AreNotEqual("", result.Stderr.Trim(), reason);
        Assert.AreEqual(0, Calls, reason);
        Assert.IsFalse(Directory.Exists(Out(".cache")), reason);
        Assert.AreEqual(0, LogLines.Length, reason);
    }

    [TestMethod]
    public void AnArmadaDirWithSpacesReachesGodotAsOneArgument()
    {
        Run(Layout);

        CollectionAssert.Contains(File.ReadAllLines(Path.Combine(fake, "args")), Armada);
    }

    [TestMethod]
    public void TwoConcurrentCallsForOneStemRunGodotOnceAndStoreOneEntry()
    {
        var env = new Dictionary<string, string?> { ["FAKE_SLEEP"] = "2" };
        var calls = new[] { Task.Run(() => Run(Layout, env)), Task.Run(() => Run(Layout, env)) };
        Task.WaitAll(calls);

        CollectionAssert.AreEqual(new[] { 0, 0 }, calls.Select(c => c.Result.Status).ToArray());
        Assert.AreEqual(1, Calls);
        var entry = Directory.GetDirectories(Out(".cache")).Single();
        CollectionAssert.AreEqual(new[] { "out" }, Directory.GetDirectories(entry).Select(Path.GetFileName).ToArray());
        Assert.AreEqual("state 1\n", File.ReadAllText(Out("demo/end.txt")));
    }

    void HoldLock(int pid)
    {
        Directory.CreateDirectory(Out(".lock-demo"));
        File.WriteAllText(Out(".lock-demo/pid"), pid + "\n");
    }

    [TestMethod]
    public void ALockOfADeadProcessIsTakenOver()
    {
        using var gone = Process.Start("true")!;
        gone.WaitForExit();
        HoldLock(gone.Id);

        Assert.AreEqual(0, Run(Layout).Status);
        Assert.AreEqual(1, Calls);
    }

    [TestMethod]
    public void ALockWithoutAPidIsTakenOverAfterTheGrace()
    {
        Directory.CreateDirectory(Out(".lock-demo"));

        var call = Run(Layout, new Dictionary<string, string?> { ["FAKE_STATUS"] = "1" });

        Assert.AreEqual(1, call.Status);
        Assert.AreEqual(1, Calls);
    }

    [TestMethod]
    public void ALockThatGainsAPidWithinTheGraceIsWaitedOnAndItsResultReused()
    {
        Run(Layout);
        Directory.CreateDirectory(Out(".lock-demo"));
        var call = Task.Run(() => Run(Layout));
        Thread.Sleep(1500);
        File.WriteAllText(Out(".lock-demo/pid"), Environment.ProcessId + "\n");
        // Well past the grace: a live owner whose pid appears within the grace is not taken over.
        Thread.Sleep(7000);
        var kept = File.ReadAllText(Out(".lock-demo/pid")).Trim() == Environment.ProcessId.ToString();
        Directory.Delete(Out(".lock-demo"), true);

        Assert.AreEqual(0, call.Result.Status);
        Assert.IsTrue(kept, "the live owner's lock was taken over");
        Assert.AreEqual(1, Calls);
    }

    // A mkdir shim first on PATH stands in for a rival call: when the wrapper takes the takeover mutex, the shim
    // rewrites the stale lock to a live owner, as a rival that took over first would. The live lock must survive.
    const string MkdirShim = """
        #!/bin/sh
        for a; do
          case "$a" in
            *.takeover)
              if [ ! -f "$FAKE_DIR/shimmed" ]; then
                echo "$FAKE_LIVE_PID" > play-out/.lock-demo/pid
                : > "$FAKE_DIR/shimmed"
              fi
              ;;
          esac
        done
        /bin/mkdir "$@"
        status=$?
        if [ "$status" -ne 0 ] && [ -f "$FAKE_DIR/shimmed" ]; then : > "$FAKE_DIR/retried"; fi
        exit "$status"
        """;

    [TestMethod]
    public void ATakeoverKeepsALockThatTurnedLiveAndWaits()
    {
        var shims = Path.Combine(root, "shims");
        Directory.CreateDirectory(shims);
        File.WriteAllText(Path.Combine(shims, "mkdir"), MkdirShim);
        File.SetUnixFileMode(
            Path.Combine(shims, "mkdir"),
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
        );
        using var gone = Process.Start("true")!;
        gone.WaitForExit();
        HoldLock(gone.Id);
        var live = Environment.ProcessId.ToString();
        var env = new Dictionary<string, string?>
        {
            ["PATH"] = shims + ":" + Environment.GetEnvironmentVariable("PATH"),
            ["FAKE_LIVE_PID"] = live,
        };
        var call = Task.Run(() => Run(Layout, env));
        // A failed mkdir after the takeover is the retry on the kept lock, after which the call says it waits.
        var retried = Path.Combine(fake, "retried");
        for (var i = 0; i < 200 && !File.Exists(retried) && !call.IsCompleted; i++)
        {
            Thread.Sleep(50);
        }

        var survived = File.Exists(retried) && File.ReadAllText(Out(".lock-demo/pid")).Trim() == live;
        Directory.Delete(Out(".lock-demo"), true);
        call.Wait();

        Assert.IsTrue(survived, "the live lock was removed");
        StringAssert.Contains(call.Result.Stderr, "waiting on play-out/.lock-demo");
    }

    // As MkdirShim, but the rival that held the dead lock removes it before the wrapper holds the mutex; an ls shim
    // then stands in for a waiter that mkdirs a fresh live lock just after the wrapper's `ls -di` of the vanished one.
    const string VanishShim = """
        #!/bin/sh
        for a; do
          case "$a" in
            *.takeover)
              if [ ! -f "$FAKE_DIR/shimmed" ]; then
                /bin/rm -rf play-out/.lock-demo
                : > "$FAKE_DIR/shimmed"
              fi
              ;;
          esac
        done
        /bin/mkdir "$@"
        status=$?
        if [ "$status" -ne 0 ] && [ -f "$FAKE_DIR/shimmed" ]; then : > "$FAKE_DIR/retried"; fi
        exit "$status"
        """;

    const string LiveAfterLsShim = """
        #!/bin/sh
        /bin/ls "$@"
        status=$?
        case "$*" in
          *-di*.lock-demo)
            if [ -f "$FAKE_DIR/shimmed" ] && [ ! -f "$FAKE_DIR/lsdone" ]; then
              /bin/mkdir play-out/.lock-demo
              echo "$FAKE_LIVE_PID" > play-out/.lock-demo/pid
              : > "$FAKE_DIR/lsdone"
            fi
            ;;
        esac
        exit "$status"
        """;

    [TestMethod]
    public void ATakeoverKeepsALiveLockMadeWhereTheDeadOneVanished()
    {
        var shims = Path.Combine(root, "shims");
        Directory.CreateDirectory(shims);
        foreach (var (name, body) in new[] { ("mkdir", VanishShim), ("ls", LiveAfterLsShim) })
        {
            File.WriteAllText(Path.Combine(shims, name), body);
            File.SetUnixFileMode(
                Path.Combine(shims, name),
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            );
        }
        using var gone = Process.Start("true")!;
        gone.WaitForExit();
        HoldLock(gone.Id);
        var live = Environment.ProcessId.ToString();
        var env = new Dictionary<string, string?>
        {
            ["PATH"] = shims + ":" + Environment.GetEnvironmentVariable("PATH"),
            ["FAKE_LIVE_PID"] = live,
        };
        var call = Task.Run(() => Run(Layout, env));
        var retried = Path.Combine(fake, "retried");
        for (var i = 0; i < 200 && !File.Exists(retried) && !call.IsCompleted; i++)
        {
            Thread.Sleep(50);
        }

        var survived = File.Exists(retried) && File.ReadAllText(Out(".lock-demo/pid")).Trim() == live;
        if (Directory.Exists(Out(".lock-demo")))
        {
            Directory.Delete(Out(".lock-demo"), true);
        }
        call.Wait();

        Assert.IsTrue(survived, "the live lock was removed");
    }

    // As MkdirShim, but the rival replaces the pid-less lock with a fresh pid-less one (another inode, as a call
    // that took over first and is about to write its pid leaves it) and marks it. The replacement must survive.
    const string ReplaceShim = """
        #!/bin/sh
        for a; do
          case "$a" in
            *.takeover)
              if [ ! -f "$FAKE_DIR/shimmed" ]; then
                /bin/mkdir play-out/.lock-demo.new
                : > play-out/.lock-demo.new/replacement
                /bin/mv play-out/.lock-demo play-out/.lock-demo.old
                /bin/mv play-out/.lock-demo.new play-out/.lock-demo
                /bin/rmdir play-out/.lock-demo.old
                : > "$FAKE_DIR/shimmed"
              fi
              ;;
          esac
        done
        /bin/mkdir "$@"
        status=$?
        if [ "$status" -ne 0 ] && [ -f "$FAKE_DIR/shimmed" ]; then : > "$FAKE_DIR/retried"; fi
        exit "$status"
        """;

    [TestMethod]
    public void ATakeoverKeepsAPidlessLockReplacedWhileItWaitedForTheMutex()
    {
        var shims = Path.Combine(root, "shims");
        Directory.CreateDirectory(shims);
        File.WriteAllText(Path.Combine(shims, "mkdir"), ReplaceShim);
        File.SetUnixFileMode(
            Path.Combine(shims, "mkdir"),
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
        );
        Directory.CreateDirectory(Out(".lock-demo"));
        var env = new Dictionary<string, string?>
        {
            ["PATH"] = shims + ":" + Environment.GetEnvironmentVariable("PATH"),
        };
        var call = Task.Run(() => Run(Layout, env));
        var retried = Path.Combine(fake, "retried");
        for (var i = 0; i < 200 && !File.Exists(retried) && !call.IsCompleted; i++)
        {
            Thread.Sleep(50);
        }

        var survived = File.Exists(retried) && File.Exists(Out(".lock-demo/replacement"));
        Directory.Delete(Out(".lock-demo"), true);
        call.Wait();

        Assert.IsTrue(survived, "the replacement lock was removed");
    }

    // The takeover race is timing-dependent, so this can pass without the atomic rename on a quiet machine.
    [TestMethod]
    public void ConcurrentWaitersOnADeadLockRunGodotOnce()
    {
        using var gone = Process.Start("true")!;
        gone.WaitForExit();
        HoldLock(gone.Id);
        var env = new Dictionary<string, string?> { ["FAKE_SLEEP"] = "2" };
        var calls = Enumerable.Range(0, 6).Select(_ => Task.Run(() => Run(Layout, env))).ToArray();
        Task.WaitAll(calls);

        Assert.AreEqual(1, Calls);
        Assert.IsFalse(Directory.EnumerateDirectories(Out("")).Any(d => d.Contains(".dead.")));
    }

    [TestMethod]
    public void ACallWaitingOnALiveLockSaysSoAndRunsOnceItIsFree()
    {
        HoldLock(Environment.ProcessId);
        var call = Task.Run(() => Run(Layout));
        Thread.Sleep(1500);
        Directory.Delete(Out(".lock-demo"), true);

        Assert.AreEqual(0, call.Result.Status);
        StringAssert.Contains(
            call.Result.Stderr,
            "play-run.sh: waiting on play-out/.lock-demo (remove it if no run is live)"
        );
    }

    [TestMethod]
    public void TheHashIsTakenAfterTheLock()
    {
        Run(Layout);
        HoldLock(Environment.ProcessId);
        var call = Task.Run(() => Run(Layout));
        Thread.Sleep(1500);
        Write("src/Game.cs", "class Game { int changed; }");
        Directory.Delete(Out(".lock-demo"), true);
        call.Wait();

        Assert.AreEqual(2, Calls);
    }

    [TestMethod]
    public void AnUnwritablePlayOutFailsWithAMessage()
    {
        Directory.CreateDirectory(Out(""));
        File.SetUnixFileMode(Out(""), UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            // Root writes to a folder whatever its mode (container CI), so the fault cannot be staged there.
            try
            {
                File.WriteAllText(Path.Combine(Out(""), "probe"), "");
                Assert.Inconclusive("the folder stays writable (running as root)");
            }
            catch (UnauthorizedAccessException) { }

            var call = Task.Run(() => Run(Layout));
            var finished = call.Wait(10000);

            Assert.IsTrue(finished, "the call waits forever");
            Assert.AreNotEqual(0, call.Result.Status);
            StringAssert.Contains(call.Result.Stderr, "play-out");
            Assert.AreEqual(0, Calls);
        }
        finally
        {
            File.SetUnixFileMode(Out(""), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }
}
