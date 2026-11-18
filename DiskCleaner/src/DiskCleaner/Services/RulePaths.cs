using System.Security;
using DiskCleaner.Core;
using DiskCleaner.Core.Models;

namespace DiskCleaner.Services;

/// <summary>Các hàm dùng chung cho FileSystemScanner (dry-run) và CleanExecutor để số liệu khớp nhau. Không xóa gì ở đây.</summary>
internal static class RulePaths
{
    public static readonly EnumerationOptions ListOptions = new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = true,
        AttributesToSkip = 0,
        ReturnSpecialDirectories = false
    };

    public static IReadOnlyList<string> ExpandRoots(CleanRule rule, IEnvironment env)
    {
        var roots = new List<string>();
        foreach (var p in rule.Paths)
            roots.AddRange(PathExpander.Expand(p, env, ListDirs));
        return roots.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static IEnumerable<string> ListDirs(string dir) =>
        Directory.EnumerateDirectories(dir, "*", ListOptions);

    /// <summary>Liệt kê 1 cấp; null nếu không đọc được.</summary>
    public static List<FileSystemInfo>? ListEntries(string dir)
    {
        try
        {
            return new DirectoryInfo(dir).EnumerateFileSystemInfos("*", ListOptions).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            return null;
        }
    }

    private static readonly EnumerationOptions StrictOptions = new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = false,
        AttributesToSkip = 0,
        ReturnSpecialDirectories = false
    };

    /// <summary>true nếu KHÔNG đủ quyền liệt kê thư mục (ListOptions nuốt lỗi này nên cần thử lại ở chế độ strict).</summary>
    public static bool IsAccessDenied(string dir)
    {
        try
        {
            using var en = Directory.EnumerateFileSystemEntries(dir, "*", StrictOptions).GetEnumerator();
            en.MoveNext();
            return false;
        }
        catch (UnauthorizedAccessException) { return true; }
        catch (SecurityException) { return true; }
        catch (IOException) { return false; }
    }

    public static bool IsReparse(FileSystemInfo e) => (e.Attributes & FileAttributes.ReparsePoint) != 0;

    public static bool IsDirectory(FileSystemInfo e) => (e.Attributes & FileAttributes.Directory) != 0;

    public static bool AgeOk(DateTime lastWriteUtc, DateTime nowUtc, int minAgeHours) =>
        minAgeHours <= 0 || nowUtc - lastWriteUtc >= TimeSpan.FromHours(minAgeHours);

    /// <summary>Link (junction/symlink) có đủ điều kiện để xóa CHÍNH LINK (không đụng đích).</summary>
    public static bool LinkQualifies(FileSystemInfo link, CleanRule rule, DateTime nowUtc)
    {
        if (IsDirectory(link))
            return rule.NamePatterns.Length == 0 && AgeOk(link.LastWriteTimeUtc, nowUtc, rule.MinAgeHours);
        return FileFilter.ShouldDeleteAny(link.Attributes & ~FileAttributes.ReparsePoint, link.LastWriteTimeUtc,
            nowUtc, rule.MinAgeHours, rule.NamePatterns, link.Name);
    }
}

/// <summary>Đo dung lượng thư mục, không đi vào reparse point.</summary>
internal static class DirectorySizer
{
    public static (long Bytes, int Files) Measure(string root, CancellationToken ct)
    {
        long bytes = 0;
        int files = 0;
        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var dir = stack.Pop();
            var entries = RulePaths.ListEntries(dir);
            if (entries == null) continue;
            foreach (var e in entries)
            {
                if (RulePaths.IsReparse(e)) continue;
                if (RulePaths.IsDirectory(e)) { stack.Push(e.FullName); continue; }
                try { bytes += ((FileInfo)e).Length; files++; }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
        return (bytes, files);
    }
}
