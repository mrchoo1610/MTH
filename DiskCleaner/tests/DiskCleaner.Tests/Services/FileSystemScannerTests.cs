using DiskCleaner.Core.Models;
using DiskCleaner.Services;
using DiskCleaner.Tests.TestHelpers;

namespace DiskCleaner.Tests.Services;

public class FileSystemScannerTests
{
    private static (FileSystemScanner Scanner, FakeCommandRunner Runner, FakeRecycleBin Bin) Create(TempTree t)
    {
        var env = FakeEnvironment.Standard().Set("ROOT", t.Root);
        var runner = new FakeCommandRunner();
        var bin = new FakeRecycleBin();
        return (new FileSystemScanner(env, runner, bin), runner, bin);
    }

    [Fact]
    public async Task CountsTotalBytesAndFiles()
    {
        using var t = new TempTree();
        t.CreateFile(@"target\a.bin", 100, 48);
        t.CreateFile(@"target\sub\b.bin", 200, 48);
        t.CreateFile(@"target\sub\deep\c.bin", 300, 48);
        var (scanner, _, _) = Create(t);

        var r = await scanner.ScanAsync(TestRules.Delete("r", @"%ROOT%\target"), CancellationToken.None);

        Assert.True(r.Available);
        Assert.Equal(600, r.Bytes);
        Assert.Equal(3, r.FileCount);
        Assert.Null(r.Error);
    }

    [Fact]
    public async Task RespectsMinAge()
    {
        using var t = new TempTree();
        t.CreateFile(@"target\old.bin", 100, 48);
        t.CreateFile(@"target\sub\old2.bin", 200, 30);
        t.CreateFile(@"target\new.bin", 999, 1);
        var (scanner, _, _) = Create(t);

        var r = await scanner.ScanAsync(TestRules.Delete("r", @"%ROOT%\target", minAgeHours: 24), CancellationToken.None);

        Assert.Equal(300, r.Bytes);
        Assert.Equal(2, r.FileCount);
    }

    [Fact]
    public async Task RespectsNamePatterns()
    {
        using var t = new TempTree();
        t.CreateFile(@"target\thumbcache_32.db", 100);
        t.CreateFile(@"target\iconcache_32.db", 50);
        t.CreateFile(@"target\keep.txt", 999);
        var (scanner, _, _) = Create(t);

        var rule = TestRules.Delete("r", @"%ROOT%\target", patterns: ["thumbcache_*.db", "iconcache_*.db"]);
        var r = await scanner.ScanAsync(rule, CancellationToken.None);

        Assert.Equal(150, r.Bytes);
        Assert.Equal(2, r.FileCount);
    }

    [Fact]
    public async Task MissingPath_IsNotAvailable()
    {
        using var t = new TempTree();
        var (scanner, _, _) = Create(t);

        var r = await scanner.ScanAsync(TestRules.Delete("r", @"%ROOT%\nope"), CancellationToken.None);

        Assert.False(r.Available);
        Assert.Equal(0, r.Bytes);
    }

    [Fact]
    public async Task DoesNotFollowJunction()
    {
        using var t = new TempTree();
        t.CreateFile(@"outside\big.bin", 5000, 48);
        t.CreateFile(@"target\a.bin", 100, 48);
        if (!t.CreateJunction(@"target\link", t.P("outside"))) return; // không tạo được junction: bỏ qua
        var (scanner, _, _) = Create(t);

        var r = await scanner.ScanAsync(TestRules.Delete("r", @"%ROOT%\target"), CancellationToken.None);

        Assert.Equal(100, r.Bytes);
    }

    [Fact]
    public async Task RunCommand_UnavailableCommand_IsNotAvailable()
    {
        using var t = new TempTree();
        var (scanner, _, _) = Create(t);
        var rule = TestRules.Delete("npm", @"%ROOT%\cache") with
        { Action = RuleAction.RunCommand, Command = "npm", CommandArgs = "cache clean --force" };

        var r = await scanner.ScanAsync(rule, CancellationToken.None);

        Assert.False(r.Available);
    }

    [Fact]
    public async Task RunCommand_AvailableCommand_MeasuresPaths_AndNeverRunsIt()
    {
        using var t = new TempTree();
        t.CreateFile(@"cache\a.bin", 400);
        t.CreateFile(@"cache\x\b.bin", 100);
        var (scanner, runner, _) = Create(t);
        runner.Available.Add("npm");
        var rule = TestRules.Delete("npm", @"%ROOT%\cache") with
        { Action = RuleAction.RunCommand, Command = "npm", CommandArgs = "cache clean --force" };

        var r = await scanner.ScanAsync(rule, CancellationToken.None);

        Assert.True(r.Available);
        Assert.Equal(500, r.Bytes);
        Assert.Empty(runner.Calls); // dry-run không chạy lệnh
    }

    [Fact]
    public async Task RecycleBin_UsesQuery_NotEmpty()
    {
        using var t = new TempTree();
        var (scanner, _, bin) = Create(t);
        bin.Bytes = 12345;
        bin.Items = 7;
        var rule = TestRules.Delete("rb", @"%ROOT%") with { Action = RuleAction.EmptyRecycleBin, Paths = [] };

        var r = await scanner.ScanAsync(rule, CancellationToken.None);

        Assert.Equal(12345, r.Bytes);
        Assert.Equal(7, r.FileCount);
        Assert.False(bin.Emptied);
    }

    [Fact]
    public async Task ScanAll_ReturnsOneResultPerRule_AndReportsProgress()
    {
        using var t = new TempTree();
        t.CreateFile(@"a\f.bin", 10);
        t.CreateFile(@"b\f.bin", 20);
        var (scanner, _, _) = Create(t);
        var names = new List<string>();

        var results = await scanner.ScanAllAsync(
            [TestRules.Delete("a", @"%ROOT%\a"), TestRules.Delete("b", @"%ROOT%\b")],
            new SyncProgress<string>(names.Add), CancellationToken.None);

        Assert.Equal(2, results.Count);
        Assert.Equal(["a", "b"], names);
        Assert.Equal(30, results.Sum(x => x.Bytes));
    }

    [Fact]
    public async Task Cancelled_Throws()
    {
        using var t = new TempTree();
        t.CreateFile(@"a\f.bin", 10);
        var (scanner, _, _) = Create(t);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            scanner.ScanAllAsync([TestRules.Delete("a", @"%ROOT%\a")], null, cts.Token));
    }
}
