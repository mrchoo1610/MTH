using DiskCleaner.Services;
using DiskCleaner.Tests.TestHelpers;

namespace DiskCleaner.Tests.Services;

public class FolderAnalyzerTests
{
    private static void Populate(TempTree t)
    {
        t.CreateFile("root.bin", 10);
        t.CreateFile(@"a\a1.bin", 100);
        t.CreateFile(@"a\a2.bin", 200);
        t.CreateFile(@"a\b\b1.bin", 300);
        t.CreateFile(@"c\c1.bin", 50);
        t.CreateDir("empty");
    }

    [Fact]
    public async Task AggregatesSizesAndCounts()
    {
        using var t = new TempTree();
        Populate(t);

        var root = await new FolderAnalyzer().AnalyzeAsync(t.Root, null, CancellationToken.None);

        Assert.Equal(660, root.SizeBytes);
        Assert.Equal(5, root.FileCount);
        var a = root.Children.Single(c => c.Name == "a");
        Assert.Equal(600, a.SizeBytes);
        Assert.Equal(3, a.FileCount);
        Assert.Equal(300, a.Children.Single(c => c.Name == "b").SizeBytes);
        Assert.Equal(50, root.Children.Single(c => c.Name == "c").SizeBytes);
    }

    [Fact]
    public async Task ChildrenSortedBySizeDescending()
    {
        using var t = new TempTree();
        Populate(t);

        var root = await new FolderAnalyzer().AnalyzeAsync(t.Root, null, CancellationToken.None);

        Assert.Equal(["a", "c", "empty"], root.Children.Select(c => c.Name));
        Assert.Equal(root.Children.OrderByDescending(c => c.SizeBytes).Select(c => c.Name), root.Children.Select(c => c.Name));
    }

    [Fact]
    public async Task DoesNotFollowJunction()
    {
        using var t = new TempTree();
        t.CreateFile(@"real\r.bin", 100);
        t.CreateFile(@"outside\big.bin", 100000);
        if (!t.CreateJunction(@"real\link", t.P("outside"))) return;
        // Quét riêng thư mục "real": junction bên trong không được tính.

        var node = await new FolderAnalyzer().AnalyzeAsync(t.P("real"), null, CancellationToken.None);

        Assert.Equal(100, node.SizeBytes);
        Assert.DoesNotContain(node.Children, c => c.Name == "link");
    }

    [Fact]
    public async Task MaxDepth_TrimsNodesButKeepsTotals()
    {
        using var t = new TempTree();
        Populate(t);

        var root = await new FolderAnalyzer().AnalyzeAsync(t.Root, null, CancellationToken.None, maxDepth: 1);

        Assert.Equal(660, root.SizeBytes);
        var a = root.Children.Single(c => c.Name == "a");
        Assert.Equal(600, a.SizeBytes);
        Assert.Empty(a.Children);
    }

    [Fact]
    public async Task Cancelled_Throws()
    {
        using var t = new TempTree();
        Populate(t);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new FolderAnalyzer().AnalyzeAsync(t.Root, null, cts.Token));
    }
}
