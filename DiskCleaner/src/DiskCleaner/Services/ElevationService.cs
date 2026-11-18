using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using System.Text.Json;
using DiskCleaner.Core;
using DiskCleaner.Core.Models;

namespace DiskCleaner.Services;

public sealed record ElevatedJob(string[] RuleIds);

public sealed record ElevatedResult(bool Completed, List<CleanReport> Reports);

public sealed class ElevationService
{
    public const string ElevatedArg = "--elevated-clean";

    public bool IsElevated
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    /// <summary>
    /// Ghi danh sách ruleId ra file job trong %TEMP%\DiskCleaner, chạy lại chính exe với Verb=runas,
    /// chờ kết thúc rồi đọc file kết quả. Trả null nếu người dùng bấm "No" ở UAC.
    /// </summary>
    public async Task<IReadOnlyList<CleanReport>?> RunElevatedCleanAsync(IEnumerable<string> ruleIds, CancellationToken ct)
    {
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Không xác định được đường dẫn exe.");
        var dir = Path.Combine(Path.GetTempPath(), "DiskCleaner");
        Directory.CreateDirectory(dir);

        // Tên file cố định (ghi đè mỗi lần) để không phải xóa file tạm.
        var jobFile = Path.Combine(dir, "clean-job.json");
        var resultFile = jobFile + ".result.json";
        File.WriteAllText(jobFile, JsonSerializer.Serialize(new ElevatedJob(ruleIds.ToArray()), JsonDefaults.Options));
        File.WriteAllText(resultFile, "null"); // xóa kết quả cũ

        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = $"{ElevatedArg} \"{jobFile}\"",
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden
        };

        Process? proc;
        try
        {
            proc = Process.Start(psi);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return null; // người dùng từ chối UAC
        }
        if (proc == null) return null;

        using (proc)
        {
            try
            {
                await proc.WaitForExitAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try { proc.Kill(entireProcessTree: true); } catch (Exception) { }
                throw;
            }
        }

        var json = File.ReadAllText(resultFile);
        var result = JsonSerializer.Deserialize<ElevatedResult>(json, JsonDefaults.Options);
        if (result is not { Completed: true })
            throw new InvalidOperationException("Tiến trình quyền admin không trả về kết quả hợp lệ.");
        return result.Reports;
    }
}

/// <summary>Chế độ --elevated-clean: không mở cửa sổ, chỉ chạy rule RequiresAdmin (DeleteFiles) có trong rules.json.</summary>
public static class ElevatedCleanRunner
{
    /// <returns>0 nếu thành công.</returns>
    public static int Run(string jobFile, string rulesPath, IEnvironment env, ICommandRunner runner,
        IRecycleBinService recycleBin, ILogger log)
    {
        var resultFile = jobFile + ".result.json";
        try
        {
            var job = JsonSerializer.Deserialize<ElevatedJob>(File.ReadAllText(jobFile), JsonDefaults.Options);
            var requested = new HashSet<string>(job?.RuleIds ?? [], StringComparer.OrdinalIgnoreCase);

            // Không tin path nào trong job: chỉ lấy Id, rule lấy từ rules.json đã validate. KHÔNG bao giờ nạp custom-rules.json (rule tùy chỉnh chỉ chạy quyền thường).
            var rules = new RuleRepository(rulesPath, env, log).LoadAll()
                .Where(r => requested.Contains(r.Id) && r.RequiresAdmin && !r.IsCustom && r.Action == RuleAction.DeleteFiles)
                .ToList();

            var executor = new CleanExecutor(new SafetyGuardContext(env), runner, recycleBin, log);
            var reports = new List<CleanReport>();
            foreach (var rule in rules)
                reports.Add(executor.CleanAsync(rule, null, CancellationToken.None).GetAwaiter().GetResult());

            File.WriteAllText(resultFile, JsonSerializer.Serialize(new ElevatedResult(true, reports), JsonDefaults.Options));
            return 0;
        }
        catch (Exception ex)
        {
            log.Error("Elevated clean thất bại", ex);
            try { File.WriteAllText(resultFile, JsonSerializer.Serialize(new ElevatedResult(false, []), JsonDefaults.Options)); }
            catch (Exception) { }
            return 1;
        }
    }
}
