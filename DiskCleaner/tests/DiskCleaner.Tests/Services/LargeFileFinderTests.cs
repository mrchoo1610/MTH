using DiskCleaner.Services;
using DiskCleaner.Tests.TestHelpers;

namespace DiskCleaner.Tests.Services;

public class LargeFileFinderTests
{
    private static void Populate(TempTree t)
    {
        t.CreateFile("f10.bin", 10);
        t.CreateFile(@"d1\f50.bin", 50);
        t.CreateFile(@"d1\d2\f30.bin", 30);
        t.CreateFile(@"d3\f70.bin", 70);
        t.CreateFile(@"d3\f20.bin", 20);
    }

    [Fact]
    public async Task ReturnsTopN_InDescendingOrder()
    {
        using var t = new TempTree();
        Populate(t);

        var result = await new LargeFileFinder().FindAsync(t.Root, 0, 3, CancellationToken.None);

        Assert.Equal([70L, 50L, 30L], result.Select(r => r.SizeBytes));
        Assert.EndsWith("f70.bin", result[0].FullPath);
    }

    [Fact]
    public async Task RespectsMinBytes()
    {
        using var t = new TempTree();
        Populate(t);

        var result = await new LargeFileFinder().FindAsync(t.Root, 30, 100, CancellationToken.None);

        Assert.Equal([70L, 50L, 30L], result.Select(r => r.SizeBytes));
    }

    [Fact]
    public async Task MaxResultsLargerThanFileCount_ReturnsAll()
    {
        using var t = new TempTree();
        Populate(t);

        var result = await new LargeFileFinder().FindAsync(t.Root, 0, 100, CancellationToken.None);

        Assert.Equal(5, result.Count);
        Assert.Equal(result.OrderByDescending(r => r.SizeBytes).Select(r => r.SizeBytes), result.Select(r => r.SizeBytes));
    }

    [Fact]
    public async Task ZeroMaxResults_ReturnsEmpty()
    {
        using var t = new TempTree();
        Populate(t);
        Assert.Empty(await new LargeFileFinder().FindAsync(t.Root, 0, 0, CancellationToken.None));
    }

    [Fact]
    public async Task DoesNotFollowJunction()
    {
        using var t = new TempTree();
        t.CreateFile(@"scan\small.bin", 10);
        t.CreateFile(@"outside\huge.bin", 99999);
        if (!t.CreateJunction(@"scan\link", t.P("outside"))) return;

        var result = await new LargeFileFinder().FindAsync(t.P("scan"), 0, 10, CancellationToken.None);

        Assert.Single(result);
        Assert.EndsWith("small.bin", result[0].FullPath);
    }

    [Fact]
    public async Task Cancelled_Throws()
    {
        using var t = new TempTree();
        Populate(t);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new LargeFileFinder().FindAsync(t.Root, 0, 10, cts.Token));
    }
}
