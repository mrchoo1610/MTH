using DiskCleaner.Core;
using DiskCleaner.Core.Models;
using DiskCleaner.Services;
using DiskCleaner.Tests.TestHelpers;

namespace DiskCleaner.Tests.Services;

public class CleanExecutorTests
{
    private sealed class Fixture
    {
        public required FakeEnvironment Env { get; init; }
        public required FakeCommandRunner Runner { get; init; }
        public required FakeRecycleBin Bin { get; init; }
        public required CleanExecutor Executor { get; init; }
        public required FileSystemScanner Scanner { get; init; }
    }

    private static Fixture Create(TempTree t, Action<FakeEnvironment>? tweak = null)
    {
        var env = FakeEnvironment.Standard().Set("ROOT", t.Root);
        tweak?.Invoke(env);
        var runner = new FakeCommandRunner();
        var bin = new FakeRecycleBin();
        return new Fixture
        {
            Env = env,
            Runner = runner,
            Bin = bin,
            Executor = new CleanExecutor(new SafetyGuardContext(env), runner, bin, NullLogger.Instance),
            Scanner = new FileSystemScanner(env, runner, bin)
        };
    }

    [Fact]
    public async Task DeletesFiles_KeepsRootDirectory()
    {
        using var t = new TempTree();
        var a = t.CreateFile(@"target\a.bin", 100, 48);
        var b = t.CreateFile(@"target\sub\b.bin", 200, 48);
        var f = Create(t);

        var report = await f.Executor.CleanAsync(TestRules.Delete("r", @"%ROOT%\target"), null, CancellationToken.None);

        Assert.False(File.Exists(a));
        Assert.False(File.Exists(b));
        Assert.True(Directory.Exists(t.P("target")), "thư mục gốc của rule phải còn");
        Assert.Equal(2, report.FilesDeleted);
        Assert.Equal(0, report.FilesSkipped);
        Assert.Equal(300, report.BytesFreed);
        Assert.Empty(report.Errors);
    }

    [Fact]
    public async Task RemovesEmptySubdirectories_ButKeepsOnesWithSurvivors()
    {
        using var t = new TempTree();
        t.CreateFile(@"target\emptyafter\deep\x.bin", 10, 48);
        var survivor = t.CreateFile(@"target\keep\new.bin", 10, 0);
        t.CreateFile(@"target\keep\old.bin", 10, 48);
        t.SetDirAge(@"target\emptyafter", 48);
        t.SetDirAge(@"target\emptyafter\deep", 48);
        var f = Create(t);

        await f.Executor.CleanAsync(TestRules.Delete("r", @"%ROOT%\target", minAgeHours: 24), null, CancellationToken.None);

        Assert.False(Directory.Exists(t.P(@"target\emptyafter")));
        Assert.True(File.Exists(survivor));
        Assert.True(Directory.Exists(t.P(@"target\keep")));
        Assert.False(File.Exists(t.P(@"target\keep\old.bin")));
    }

    [Fact]
    public async Task RespectsMinAge_NewFilesSurvive()
    {
        using var t = new TempTree();
        var old = t.CreateFile(@"target\old.bin", 100, 48);
        var fresh = t.CreateFile(@"target\fresh.bin", 100, 1);
        var f = Create(t);

        var report = await f.Executor.CleanAsync(TestRules.Delete("r", @"%ROOT%\target", minAgeHours: 24), null, CancellationToken.None);

        Assert.False(File.Exists(old));
        Assert.True(File.Exists(fresh));
        Assert.Equal(1, report.FilesDeleted);
    }

    [Fact]
    public async Task NamePatterns_OnlyMatchingFilesDeleted_DirectoriesKept()
    {
        using var t = new TempTree();
        var thumb = t.CreateFile(@"target\thumbcache_96.db", 100);
        var icon = t.CreateFile(@"target\iconcache_96.db", 100);
        var other = t.CreateFile(@"target\desktop.ini", 100);
        t.CreateDir(@"target\emptydir");
        var f = Create(t);

        await f.Executor.CleanAsync(
            TestRules.Delete("r", @"%ROOT%\target", patterns: ["thumbcache_*.db", "iconcache_*.db"]), null, CancellationToken.None);

        Assert.False(File.Exists(thumb));
        Assert.False(File.Exists(icon));
        Assert.True(File.Exists(other));
        Assert.True(Directory.Exists(t.P(@"target\emptydir")));
    }

