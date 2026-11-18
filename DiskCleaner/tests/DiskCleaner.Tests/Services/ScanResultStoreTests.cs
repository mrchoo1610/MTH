using DiskCleaner.Core.Models;
using DiskCleaner.Services;

namespace DiskCleaner.Tests.Services;

public class ScanResultStoreTests
{
    [Fact]
    public void Update_MergesAndRaisesChanged_ClearEmpties()
    {
        var store = new ScanResultStore();
        int changed = 0;
        store.Changed += (_, _) => changed++;
        Assert.False(store.HasAny);

        store.Update([new ScanItemResult("a", 10, 1, true, null)]);
        store.Update([new ScanItemResult("b", 0, 0, true, null, AccessDenied: true), new ScanItemResult("a", 20, 2, true, null)]);

        Assert.Equal(2, changed);
        Assert.True(store.TryGet("a", out var a));
        Assert.Equal(20, a.Bytes);
        Assert.True(store.TryGet("B", out var b));
        Assert.True(b.AccessDenied);

        store.Clear();
        Assert.False(store.HasAny);
        Assert.Equal(3, changed);
    }
}
