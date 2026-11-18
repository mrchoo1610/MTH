using System.Text.Json;
using DiskCleaner.Core;
using DiskCleaner.Core.Models;
using DiskCleaner.Services;
using DiskCleaner.Tests.TestHelpers;

namespace DiskCleaner.Tests.Services;

public class CustomRuleStoreTests
{
    private sealed class CapturingLogger : ILogger
    {
        public List<string> Errors { get; } = [];
        public void Info(string message) { }
        public void Error(string message, Exception? ex = null) => Errors.Add(message);
    }

    /// <summary>Môi trường giả mà profile/AppData nằm trong cây tạm: mọi thứ test đụng tới đều ở trong temp.</summary>
    private sealed class Fixture
    {
        public required TempTree Tree { get; init; }
        public required FakeEnvironment Env { get; init; }
        public required CapturingLogger Log { get; init; }
        public string JsonFile => Tree.P("custom-rules.json");

        public CustomRuleStore NewStore(IEnumerable<CleanRule>? builtIn = null) =>
            new(JsonFile, Env, Log, builtIn, Tree.P(@"u\AppData\Local\DiskCleaner"), Tree.P("exe"));
    }

    private static Fixture Create(TempTree t) => new()
    {
        Tree = t,
        Log = new CapturingLogger(),
        Env = FakeEnvironment.Standard()
            .Set("USERPROFILE", t.P("u"))
            .Set("LOCALAPPDATA", t.P(@"u\AppData\Local"))
            .Set("TEMP", t.P(@"u\AppData\Local\Temp"))
    };

    [Fact]
    public void Add_ThenLoadAllFromNewInstance_ReturnsRule()
    {
        using var t = new TempTree();
        var f = Create(t);
        var dir = t.CreateDir(@"u\AppData\Local\CapCut\User Data\Cache");
        var keep = t.CreateFile(@"u\AppData\Local\CapCut\User Data\Cache\a.bin", 50);

        var rule = f.NewStore().Add(dir, @"CapCut\Cache", 24);

        var loaded = f.NewStore().LoadAll();
        var r = Assert.Single(loaded);
        Assert.Equal(rule.Id, r.Id);
        Assert.Equal(dir, r.Paths[0]);
        Assert.Equal(@"CapCut\Cache", r.Name);
        Assert.Equal(dir, r.Description);
        Assert.Equal(24, r.MinAgeHours);
        Assert.True(r.IsCustom);
        Assert.Equal(RuleCategory.Custom, r.Category);
        Assert.Equal(RuleAction.DeleteFiles, r.Action);
        Assert.False(r.RequiresAdmin);
        Assert.True(File.Exists(keep), "Add không được đụng tới file");
    }

    [Fact]
    public void Id_IsStableAndCaseInsensitive()
    {
        using var t = new TempTree();
        var p = t.P(@"u\AppData\Local\X\Cache");
        var id = CustomRuleStore.ComputeId(p);
        Assert.Matches("^custom-[0-9a-f]{8}$", id);
        Assert.Equal(id, CustomRuleStore.ComputeId(p.ToUpperInvariant()));
        Assert.Equal(id, CustomRuleStore.ComputeId(p + "\\"));
        Assert.NotEqual(id, CustomRuleStore.ComputeId(p + "2"));
    }

    [Fact]
    public void Remove_RemovesRuleOnly_NotFiles_AndRaisesChanged()
    {
        using var t = new TempTree();
        var f = Create(t);
        var dir = t.CreateDir(@"u\AppData\Local\App\Cache");
        var file = t.CreateFile(@"u\AppData\Local\App\Cache\keep.bin", 10);
        var store = f.NewStore();
        int changed = 0;
        store.Changed += (_, _) => changed++;

        var rule = store.Add(dir, "App\\Cache", 0);
        store.Remove(rule.Id);

        Assert.Equal(2, changed);
        Assert.Empty(f.NewStore().LoadAll());
        Assert.True(File.Exists(file));
        Assert.True(Directory.Exists(dir));
    }

    [Fact]
    public void Add_Blocked_Throws_AndWritesNothing()
    {
        using var t = new TempTree();
        var f = Create(t);
        var local = t.CreateDir(@"u\AppData\Local");
        var store = f.NewStore();

        Assert.Equal(CustomRuleVerdict.Blocked, store.Check(local).Verdict);
        Assert.Throws<InvalidOperationException>(() => store.Add(local, "x", 0));
        Assert.False(File.Exists(f.JsonFile));
    }

    [Fact]
    public void Add_Duplicate_IsBlockedAsAlreadyExists()
    {
        using var t = new TempTree();
        var f = Create(t);
        var dir = t.CreateDir(@"u\AppData\Local\App\Cache");
        var store = f.NewStore();
        store.Add(dir, "a", 0);

        var check = store.Check(dir);
        Assert.Equal(CustomRuleVerdict.Blocked, check.Verdict);
        Assert.Contains(CustomRulePolicy.BlockAlreadyExists, check.ReasonKeys);
        Assert.Throws<InvalidOperationException>(() => store.Add(dir, "b", 0));
    }

