using System.Text.Json;
using DiskCleaner.Core;
using DiskCleaner.Core.Models;
using DiskCleaner.Services;
using DiskCleaner.Tests.TestHelpers;

namespace DiskCleaner.Tests.Services;

public class CustomRuleCleanExecutorTests
{
    private static CleanRule Custom(string id, string absolutePath, int minAgeHours = 0) =>
        TestRules.Delete(id, absolutePath, minAgeHours) with { IsCustom = true, Category = RuleCategory.Custom };

    [Fact]
    public async Task ValidCustomRule_DeletesContents_KeepsRootFolder()
    {
        using var t = new TempTree();
        var a = t.CreateFile(@"target\a.bin", 100, 48);
        var b = t.CreateFile(@"target\sub\b.bin", 200, 48);
        var env = FakeEnvironment.Standard();
        var executor = new CleanExecutor(new SafetyGuardContext(env), new FakeCommandRunner(), new FakeRecycleBin(), NullLogger.Instance);

        var report = await executor.CleanAsync(Custom("custom-1", t.P("target")), null, CancellationToken.None);

        Assert.False(File.Exists(a));
        Assert.False(File.Exists(b));
        Assert.True(Directory.Exists(t.P("target")));
        Assert.Equal(2, report.FilesDeleted);
        Assert.Equal(300, report.BytesFreed);
        Assert.Empty(report.Errors);
    }

    [Fact]
    public async Task BlockedCustomRule_DeletesNothing_ReportsError()
    {
        using var t = new TempTree();
        // Profile giả nằm trong cây tạm; rule trỏ vào AppData\Local của nó (quá rộng => Blocked).
        var env = FakeEnvironment.Standard()
            .Set("USERPROFILE", t.P("u"))
            .Set("LOCALAPPDATA", t.P(@"u\AppData\Local"));
        var victim = t.CreateFile(@"u\AppData\Local\SomeApp\data.bin", 100, 48);
        var executor = new CleanExecutor(new SafetyGuardContext(env), new FakeCommandRunner(), new FakeRecycleBin(), NullLogger.Instance);

        var report = await executor.CleanAsync(Custom("custom-2", t.P(@"u\AppData\Local")), null, CancellationToken.None);

        Assert.True(File.Exists(victim));
        Assert.Equal(0, report.FilesDeleted);
        Assert.Equal(0, report.BytesFreed);
        Assert.NotEmpty(report.Errors);
    }

    [Fact]
    public async Task CustomRule_ThatContainsAppDataDir_IsBlocked()
    {
        using var t = new TempTree();
        var victim = t.CreateFile(@"target\keep\x.bin", 100, 48);
        var env = FakeEnvironment.Standard();
        var executor = new CleanExecutor(new SafetyGuardContext(env), new FakeCommandRunner(), new FakeRecycleBin(),
            NullLogger.Instance, appDataDir: t.P(@"target\keep"), appExeDir: t.P("exe"));

        var report = await executor.CleanAsync(Custom("custom-3", t.P("target")), null, CancellationToken.None);

        Assert.True(File.Exists(victim));
        Assert.NotEmpty(report.Errors);
    }

    [Fact]
    public async Task CustomRule_WithNonDeleteAction_IsRefused()
    {
        using var t = new TempTree();
        var runner = new FakeCommandRunner();
        runner.Available.Add("npm");
        var env = FakeEnvironment.Standard();
        var executor = new CleanExecutor(new SafetyGuardContext(env), runner, new FakeRecycleBin(), NullLogger.Instance);
        var rule = Custom("custom-4", t.P("target")) with { Action = RuleAction.RunCommand, Command = "npm", CommandArgs = "cache clean --force" };

        var report = await executor.CleanAsync(rule, null, CancellationToken.None);

        Assert.Empty(runner.Calls);
        Assert.NotEmpty(report.Errors);
    }

    [Fact]
    public void ElevatedMode_NeverLoadsCustomRules()
    {
        using var t = new TempTree();
        var customFile = t.CreateFile(@"cus\c.bin", 100, 48);
        // rules.json cố tình có rule gắn cờ isCustom + requiresAdmin, và có custom-rules.json cạnh đó.
        File.WriteAllText(t.P("rules.json"), """
        {
          "rules": [
            { "id": "custom-aaaaaaaa", "name": "c", "description": "d", "category": "Custom", "action": "DeleteFiles",
              "paths": ["%ROOT%\\cus"], "requiresAdmin": true, "isCustom": true }
          ]
        }
        """);
        File.WriteAllText(t.P("custom-rules.json"), """{ "rules": [] }""");
        var jobFile = t.P("job.json");
        File.WriteAllText(jobFile, """{ "ruleIds": ["custom-aaaaaaaa"] }""");

        var env = FakeEnvironment.Standard().Set("ROOT", t.Root);
        var code = ElevatedCleanRunner.Run(jobFile, t.P("rules.json"), env, new FakeCommandRunner(), new FakeRecycleBin(), NullLogger.Instance);

        Assert.Equal(0, code);
        Assert.True(File.Exists(customFile));
        var result = JsonSerializer.Deserialize<ElevatedResult>(File.ReadAllText(jobFile + ".result.json"), JsonDefaults.Options);
        Assert.Empty(result!.Reports);
    }
}
