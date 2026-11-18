using System.Globalization;

namespace DiskCleaner.Core;

public static class SizeFormatter
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB", "PB", "EB"];

    /// <summary>"0 B", "1023 B", "1 KB", "1.5 GB" (tối đa 2 chữ số thập phân, bỏ số 0 thừa).</summary>
    public static string Format(long bytes)
    {
        if (bytes < 0) bytes = 0;
        if (bytes < 1024) return $"{bytes} B";
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        var text = Math.Round(value, 2).ToString("0.##", CultureInfo.InvariantCulture);
        return $"{text} {Units[unit]}";
    }
}