    [Fact]
    public async Task Junction_PointingOutsideRoot_TargetFilesRemainIntact()
    {
        using var t = new TempTree();
        var secret = t.CreateFile(@"outside\secret.txt", 500, 48);
        var secretSub = t.CreateFile(@"outside\nested\secret2.txt", 500, 48);
        var inside = t.CreateFile(@"target\inside.bin", 100, 48);
        if (!t.CreateJunction(@"target\link", t.P("outside"))) return; // không tạo được junction: bỏ qua
        var f = Create(t);

        var report = await f.Executor.CleanAsync(TestRules.Delete("r", @"%ROOT%\target"), null, CancellationToken.None);

        Assert.True(File.Exists(secret), "file đích của junction phải còn nguyên");
        Assert.True(File.Exists(secretSub));
        Assert.True(Directory.Exists(t.P("outside")));
        Assert.False(File.Exists(inside));
        Assert.Equal(100, report.BytesFreed); // không tính byte phía sau junction
    }

    [Fact]
    public async Task Junction_AsRuleRoot_IsNotFollowed()
    {
        using var t = new TempTree();
        var secret = t.CreateFile(@"outside\secret.txt", 500, 48);
        if (!t.CreateJunction("rootlink", t.P("outside"))) return;
        var f = Create(t);

        var report = await f.Executor.CleanAsync(TestRules.Delete("r", @"%ROOT%\rootlink"), null, CancellationToken.None);

        Assert.True(File.Exists(secret));
        Assert.Equal(0, report.FilesDeleted);
        Assert.NotEmpty(report.Errors);
    }

    [Fact]
    public async Task LockedFile_IsSkipped_NoThrow_OthersStillDeleted()
    {
        using var t = new TempTree();
        var locked = t.CreateFile(@"target\locked.bin", 100, 48);
        var free = t.CreateFile(@"target\free.bin", 200, 48);
        var f = Create(t);

        CleanReport report;
        using (new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            report = await f.Executor.CleanAsync(TestRules.Delete("r", @"%ROOT%\target"), null, CancellationToken.None);
        }

        Assert.True(File.Exists(locked));
        Assert.False(File.Exists(free));
        Assert.Equal(1, report.FilesSkipped);
        Assert.Equal(1, report.FilesDeleted);
        Assert.Equal(200, report.BytesFreed);
    }

    [Fact]
    public async Task ReadOnlyFile_IsDeleted()
    {
        using var t = new TempTree();
        var ro = t.CreateFile(@"target\ro.bin", 50, 48);
        File.SetAttributes(ro, FileAttributes.ReadOnly);
        var f = Create(t);

        var report = await f.Executor.CleanAsync(TestRules.Delete("r", @"%ROOT%\target"), null, CancellationToken.None);

        Assert.False(File.Exists(ro));
        Assert.Equal(1, report.FilesDeleted);
    }

    [Fact]
    public async Task BytesFreed_EqualsBytesScanned()
    {
        using var t = new TempTree();
        t.CreateFile(@"target\a.bin", 123, 48);
        t.CreateFile(@"target\s\b.bin", 4567, 48);
        t.CreateFile(@"target\s\t\c.bin", 89, 48);
        t.CreateFile(@"target\new.bin", 777, 1);
        var f = Create(t);
        var rule = TestRules.Delete("r", @"%ROOT%\target", minAgeHours: 24);

        var scan = await f.Scanner.ScanAsync(rule, CancellationToken.None);
        var report = await f.Executor.CleanAsync(rule, null, CancellationToken.None);

        Assert.Equal(scan.Bytes, report.BytesFreed);
        Assert.Equal(scan.FileCount, report.FilesDeleted);
        Assert.Equal(123 + 4567 + 89, report.BytesFreed);
    }

    [Fact]
    public async Task WildcardPaths_CleanEveryMatchingProfile()
    {
        using var t = new TempTree();
        var a = t.CreateFile(@"User Data\Default\Cache\f1", 100);
        var b = t.CreateFile(@"User Data\Profile 1\Cache\f2", 100);
        var keep = t.CreateFile(@"User Data\Default\Bookmarks", 100);
        var f = Create(t);

        var report = await f.Executor.CleanAsync(
            TestRules.Delete("chrome", @"%ROOT%\User Data\*\Cache"), null, CancellationToken.None);

        Assert.False(File.Exists(a));
        Assert.False(File.Exists(b));
        Assert.True(File.Exists(keep));
        Assert.Equal(200, report.BytesFreed);
    }

    [Fact]
    public async Task Cancel_MidWay_StopsAndLeavesRemainingFiles()
    {
        using var t = new TempTree();
        for (int i = 0; i < 40; i++) t.CreateFile($@"target\f{i:D2}.bin", 10, 48);
        var f = Create(t);
        using var cts = new CancellationTokenSource();
        var progress = new SyncProgress<CleanProgress>(_ => cts.Cancel()); // hủy ngay lần báo đầu tiên

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            f.Executor.CleanAsync(TestRules.Delete("r", @"%ROOT%\target"), progress, cts.Token));