    [Fact]
    public void Check_BuiltInRulePath_IsAlreadyExists()
    {
        using var t = new TempTree();
        var f = Create(t);
        var dir = t.CreateDir(@"u\AppData\Local\App\Cache");
        var builtIn = new[] { TestRules.Delete("builtin", @"%LOCALAPPDATA%\App\Cache") };

        var check = f.NewStore(builtIn).Check(dir);
        Assert.Contains(CustomRulePolicy.BlockAlreadyExists, check.ReasonKeys);
    }

    [Fact]
    public void CorruptJson_ReturnsEmpty_AndLogs_NoCrash()
    {
        using var t = new TempTree();
        var f = Create(t);
        File.WriteAllText(f.JsonFile, "{ this is not json");

        var store = f.NewStore();
        Assert.Empty(store.LoadAll());
        Assert.NotEmpty(f.Log.Errors);
        // vẫn dùng được sau đó
        var dir = t.CreateDir(@"u\AppData\Local\App\Cache");
        store.Add(dir, "ok", 0);
        Assert.Single(f.NewStore().LoadAll());
    }

    [Fact]
    public void MissingFile_ReturnsEmpty()
    {
        using var t = new TempTree();
        Assert.Empty(Create(t).NewStore().LoadAll());
    }

    private static object Raw(string id, string path) => new
    {
        id,
        name = id,
        description = path,
        category = "Custom",
        action = "DeleteFiles",
        paths = new[] { path },
        isCustom = true,
        requiresAdmin = false
    };

    [Fact]
    public void HandEditedRules_PointingIntoForbiddenZones_AreDropped()
    {
        using var t = new TempTree();
        var f = Create(t);
        var good = t.CreateDir(@"u\AppData\Local\App\Cache");
        var docs = t.CreateDir(@"u\Documents");

        var json = JsonSerializer.Serialize(new
        {
            rules = new[]
            {
                Raw("custom-00000001", @"C:\Windows\System32"),
                Raw("custom-00000002", docs),
                Raw("custom-00000003", t.P(@"u\AppData\Local")),
                Raw("custom-00000004", t.P(@"u\AppData\Local\DiskCleaner")),
                Raw("custom-00000005", good)
            }
        });
        File.WriteAllText(f.JsonFile, json);

        var loaded = f.NewStore().LoadAll();
        var r = Assert.Single(loaded);
        Assert.Equal("custom-00000005", r.Id);
        Assert.True(f.Log.Errors.Count >= 4);
    }

    [Fact]
    public void LoadedRule_IsSanitized_NoAdminNoCommandNoPatterns()
    {
        using var t = new TempTree();
        var f = Create(t);
        var good = t.CreateDir(@"u\AppData\Local\App\Cache");
        File.WriteAllText(f.JsonFile, JsonSerializer.Serialize(new
        {
            rules = new[]
            {
                new
                {
                    id = "custom-0000000a", name = "x", description = "x", category = "System", action = "DeleteFiles",
                    paths = new[] { good }, requiresAdmin = true, command = "npm", namePatterns = new[] { "*.dll" }, minAgeHours = -5
                }
            }
        }));

        var r = Assert.Single(f.NewStore().LoadAll());
        Assert.False(r.RequiresAdmin);
        Assert.Null(r.Command);
        Assert.Empty(r.NamePatterns);
        Assert.Equal(0, r.MinAgeHours);
        Assert.True(r.IsCustom);
        Assert.Equal(RuleCategory.Custom, r.Category);
    }

    [Fact]
    public void NonDeleteFilesCustomRule_IsDropped()
    {
        using var t = new TempTree();
        var f = Create(t);
        File.WriteAllText(f.JsonFile, JsonSerializer.Serialize(new
        {
            rules = new[]
            {
                new
                {
                    id = "custom-0000000b", name = "x", description = "x", category = "Custom", action = "RunCommand",
                    paths = new[] { t.P(@"u\AppData\Local\App\Cache") }, command = "npm"
                }
            }
        }));
        Assert.Empty(f.NewStore().LoadAll());
    }

    [Fact]
    public void DefaultDisplayName_ParentAndName()
    {
        Assert.Equal(@"CapCut\Cache", CustomRuleStore.DefaultDisplayName(@"C:\Users\x\AppData\Local\CapCut\Cache"));
        Assert.Equal(@"User Data\Cache", CustomRuleStore.DefaultDisplayName(@"C:\Users\x\AppData\Local\CapCut\User Data\Cache\"));
    }
}
