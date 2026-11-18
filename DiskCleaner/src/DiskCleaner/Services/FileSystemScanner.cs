using DiskCleaner.Core;
using DiskCleaner.Core.Models;

namespace DiskCleaner.Services;

/// <summary>Dry-run: chỉ đo, không xóa. Dùng cùng điều kiện lọc với CleanExecutor.</summary>
public sealed class FileSystemScanner
{
    private readonly IEnvironment _env;
    private readonly ICommandRunner _runner;
    private readonly IRecycleBinService _recycleBin;

    public FileSystemScanner(IEnvironment env, ICommandRunner runner, IRecycleBinService recycleBin)
    {
        _env = env;
        _runner = runner;
        _recycleBin = recycleBin;
    }

    public Task<ScanItemResult> ScanAsync(CleanRule rule, CancellationToken ct) =>
        Task.Run(() => Scan(rule, ct), ct);

    public async Task<IReadOnlyList<ScanItemResult>> ScanAllAsync(IEnumerable<CleanRule> rules,
        IProgress<string>? progress, CancellationToken ct)
    {
        var results = new List<ScanItemResult>();
        foreach (var rule in rules)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report(rule.Name);
            results.Add(await ScanAsync(rule, ct).ConfigureAwait(false));
        }
        return results;
    }

    private ScanItemResult Scan(CleanRule rule, CancellationToken ct)
    {
        try
        {
            return rule.Action switch
            {
                RuleAction.EmptyRecycleBin => ScanRecycleBin(rule),
                RuleAction.RunCommand => ScanCommand(rule, ct),
                _ => ScanDelete(rule, ct)
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new ScanItemResult(rule.Id, 0, 0, true, ex.Message);
        }
    }

    private ScanItemResult ScanRecycleBin(CleanRule rule)
    {
        var (bytes, items) = _recycleBin.Query();
        return new ScanItemResult(rule.Id, bytes, (int)Math.Min(items, int.MaxValue), true, null);
    }

    private ScanItemResult ScanCommand(CleanRule rule, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rule.Command) || !_runner.IsAvailable(rule.Command))
            return new ScanItemResult(rule.Id, 0, 0, false, $"Không tìm thấy lệnh '{rule.Command}'");

        long bytes = 0;
        int files = 0;
        foreach (var root in RulePaths.ExpandRoots(rule, _env))
        {
            var (b, f) = DirectorySizer.Measure(root, ct);
            bytes += b;
            files += f;
        }
        return new ScanItemResult(rule.Id, bytes, files, true, null);
    }

    private ScanItemResult ScanDelete(CleanRule rule, CancellationToken ct)
    {
        var roots = RulePaths.ExpandRoots(rule, _env);
        if (roots.Count == 0) return new ScanItemResult(rule.Id, 0, 0, false, null);

        var now = DateTime.UtcNow;
        long bytes = 0;
        int count = 0;
        bool anyDenied = false;
        foreach (var root in roots)
        {
            try
            {
                if ((new DirectoryInfo(root).Attributes & FileAttributes.ReparsePoint) != 0) continue;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
            if (RulePaths.IsAccessDenied(root)) { anyDenied = true; continue; }
            Walk(root, rule, now, ct, ref bytes, ref count);
        }
        // Không đọc được thư mục thì 0 B là "không biết", không phải "trống".
        return new ScanItemResult(rule.Id, bytes, count, true, null, AccessDenied: anyDenied && count == 0);
    }

    private static void Walk(string dir, CleanRule rule, DateTime now, CancellationToken ct, ref long bytes, ref int count)
    {
        ct.ThrowIfCancellationRequested();
        var entries = RulePaths.ListEntries(dir);
        if (entries == null) return;
        foreach (var e in entries)
        {
            ct.ThrowIfCancellationRequested();
            if (RulePaths.IsReparse(e))
            {
                if (RulePaths.LinkQualifies(e, rule, now)) count++;
            }
            else if (RulePaths.IsDirectory(e))
            {
                Walk(e.FullName, rule, now, ct, ref bytes, ref count);
            }
            else if (FileFilter.ShouldDeleteAny(e.Attributes, e.LastWriteTimeUtc, now, rule.MinAgeHours, rule.NamePatterns, e.Name))
            {
                try { bytes += ((FileInfo)e).Length; count++; }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
    }
}
