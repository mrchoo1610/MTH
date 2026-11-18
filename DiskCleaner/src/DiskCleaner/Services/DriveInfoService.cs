using DiskCleaner.Core.Models;

namespace DiskCleaner.Services;

public sealed class DriveInfoService
{
    public IReadOnlyList<DriveSnapshot> GetFixedDrives()
    {
        var list = new List<DriveSnapshot>();
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                if (d.DriveType != DriveType.Fixed || !d.IsReady) continue;
                list.Add(new DriveSnapshot(d.Name, d.VolumeLabel, d.TotalSize, d.AvailableFreeSpace));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // ổ không đọc được: bỏ qua
            }
        }
        return list;
    }
}
