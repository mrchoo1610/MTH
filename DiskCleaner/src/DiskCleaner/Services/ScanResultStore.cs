using DiskCleaner.Core.Models;

namespace DiskCleaner.Services;

/// <summary>Kết quả quét dùng chung: Quét nhanh (Overview) và Quét (Clean) cùng ghi/đọc ở đây.</summary>
public sealed class ScanResultStore
{
    private readonly object _lock = new();
    private readonly Dictionary<string, ScanItemResult> _results = new(StringComparer.OrdinalIgnoreCase);

    public event EventHandler? Changed;

    public bool HasAny
    {
        get { lock (_lock) return _results.Count > 0; }
    }

    public IReadOnlyList<ScanItemResult> Snapshot()
    {
        lock (_lock) return _results.Values.ToList();
    }

    public bool TryGet(string ruleId, out ScanItemResult result)
    {
        lock (_lock)
        {
            if (_results.TryGetValue(ruleId, out var r)) { result = r; return true; }
        }
        result = null!;
        return false;
    }

    /// <summary>Ghi đè/thêm kết quả theo RuleId (giữ lại các rule khác).</summary>
    public void Update(IEnumerable<ScanItemResult> results)
    {
        lock (_lock)
        {
            foreach (var r in results) _results[r.RuleId] = r;
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        lock (_lock) _results.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
