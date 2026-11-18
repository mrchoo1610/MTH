using System.Text.Json;
using DiskCleaner.Core;
using DiskCleaner.Services;
using DiskCleaner.Tests.TestHelpers;

namespace DiskCleaner.Tests.Services;

public class ElevatedCleanRunnerTests
{
    [Fact]
    public void OnlyRequiresAdminRulesInRulesJson_AreExecuted()
    {
        using var t = new TempTree();
        var adminFile = t.CreateFile(@"adm\a.bin", 100, 48);
        var userFile = t.CreateFile(@"usr\u.bin", 100, 48);

        File.WriteAllText(t.P("rules.json"), """
        {
          "rules": [
            { "id": "adm", "name": "adm", "description": "d", "category": "System", "action": "DeleteFiles",
              "paths": ["%ROOT%\\adm"], "requiresAdmin": true },
            { "id": "usr", "name": "usr", "description": "d", "category": "System", "action": "DeleteFiles",
              "paths": ["%ROOT%\\usr"], "requiresAdmin": false }
          ]
        }
        """);
        // job cố tình chứa cả rule không-admin, id lạ, và một "path" giả: chỉ Id được dùng.
        var jobFile = t.P("job.json");
        File.WriteAllText(jobFile, """{ "ruleIds": ["adm", "usr", "unknown", "C:\\Windows"], "paths": ["C:\\Windows\\System32"] }""");

        var env = FakeEnvironment.Standard().Set("ROOT", t.Root);
        var code = ElevatedCleanRunner.Run(jobFile, t.P("rules.json"), env, new FakeCommandRunner(), new FakeRecycleBin(), NullLogger.Instance);

        Assert.Equal(0, code);
        Assert.False(File.Exists(adminFile));
        Assert.True(File.Exists(userFile), "rule không cần admin không được chạy trong chế độ elevated");

        var result = JsonSerializer.Deserialize<ElevatedResult>(File.ReadAllText(jobFile + ".result.json"), JsonDefaults.Options);
        Assert.NotNull(result);
        Assert.True(result!.Completed);
        Assert.Single(result.Reports);
        Assert.Equal("adm", result.Reports[0].RuleId);
        Assert.Equal(100, result.Reports[0].BytesFreed);
    }

    [Fact]
    public void BrokenJob_WritesFailureResult_AndReturnsNonZero()
    {
        using var t = new TempTree();
        File.WriteAllText(t.P("rules.json"), """{ "rules": [] }""");
        var jobFile = t.P("job.json");
        File.WriteAllText(jobFile, "not json");

        var code = ElevatedCleanRunner.Run(jobFile, t.P("rules.json"), FakeEnvironment.Standard(),
            new FakeCommandRunner(), new FakeRecycleBin(), NullLogger.Instance);

        Assert.NotEqual(0, code);
        var result = JsonSerializer.Deserialize<ElevatedResult>(File.ReadAllText(jobFile + ".result.json"), JsonDefaults.Options);
        Assert.False(result!.Completed);
    }
}
