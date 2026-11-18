using System.Diagnostics;
using MiniTools.UpdateHelper;

// Usage: MiniTools.UpdateHelper.exe --pid <pid> --source <dir> --target <dir> --restart <exe> [--version <v>] [--backup <dir>] [--log <file>] [--deelevate]
var opts = ParseArgs(args);
var logPath = opts.GetValueOrDefault("log") ?? DefaultLogPath();
void Log(string m)
{
    try
    {
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
        File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {m}{Environment.NewLine}");
    }
    catch (Exception) { }
}

try
{
    if (!opts.TryGetValue("source", out var source) || !opts.TryGetValue("target", out var target) || !opts.TryGetValue("restart", out var restart))
    {
        Log("Missing arguments (need --source, --target, --restart).");
        return 64;
    }
    var version = opts.GetValueOrDefault("version") ?? "new";
    Log($"Helper started. source={source} target={target} restart={restart} version={version}");

    if (opts.TryGetValue("pid", out var pidText) && int.TryParse(pidText, out var pid))
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            if (!p.WaitForExit(TimeSpan.FromSeconds(60)))
            {
                Log("Timed out waiting for the app to exit; nothing was changed.");
                return 3;
            }
        }
        catch (ArgumentException) { /* already exited */ }
    }

    var targetFull = Path.GetFullPath(target).TrimEnd('\\', '/');
    var backup = opts.GetValueOrDefault("backup")
        ?? Path.Combine(Path.GetDirectoryName(targetFull) ?? targetFull, Path.GetFileName(targetFull) + ".bak-" + version);

    var result = new UpdateApplier(Log).Apply(source, targetFull, backup);
    Log(result.Success ? "Update applied." : $"Update FAILED ({result.Error}). Rolled back: {result.RolledBack}.");

    Restart(restart, targetFull, opts.ContainsKey("deelevate"), Log);
    return result.Success ? 0 : 1;
}
catch (Exception ex)
{
    Log("Helper crashed: " + ex);
    return 2;
}

static void Restart(string exe, string workDir, bool deElevate, Action<string> log)
{
    try
    {
        // After a UAC prompt this process is elevated; start the app through explorer so it runs as the normal user.
        var psi = deElevate
            ? new ProcessStartInfo("explorer.exe", "\"" + exe + "\"") { UseShellExecute = false }
            : new ProcessStartInfo(exe) { WorkingDirectory = workDir, UseShellExecute = true };
        Process.Start(psi);
        log("Restarted " + exe);
    }
    catch (Exception ex)
    {
        log("Could not restart the app: " + ex.Message);
    }
}

static string DefaultLogPath()
{
    var env = Environment.GetEnvironmentVariable("MINITOOLS_DATA_DIR");
    var root = !string.IsNullOrWhiteSpace(env)
        ? env
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MiniTools");
    return Path.Combine(root, "updates", "helper.log");
}

static Dictionary<string, string> ParseArgs(string[] a)
{
    var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < a.Length; i++)
    {
        if (!a[i].StartsWith("--", StringComparison.Ordinal)) continue;
        var key = a[i][2..];
        d[key] = i + 1 < a.Length && !a[i + 1].StartsWith("--", StringComparison.Ordinal) ? a[++i] : "";
    }
    return d;
}
