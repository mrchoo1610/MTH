namespace DiskCleaner.Core.Models;

public enum DriveHealth { Ok, Warning, Critical }

public sealed record DriveSnapshot(string Name, string Label, long TotalBytes, long FreeBytes)
{
    public long UsedBytes => TotalBytes - FreeBytes;
    public double UsedPercent => TotalBytes == 0 ? 0 : UsedBytes * 100.0 / TotalBytes;
    public DriveHealth Health => (FreeBytes * 100.0 / Math.Max(TotalBytes, 1)) switch
    {
        < 5 => DriveHealth.Critical,
        < 15 => DriveHealth.Warning,
        _ => DriveHealth.Ok
    };
}
