using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenArmada.Tests;

// Every checked-in play script runs through tools/play-run.sh against the install and Godot and finishes with exit 0:
// every step runs and every expect holds. The wrapper reuses a stored result whose inputs hash the same, so a second
// run on unchanged code starts no Godot. The integration command runs this class; the unit test commands skip it.
[TestCategory(Needs.Install)]
[TestCategory(Needs.Godot)]
[OSCondition(ConditionMode.Exclude, OperatingSystems.Windows)]
[TestClass]
public class CheckedInPlayRunTests
{
    // The wrapper arguments after the stem, per play script. CheckedInPlayTests checks that it names every script.
    public static readonly IReadOnlyDictionary<string, string[]> Runs = new Dictionary<string, string[]>
    {
        ["auto-assimilator"] = ["--layout", "layouts/auto-assimilator.jsonc", "--fps", "10"],
        ["black-hole"] = ["--layout", "layouts/black-hole.jsonc", "--fps", "10"],
        ["borg-assimilation"] = ["--layout", "layouts/borg-assimilation.jsonc", "--fps", "10"],
        ["borg-move"] = ["--layout", "layouts/borg-move.jsonc", "--fps", "10"],
        ["coast"] = ["--layout", "layouts/coast.jsonc", "--fps", "30"],
        ["damage"] = ["--layout", "layouts/damage.jsonc", "--fps", "10"],
        ["destroy"] = ["--layout", "layouts/destroy.jsonc", "--fps", "30"],
        ["drift"] = ["--layout", "layouts/drift.jsonc", "--fps", "30"],
        ["emitters"] = ["--layout", "layouts/emitters.jsonc", "--fps", "30"],
        ["group-move"] = ["--layout", "layouts/group-move.jsonc", "--fps", "10"],
        ["hit-explosion"] = ["--layout", "layouts/hit-explosion.jsonc", "--fps", "30"],
        ["map-black-hole"] = ["--map", "borg4", "--fps", "10"],
        ["nebula"] = ["--layout", "layouts/nebula.jsonc", "--fps", "10"],
        ["path-field"] = ["--layout", "layouts/path-field.jsonc", "--fps", "10"],
        ["scale"] = ["--layout", "layouts/scale.jsonc", "--fps", "30"],
        ["shield-hit"] = ["--layout", "layouts/shield-hit.jsonc", "--fps", "30"],
        ["shield-hull"] = ["--layout", "layouts/destroy.jsonc", "--fps", "30"],
        ["shield-red"] = ["--layout", "layouts/shield-red.jsonc", "--fps", "30"],
        ["sound-fade"] = ["--layout", "layouts/sound-fade.jsonc", "--fps", "30"],
        ["sound-fade-start"] = ["--layout", "layouts/sound-fade.jsonc", "--fps", "30"],
        ["special-beams"] = ["--layout", "layouts/special-beams.jsonc", "--fps", "10"],
        ["special-weapon"] = ["--layout", "layouts/special-weapon.jsonc", "--fps", "10"],
        ["transport-cases"] = ["--layout", "layouts/transport-cases.jsonc", "--fps", "30"],
        ["transport-feedback"] = ["--layout", "layouts/transport-feedback.jsonc", "--fps", "30"],
    };

    public static IEnumerable<object[]> Stems => Runs.Keys.Order().Select(s => new object[] { s });

    [TestMethod]
    [DynamicData(nameof(Stems))]
    public void ACheckedInPlayScriptRunsToItsEnd(string stem)
    {
        var armada = Environment.GetEnvironmentVariable("ARMADA_DIR");
        if (string.IsNullOrEmpty(armada) || !Directory.Exists(armada))
            Assert.Inconclusive("set ARMADA_DIR to an Armada install");

        var godot = Environment.GetEnvironmentVariable("PLAY_RUN_GODOT") ?? "godot-mono";
        if (Run(godot, ["--version"], 30_000).Status != 0)
            Assert.Inconclusive($"cannot run {godot}; run the integration tests through mise run test:integration");

        var (status, output) = Run("sh", ["tools/play-run.sh", stem, .. Runs[stem]], 600_000);

        Assert.AreEqual(0, status, $"play-run.sh {stem} exited {status}:\n{output}");
    }

    static (int Status, string Output) Run(string file, string[] args, int timeout)
    {
        var info = new ProcessStartInfo(file)
        {
            WorkingDirectory = CheckedInPlayTests.Root(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in args)
            info.ArgumentList.Add(arg);

        Process process;
        try
        {
            process = Process.Start(info)!;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return (-1, $"{file} not found");
        }

        using (process)
        {
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(timeout))
            {
                process.Kill(true);
                Assert.Fail($"{file} {string.Join(' ', args)} did not exit within {timeout / 1000} s");
            }

            return (process.ExitCode, stdout.Result + stderr.Result);
        }
    }
}
