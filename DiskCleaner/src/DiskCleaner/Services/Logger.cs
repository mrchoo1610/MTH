using System.Text;
using DiskCleaner.Core;

namespace DiskCleaner.Services;

public static class AppPaths
{
    public static string LogDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DiskCleaner", "logs");
}

/// <summary>Ghi log ra file, an toàn đa luồng, không bao giờ throw.</summary>
public sealed class FileLogger : ILogger, IDisposable
{
    private readonly object _lock = new();
    private StreamWriter? _writer;
    private bool _failed;

    public string FilePath { get; }

    public FileLogger(string filePath) => FilePath = filePath;

    /// <summary>Tạo file log mới dạng prefix-yyyyMMdd-HHmmss.log trong thư mục log mặc định.</summary>
    public static FileLogger CreateTimestamped(string prefix, string? directory = null)
    {
        var dir = directory ?? AppPaths.LogDirectory;
        return new FileLogger(Path.Combine(dir, $"{prefix}-{DateTime.Now:yyyyMMdd-HHmmss}.log"));
    }

    public void Info(string message) => Write("INFO ", message);

    public void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex == null ? message : $"{message} | {ex.GetType().Name}: {ex.Message}");

    private void Write(string level, string message)
    {
        lock (_lock)
        {
            if (_failed) return;
            try
            {
                if (_writer == null)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                    _writer = new StreamWriter(FilePath, true, new UTF8Encoding(false)) { AutoFlush = false };
                }
                _writer.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {level} {message}");
                if (level == "ERROR") _writer.Flush();
            }
            catch (Exception)
            {
                _failed = true;
            }
        }
    }

    public void Flush()
    {
        lock (_lock)
        {
            try { _writer?.Flush(); } catch (Exception) { }
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            try { _writer?.Dispose(); } catch (Exception) { }
            _writer = null;
        }
    }
}

/// <summary>Logger toàn cục của ứng dụng (một file app.log, ghi nối tiếp).</summary>
public static class AppLog
{
    public static FileLogger Instance { get; } = new(Path.Combine(AppPaths.LogDirectory, "app.log"));
}
