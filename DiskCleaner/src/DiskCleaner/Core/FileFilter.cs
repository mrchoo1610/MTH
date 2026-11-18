namespace DiskCleaner.Core;

public static class FileFilter
{
    private const FileAttributes RecallOnOpen = (FileAttributes)0x40000;
    private const FileAttributes RecallOnDataAccess = (FileAttributes)0x400000;

    public static bool ShouldDelete(FileAttributes attrs, DateTime lastWriteUtc, DateTime nowUtc,
                                    int minAgeHours, string? namePattern, string fileName)
    {
        // Không đụng vào link, file OneDrive/offline (tránh kích hoạt tải về).
        if ((attrs & (FileAttributes.ReparsePoint | FileAttributes.Offline | RecallOnOpen | RecallOnDataAccess)) != 0)
            return false;
        if (minAgeHours > 0 && nowUtc - lastWriteUtc < TimeSpan.FromHours(minAgeHours))
            return false;
        if (!string.IsNullOrEmpty(namePattern) && !Glob.IsMatch(namePattern, fileName))
            return false;
        return true;
    }

    /// <summary>Như ShouldDelete nhưng với nhiều pattern (rỗng = mọi tên).</summary>
    public static bool ShouldDeleteAny(FileAttributes attrs, DateTime lastWriteUtc, DateTime nowUtc,
                                       int minAgeHours, IReadOnlyList<string> namePatterns, string fileName)
    {
        if (namePatterns.Count == 0)
            return ShouldDelete(attrs, lastWriteUtc, nowUtc, minAgeHours, null, fileName);
        foreach (var p in namePatterns)
            if (ShouldDelete(attrs, lastWriteUtc, nowUtc, minAgeHours, p, fileName))
                return true;
        return false;
    }
}