        var remaining = Directory.GetFiles(t.P("target")).Length;
        Assert.InRange(remaining, 1, 39);
    }

    [Fact]
    public async Task ForbiddenRoot_IsRefused_NothingDeleted()
    {
        using var t = new TempTree();
        var victim = t.CreateFile(@"pf\app\important.dll", 100, 48);
        // Cho "Program Files" giả trỏ vào cây tạm: guard phải từ chối root nằm trong vùng cấm.
        var f = Create(t, env => env.Set("PROGRAMFILES", t.P("pf")));

        var report = await f.Executor.CleanAsync(TestRules.Delete("r", @"%ROOT%\pf\app"), null, CancellationToken.None);

        Assert.True(File.Exists(victim));
        Assert.Equal(0, report.FilesDeleted);
        Assert.NotEmpty(report.Errors);
    }

    [Fact]
    public async Task UserProfileDocuments_IsRefused()
    {
        using var t = new TempTree();
        var doc = t.CreateFile(@"profile\Documents\thesis.docx", 100, 48);
        var f = Create(t, env => env.Set("USERPROFILE", t.P("profile")));

        var report = await f.Executor.CleanAsync(TestRules.Delete("r", @"%ROOT%\profile\Documents"), null, CancellationToken.None);

        Assert.True(File.Exists(doc));
        Assert.Equal(0, report.FilesDeleted);
    }

    [Fact]
    public async Task RunCommand_Unavailable_ReportsError_AndDoesNotRun()
    {
        using var t = new TempTree();
        var f = Create(t);
        var rule = TestRules.Delete("npm", @"%ROOT%\cache") with
        { Action = RuleAction.RunCommand, Command = "npm", CommandArgs = "cache clean --force" };

        var report = await f.Executor.CleanAsync(rule, null, CancellationToken.None);

        Assert.Empty(f.Runner.Calls);
        Assert.Equal(0, report.BytesFreed);
        Assert.Single(report.Errors);
    }

    [Fact]
    public async Task RunCommand_MeasuresFreedSpaceBeforeAndAfter()
    {
        using var t = new TempTree();
        t.CreateFile(@"cache\a.bin", 1000);
        t.CreateFile(@"cache\b.bin", 500);
        var f = Create(t);
        f.Runner.Available.Add("npm");
        f.Runner.Behavior = (_, _) =>
        {
            File.Delete(t.P(@"cache\a.bin")); // giả lập lệnh dọn cache (chỉ trong cây tạm của test)
            return (0, "ok");
        };
        var rule = TestRules.Delete("npm", @"%ROOT%\cache") with
        { Action = RuleAction.RunCommand, Command = "npm", CommandArgs = "cache clean --force" };

        var report = await f.Executor.CleanAsync(rule, null, CancellationToken.None);

        Assert.Equal([("npm", "cache clean --force")], f.Runner.Calls);
        Assert.Equal(1000, report.BytesFreed);
        Assert.Empty(report.Errors);
    }

    [Fact]
    public async Task RunCommand_NonZeroExit_IsReportedAsError()
    {
        using var t = new TempTree();
        t.CreateDir("cache");
        var f = Create(t);
        f.Runner.Available.Add("docker");
        f.Runner.Behavior = (_, _) => (1, "daemon not running");
        var rule = TestRules.Delete("docker", @"%ROOT%\cache") with
        { Action = RuleAction.RunCommand, Command = "docker", CommandArgs = "system prune -f" };

        var report = await f.Executor.CleanAsync(rule, null, CancellationToken.None);

        Assert.Single(report.Errors);
        Assert.Contains("daemon not running", report.Errors[0]);
    }

    [Fact]
    public async Task RunCommand_NotWhitelistedCommand_IsNeverExecuted()
    {
        using var t = new TempTree();
        var f = Create(t);
        f.Runner.Available.Add("powershell");
        var rule = TestRules.Delete("evil", @"%ROOT%\cache") with
        { Action = RuleAction.RunCommand, Command = "powershell", CommandArgs = "-c whoami" };

        var report = await f.Executor.CleanAsync(rule, null, CancellationToken.None);

        Assert.Empty(f.Runner.Calls);
        Assert.Single(report.Errors);
    }

    [Fact]
    public async Task EmptyRecycleBin_UsesService()
    {
        using var t = new TempTree();
        var f = Create(t);
        f.Bin.Bytes = 4096;
        f.Bin.Items = 3;
        var rule = TestRules.Delete("rb", @"%ROOT%") with { Action = RuleAction.EmptyRecycleBin, Paths = [] };

        var report = await f.Executor.CleanAsync(rule, null, CancellationToken.None);

        Assert.True(f.Bin.Emptied);
        Assert.Equal(4096, report.BytesFreed);
        Assert.Equal(3, report.FilesDeleted);
    }

    [Fact]
    public async Task MissingPath_ProducesEmptyReport()
    {
        using var t = new TempTree();
        var f = Create(t);

        var report = await f.Executor.CleanAsync(TestRules.Delete("r", @"%ROOT%\nope"), null, CancellationToken.None);

        Assert.Equal(0, report.FilesDeleted);
        Assert.Equal(0, report.BytesFreed);
    }
}
