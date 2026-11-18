using DiskCleaner.Core.Models;
using DiskCleaner.Services;
using DiskCleaner.Tests.TestHelpers;

namespace DiskCleaner.Tests.Services;

public class RuleRepositoryTests
{
    private static string RealRulesPath => Path.Combine(AppContext.BaseDirectory, "Rules", "rules.json");

    [Fact]
    public void RealRulesJson_Loads14ValidRules_NoneRejected()
    {
        Assert.True(File.Exists(RealRulesPath), "rules.json phải được copy ra output");
        var repo = new RuleRepository(RealRulesPath); // chỉ đọc + validate, không xóa gì
        var rules = repo.LoadAll();

        Assert.Empty(repo.Rejected);
        Assert.Equal(14, rules.Count);
        Assert.Equal(14, rules.Select(r => r.Id).Distinct().Count());

        string[] expected =
        [
            "user-temp", "windows-temp", "wu-cache", "recycle-bin", "crash-dumps", "thumb-cache",
            "chrome-cache", "edge-cache", "npm-cache", "nuget-cache", "pip-cache", "yarn-cache",
            "gradle-cache", "docker-prune"
        ];
        Assert.Equal(expected.OrderBy(x => x), rules.Select(r => r.Id).OrderBy(x => x));
    }

    [Fact]
    public void RealRulesJson_MatchesPlanTable()
    {
        var rules = new RuleRepository(RealRulesPath).LoadAll().ToDictionary(r => r.Id);

        Assert.True(rules["windows-temp"].RequiresAdmin);
        Assert.True(rules["wu-cache"].RequiresAdmin);
        Assert.Equal(2, rules.Values.Count(r => r.RequiresAdmin));

        Assert.Equal(24, rules["user-temp"].MinAgeHours);
        Assert.Equal(24, rules["windows-temp"].MinAgeHours);
        Assert.Equal(720, rules["gradle-cache"].MinAgeHours);

        Assert.Equal(RuleAction.EmptyRecycleBin, rules["recycle-bin"].Action);
        Assert.Equal(3, rules["chrome-cache"].Paths.Length);
        Assert.Equal(3, rules["edge-cache"].Paths.Length);
        Assert.Equal(["thumbcache_*.db", "iconcache_*.db"], rules["thumb-cache"].NamePatterns);

        Assert.Equal("npm", rules["npm-cache"].Command);
        Assert.Equal("cache clean --force", rules["npm-cache"].CommandArgs);
        Assert.Equal("dotnet", rules["nuget-cache"].Command);
        Assert.Equal("nuget locals all --clear", rules["nuget-cache"].CommandArgs);
        Assert.Equal("pip", rules["pip-cache"].Command);
        Assert.Equal("yarn", rules["yarn-cache"].Command);
        Assert.Equal("docker", rules["docker-prune"].Command);
        Assert.Equal("system prune -f", rules["docker-prune"].CommandArgs);

        Assert.False(string.IsNullOrWhiteSpace(rules["nuget-cache"].Warning));
        Assert.False(string.IsNullOrWhiteSpace(rules["docker-prune"].Warning));
        Assert.False(string.IsNullOrWhiteSpace(rules["wu-cache"].Warning));

        string[] defaultOn = ["user-temp", "windows-temp", "crash-dumps", "chrome-cache", "edge-cache"];
        Assert.Equal(defaultOn.OrderBy(x => x), rules.Values.Where(r => r.DefaultSelected).Select(r => r.Id).OrderBy(x => x));
    }

    [Fact]
    public void BadRulesInFile_AreDroppedOthersKept()
    {
        using var t = new TempTree();
        var json = """
        {
          "rules": [
            { "id": "ok", "name": "ok", "description": "d", "category": "System", "action": "DeleteFiles", "paths": ["%TEMP%"] },
            { "id": "forbidden", "name": "x", "description": "d", "category": "System", "action": "DeleteFiles", "paths": ["%WINDIR%\\System32"] },
            { "id": "missing-fields" }
          ]
        }
        """;
        var file = t.P("rules.json");
        File.WriteAllText(file, json);

        var repo = new RuleRepository(file, FakeEnvironment.Standard());
        var rules = repo.LoadAll();
        Assert.Single(rules);
        Assert.Equal("ok", rules[0].Id);
        Assert.Equal(2, repo.Rejected.Count);
    }

    [Fact]
    public void MissingFile_ReturnsEmptyWithoutThrowing()
    {
        using var t = new TempTree();
        var repo = new RuleRepository(t.P("nope.json"), FakeEnvironment.Standard());
        Assert.Empty(repo.LoadAll());
        Assert.NotEmpty(repo.Rejected);
    }
}
