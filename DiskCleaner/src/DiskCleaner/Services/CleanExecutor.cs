using System.Diagnostics;
using DiskCleaner.Core;
using DiskCleaner.Core.Models;

namespace DiskCleaner.Services;

/// <summary>
/// NƠI DUY NHẤT trong ứng dụng được gọi File.Delete / Directory.Delete.
/// Mọi lần xóa đều phải qua SafetyGuardContext.IsSafeToDelete. Không bao giờ đi vào reparse point.
/// Hủy giữa chừng sẽ ném OperationCanceledException (những gì đã xóa được ghi trong log).
/// </summary>
public sealed class CleanExecutor
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromMinutes(5);

    private readonly SafetyGuardContext _guard;
    private readonly ICommandRunner _runner;
    private readonly IRecycleBinService _recycleBin;
    private readonly ILogger _log;
    private readonly string _appDataDir;
    private readonly string _appExeDir;

    public CleanExecutor(SafetyGuardContext guard, ICommandRunner runner, IRecycleBinService recycleBin, ILogger log,
        string? appDataDir = null, string? appExeDir = null)
    {
        _guard = guard;
        _runner = runner;
        _recycleBin = recycleBin;
        _log = log;
        _appDataDir = appDataDir ?? CustomRuleStore.DefaultAppDataDir(guard.Env);
        _appExeDir = appExeDir ?? AppContext.BaseDirectory;
    }

    public Task<CleanReport> CleanAsync(CleanRule rule, IProgress<CleanProgress>? progress, CancellationToken ct) =>
        Task.Run(() => CleanCoreAsync(rule, progress, ct), ct);

    private async Task<CleanReport> CleanCoreAsync(CleanRule rule, IProgress<CleanProgress>? progress, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var errors = new List<string>();
        var ctx = new Ctx(rule, progress, ct, errors);
        _log.Info($"=== BẮT ĐẦU rule '{rule.Id}' ({rule.Action}) ===");
        try
        {
            // Rule tùy chỉnh: kiểm tra lại chính sách ngay trước khi chạy (file JSON có thể bị sửa tay). Blocked thì dừng.
            if (rule.IsCustom && !CheckCustomRule(rule, errors))
            {
                sw.Stop();
                (_log as FileLogger)?.Flush();
                return new CleanReport(rule.Id, 0, 0, 0, errors, sw.Elapsed);
            }

            switch (rule.Action)
            {
                case RuleAction.DeleteFiles:
                    CleanFiles(ctx);
                    break;
                case RuleAction.EmptyRecycleBin:
                    CleanRecycleBin(ctx);
                    break;
                case RuleAction.RunCommand:
                    await CleanByCommandAsync(ctx).ConfigureAwait(false);
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            _log.Info($"--- HỦY rule '{rule.Id}': đã xóa {ctx.Deleted} file, {SizeFormatter.Format(ctx.Freed)} ---");
            (_log as FileLogger)?.Flush();
            throw;
        }
        catch (Exception ex)
        {
            errors.Add(ex.Message);
            _log.Error($"Lỗi rule '{rule.Id}'", ex);
        }

        sw.Stop();
        _log.Info($"=== XONG rule '{rule.Id}': xóa {ctx.Deleted}, bỏ qua {ctx.Skipped}, giải phóng {SizeFormatter.Format(ctx.Freed)} ({sw.Elapsed.TotalSeconds:F1}s) ===");
        (_log as FileLogger)?.Flush();
        return new CleanReport(rule.Id, ctx.Freed, ctx.Deleted, ctx.Skipped, errors, sw.Elapsed);
    }

    /// <summary>true nếu rule tùy chỉnh được phép chạy; ngược lại ghi lý do vào errors.</summary>
    private bool CheckCustomRule(CleanRule rule, List<string> errors)
    {
        string? reason = null;
        if (rule.Action != RuleAction.DeleteFiles || rule.RequiresAdmin || rule.Paths.Length != 1)
        {
            reason = "rule tùy chỉnh không hợp lệ (cần DeleteFiles, 1 path, không cần admin)";
        }
        else
        {
            var check = CustomRulePolicy.Evaluate(rule.Paths[0], _guard.Env, _guard.ForbiddenRoots, [], _appDataDir, _appExeDir);
            if (check.IsBlocked) reason = string.Join(", ", check.ReasonKeys);
        }
        if (reason == null) return true;

        var msg = $"Từ chối rule tùy chỉnh '{rule.Id}' ({(rule.Paths.Length > 0 ? rule.Paths[0] : "?")}): {reason}";
        _log.Error(msg);
        errors.Add(msg);
        return false;
    }

    private sealed class Ctx(CleanRule rule, IProgress<CleanProgress>? progress, CancellationToken ct, List<string> errors)
    {
        public CleanRule Rule { get; } = rule;
        public IProgress<CleanProgress>? Progress { get; } = progress;
        public CancellationToken Ct { get; } = ct;
        public List<string> Errors { get; } = errors;
        public DateTime Now { get; } = DateTime.UtcNow;
        public long Freed;
        public int Deleted;
        public int Skipped;
        public string[] AllowedRoots = [];
        public long LastReportTick = long.MinValue;

        public void Report(string message, bool force = false)
        {
            if (Progress == null) return;
            var tick = Environment.TickCount64;
            if (!force && LastReportTick != long.MinValue && tick - LastReportTick < 100) return;
            LastReportTick = tick;
            Progress.Report(new CleanProgress(Rule.Id, message, Deleted, Freed));
        }
    }

    // ---------- DeleteFiles ----------

    private void CleanFiles(Ctx c)
    {
        var roots = RulePaths.ExpandRoots(c.Rule, _guard.Env);
        if (roots.Count == 0) _log.Info("Không có path nào tồn tại, bỏ qua.");

        foreach (var root in roots)
        {
            c.Ct.ThrowIfCancellationRequested();

            if (_guard.IsForbidden(root))
            {
                var msg = $"Từ chối root nằm trong vùng cấm: {root}";
                _log.Error(msg);
                c.Errors.Add(msg);
                continue;
            }

            try
            {
                if ((new DirectoryInfo(root).Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    var msg = $"Bỏ qua root là reparse point: {root}";
                    _log.Info(msg);
                    c.Errors.Add(msg);
                    continue;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                c.Errors.Add($"Không đọc được {root}: {ex.Message}");
                continue;
            }

            c.AllowedRoots = [root];
            _log.Info($"Root: {root}");
            CleanDir(root, c);
        }
        c.Report("Hoàn tất", force: true);
    }

    private void CleanDir(string dir, Ctx c)
    {
        c.Ct.ThrowIfCancellationRequested();
        var entries = RulePaths.ListEntries(dir);
        if (entries == null)
        {
            c.Skipped++;
            _log.Info($"SKIP (không đọc được thư mục) {dir}");
            return;
        }

        foreach (var e in entries)
        {
            c.Ct.ThrowIfCancellationRequested();
            if (RulePaths.IsReparse(e))
            {
                HandleLink(e, c);
            }
            else if (RulePaths.IsDirectory(e))
            {
                var lastWrite = e.LastWriteTimeUtc; // lấy TRƯỚC khi xóa nội dung (xóa con làm đổi LastWrite)
                CleanDir(e.FullName, c);
                TryRemoveEmptyDir(e.FullName, lastWrite, c);
            }
            else
            {
                HandleFile(e, c);
            }
        }
    }

    private void HandleFile(FileSystemInfo e, Ctx c)
    {
        var rule = c.Rule;
        if (!FileFilter.ShouldDeleteAny(e.Attributes, e.LastWriteTimeUtc, c.Now, rule.MinAgeHours, rule.NamePatterns, e.Name))
            return;

        var path = e.FullName;
        if (!_guard.IsSafeToDelete(path, c.AllowedRoots))
        {
            c.Skipped++;
            _log.Error($"SafetyGuard từ chối: {path}");
            return;
        }

        long len = 0;
        try { len = ((FileInfo)e).Length; } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }

        try
        {
            if ((e.Attributes & FileAttributes.ReadOnly) != 0)
                File.SetAttributes(path, e.Attributes & ~FileAttributes.ReadOnly);
            File.Delete(path);
            c.Freed += len;
            c.Deleted++;
            _log.Info($"DEL {path} ({len} B)");
            c.Report(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            c.Skipped++;
            _log.Info($"SKIP {path}: {ex.Message}");
        }
    }

    /// <summary>Xóa CHÍNH link (junction/symlink), tuyệt đối không đụng nội dung đích.</summary>
    private void HandleLink(FileSystemInfo link, Ctx c)
    {
        if (!RulePaths.LinkQualifies(link, c.Rule, c.Now)) return;

        var path = link.FullName;
        if (!_guard.IsSafeToDelete(path, c.AllowedRoots))
        {
            c.Skipped++;
            _log.Error($"SafetyGuard từ chối link: {path}");
            return;
        }

        try
        {
            if (RulePaths.IsDirectory(link))
                Directory.Delete(path, false);   // RemoveDirectory trên reparse point: chỉ gỡ link
            else
                File.Delete(path);
            c.Deleted++;
            _log.Info($"DEL-LINK {path}");
            c.Report(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            c.Skipped++;
            _log.Info($"SKIP link {path}: {ex.Message}");
        }
    }

    private void TryRemoveEmptyDir(string dir, DateTime lastWriteBeforeUtc, Ctx c)
    {
        var rule = c.Rule;
        if (rule.NamePatterns.Length > 0) return;
        if (!RulePaths.AgeOk(lastWriteBeforeUtc, c.Now, rule.MinAgeHours)) return;
        if (!_guard.IsSafeToDelete(dir, c.AllowedRoots)) return;

        try
        {
            if (Directory.EnumerateFileSystemEntries(dir, "*", RulePaths.ListOptions).Any()) return;
            Directory.Delete(dir, false);
            _log.Info($"RMDIR {dir}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Info($"SKIP rmdir {dir}: {ex.Message}");
        }
    }

    // ---------- EmptyRecycleBin ----------

    private void CleanRecycleBin(Ctx c)
    {
        var (bytes, items) = _recycleBin.Query();
        _recycleBin.Empty();
        c.Freed = bytes;
        c.Deleted = (int)Math.Min(items, int.MaxValue);
        _log.Info($"Đã dọn Thùng rác: {items} mục, {bytes} B");
        c.Report("Đã dọn Thùng rác", force: true);
    }

    // ---------- RunCommand ----------

    private async Task CleanByCommandAsync(Ctx c)
    {
        var rule = c.Rule;
        if (string.IsNullOrWhiteSpace(rule.Command) || !RuleValidator.AllowedCommands.Contains(rule.Command))
        {
            c.Errors.Add($"Lệnh không được phép: '{rule.Command}'");
            return;
        }
        if (!_runner.IsAvailable(rule.Command))
        {
            c.Errors.Add($"Không tìm thấy lệnh '{rule.Command}' trên PATH");
            return;
        }

        long before = MeasureRoots(rule, c.Ct);
        var args = rule.CommandArgs ?? "";
        _log.Info($"RUN {rule.Command} {args}");
        c.Report($"Đang chạy {rule.Command} {args}", force: true);

        var (exit, output) = await _runner.RunAsync(rule.Command, args, CommandTimeout, c.Ct).ConfigureAwait(false);
        _log.Info($"Exit {exit}. Output: {output.Trim()}");
        if (exit != 0)
            c.Errors.Add($"'{rule.Command}' trả về mã {exit}: {Truncate(output.Trim(), 300)}");

        long after = MeasureRoots(rule, c.Ct);
        c.Freed = Math.Max(0, before - after);
        c.Report("Hoàn tất", force: true);
    }

    private long MeasureRoots(CleanRule rule, CancellationToken ct)
    {
        long total = 0;
        foreach (var root in RulePaths.ExpandRoots(rule, _guard.Env))
            total += DirectorySizer.Measure(root, ct).Bytes;
        return total;
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}
