using System.Diagnostics;
using System.Text;

namespace DiskCleaner.Services;

public interface ICommandRunner
{
    /// <summary>Tìm trên PATH (.exe, .cmd, .bat, .ps1).</summary>
    bool IsAvailable(string command);

    Task<(int ExitCode, string Output)> RunAsync(string command, string args, TimeSpan timeout, CancellationToken ct);
}

public sealed class CommandRunner : ICommandRunner
{
    private static readonly string[] Extensions = [".exe", ".cmd", ".bat", ".ps1"];

    public string? Resolve(string command)
    {
        if (string.IsNullOrWhiteSpace(command) || command.IndexOfAny(['\\', '/', ':']) >= 0)
            return null;
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var ext in Extensions)
            {
                try
                {
                    var candidate = Path.Combine(dir.Trim().Trim('"'), command + ext);
                    if (File.Exists(candidate)) return candidate;
                }
                catch (Exception ex) when (ex is ArgumentException or IOException) { }
            }
        }
        return null;
    }

    public bool IsAvailable(string command) => Resolve(command) != null;

    public async Task<(int ExitCode, string Output)> RunAsync(string command, string args, TimeSpan timeout, CancellationToken ct)
    {
        var resolved = Resolve(command);
        if (resolved == null) return (-1, $"Không tìm thấy lệnh '{command}' trên PATH");

        var psi = new ProcessStartInfo
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        var ext = Path.GetExtension(resolved).ToLowerInvariant();
        if (ext is ".cmd" or ".bat")
        {
            psi.FileName = "cmd.exe";
            psi.Arguments = $"/c \"\"{resolved}\" {args}\"";
        }
        else if (ext == ".ps1")
        {
            psi.FileName = "powershell.exe";
            psi.Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{resolved}\" {args}";
        }
        else
        {
            psi.FileName = resolved;
            psi.Arguments = args;
        }

        var output = new StringBuilder();
        using var proc = new Process { StartInfo = psi };
        proc.OutputDataReceived += (_, e) => { if (e.Data != null) lock (output) output.AppendLine(e.Data); };
        proc.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (output) output.AppendLine(e.Data); };

        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
        try
        {
            await proc.WaitForExitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { proc.Kill(entireProcessTree: true); } catch (Exception) { }
            if (ct.IsCancellationRequested) throw;
            return (-1, "Hết thời gian chờ (timeout)");
        }

        lock (output) return (proc.ExitCode, output.ToString());
    }
}
