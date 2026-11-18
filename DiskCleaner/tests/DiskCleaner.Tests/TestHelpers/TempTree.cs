using System.Diagnostics;
using DiskCleaner.Core;
using DiskCleaner.Core.Models;
using DiskCleaner.Services;

namespace DiskCleaner.Tests.TestHelpers;

/// <summary>
/// Cây thư mục tạm dưới Path.GetTempPath()/DiskCleanerTests/&lt;guid&gt;, tự xóa khi Dispose.
/// Test KHÔNG BAO GIỜ được đụng vào thư mục thật của người dùng.
/// </summary>
public sealed class TempTree : IDisposable
{
    private readonly List<string> _junctions = [];

    public string Root { get; }

    public TempTree()
    {
        Root = Path.Combine(Path.GetTempPath(), "DiskCleanerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string P(string relative) => Path.Combine(Root, relative);

    public string CreateDir(string relative)
    {
        var p = P(relative);
        Directory.CreateDirectory(p);
        return p;
    }

    /// <summary>Tạo file có đúng <paramref name="bytes"/> byte, LastWrite = now - ageHours.</summary>
    public string CreateFile(string relative, int bytes = 10, double ageHours = 0)
    {
        var p = P(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllBytes(p, new byte[bytes]);
        File.SetLastWriteTimeUtc(p, DateTime.UtcNow.AddHours(-ageHours));
        return p;
    }

    public void SetDirAge(string relative, double ageHours) =>
        Directory.SetLastWriteTimeUtc(P(relative), DateTime.UtcNow.AddHours(-ageHours));

    /// <summary>Tạo junction bằng mklink /J. Trả false nếu không tạo được (test tự bỏ qua).</summary>
    public bool CreateJunction(string linkRelative, string targetAbsolute)
    {
        var link = P(linkRelative);
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        try
        {
            var psi = new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{targetAbsolute}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var proc = Process.Start(psi)!;
            proc.StandardOutput.ReadToEnd();
            proc.StandardError.ReadToEnd();
            proc.WaitForExit();
            if (proc.ExitCode != 0 || !Directory.Exists(link)) return false;
            _junctions.Add(link);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public void Dispose()
    {
        // Gỡ junction trước để Directory.Delete đệ quy không bao giờ chạm tới thư mục đích.
        foreach (var j in _junctions)
        {
            try { if (Directory.Exists(j)) Directory.Delete(j, false); } catch (Exception) { }
        }
        try
        {
            if (Directory.Exists(Root) && Root.Contains("DiskCleanerTests", StringComparison.Ordinal))
                Directory.Delete(Root, true);
        }
        catch (Exception) { }
    }
}

public sealed class FakeEnvironment : IEnvironment
{
    private readonly Dictionary<string, string> _vars = new(StringComparer.OrdinalIgnoreCase);

    public string? Get(string name) => _vars.TryGetValue(name, out var v) ? v : null;

    public FakeEnvironment Set(string name, string value)
    {
        _vars[name] = value;
        return this;
    }

    /// <summary>Môi trường Windows giả (không liên quan máy thật).</summary>
    public static FakeEnvironment Standard() => new FakeEnvironment()
        .Set("WINDIR", @"C:\Windows")
        .Set("SystemRoot", @"C:\Windows")
        .Set("SystemDrive", "C:")
        .Set("PROGRAMFILES", @"C:\Program Files")
        .Set("PROGRAMFILES(X86)", @"C:\Program Files (x86)")
        .Set("PROGRAMDATA", @"C:\ProgramData")
        .Set("USERPROFILE", @"C:\Users\tester")
        .Set("LOCALAPPDATA", @"C:\Users\tester\AppData\Local")
        .Set("TEMP", @"C:\Users\tester\AppData\Local\Temp");
}

public sealed class FakeCommandRunner : ICommandRunner
{
    public HashSet<string> Available { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<(string Command, string Args)> Calls { get; } = [];
    public Func<string, string, (int, string)> Behavior { get; set; } = (_, _) => (0, "");

    public bool IsAvailable(string command) => Available.Contains(command);

    public Task<(int ExitCode, string Output)> RunAsync(string command, string args, TimeSpan timeout, CancellationToken ct)
    {
        Calls.Add((command, args));
        return Task.FromResult(Behavior(command, args));
    }
}

public sealed class FakeRecycleBin : IRecycleBinService
{
    public long Bytes { get; set; }
    public long Items { get; set; }
    public bool Emptied { get; private set; }
    public List<string> Sent { get; } = [];

    public (long Bytes, long Items) Query() => (Bytes, Items);
    public void Empty() => Emptied = true;
    public void SendToRecycleBin(IEnumerable<string> paths) => Sent.AddRange(paths);
}

/// <summary>IProgress chạy đồng bộ (Progress&lt;T&gt; thật post sang thread khác nên khó test).</summary>
public sealed class SyncProgress<T>(Action<T> handler) : IProgress<T>
{
    public void Report(T value) => handler(value);
}

public static class TestRules
{
    public static CleanRule Delete(string id, string path, int minAgeHours = 0, string[]? patterns = null,
        bool admin = false) => new()
    {
        Id = id,
        Name = id,
        Description = id,
        Category = RuleCategory.System,
        Action = RuleAction.DeleteFiles,
        Paths = [path],
        MinAgeHours = minAgeHours,
        NamePatterns = patterns ?? [],
        RequiresAdmin = admin
    };
}
